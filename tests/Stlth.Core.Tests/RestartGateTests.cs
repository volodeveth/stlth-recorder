using System.Runtime.InteropServices;
using Stlth.Core.Audio;

namespace Stlth.Core.Tests;

/// <summary>
/// Перезапуск потоку приходить з двох незалежних потоків — від watchdog і від
/// сповіщення про зміну пристрою за замовчуванням. Обидва мають іти по черзі, і
/// жоден не має підняти потік після того, як запис зупинено.
/// </summary>
public class RestartGateTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    [Fact]
    public void Restarts_from_different_threads_never_overlap()
    {
        var gate = new RestartGate();
        var inside = 0;
        var maxInside = 0;

        void Restart()
        {
            var now = Interlocked.Increment(ref inside);
            InterlockedMax(ref maxInside, now);
            Thread.Sleep(20);
            Interlocked.Decrement(ref inside);
        }

        var threads = Enumerable.Range(0, 4)
            .Select(_ => new Thread(() =>
            {
                for (var i = 0; i < 5; i++)
                {
                    gate.TryRun(Restart);
                }
            }))
            .ToList();

        threads.ForEach(t => t.Start());
        threads.ForEach(t => Assert.True(t.Join(Wait)));

        Assert.Equal(1, maxInside);
    }

    [Fact]
    public void A_closed_gate_refuses_to_restart()
    {
        var gate = new RestartGate();
        var ran = false;

        gate.Close();

        Assert.False(gate.TryRun(() => ran = true));
        Assert.False(ran);
    }

    [Fact]
    public void An_open_gate_runs_the_restart()
    {
        var gate = new RestartGate();
        var ran = false;

        Assert.True(gate.TryRun(() => ran = true));
        Assert.True(ran);
    }

    [Fact]
    public void Close_waits_for_a_restart_that_is_already_running()
    {
        var gate = new RestartGate();
        using var entered = new ManualResetEventSlim();
        var finished = false;

        var worker = new Thread(() => gate.TryRun(() =>
        {
            entered.Set();
            Thread.Sleep(300);
            Volatile.Write(ref finished, true);
        }));
        worker.Start();

        Assert.True(entered.Wait(Wait), "the restart never ran");

        gate.Close();

        Assert.True(Volatile.Read(ref finished),
                    "Close returned while a restart was still building a stream");
        Assert.True(worker.Join(Wait));
    }

    [Fact]
    public void A_restart_queued_behind_close_does_not_run()
    {
        // Сповіщення про пристрій пройшло перевірку «ще не зупинено» і чекає на
        // свою чергу, поки йде зупинка. Коли черга дійде — запис уже скінчився.
        var gate = new RestartGate();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var lateRan = false;

        var first = new Thread(() => gate.TryRun(() =>
        {
            entered.Set();
            release.Wait(Wait);
        }));
        first.Start();
        Assert.True(entered.Wait(Wait));

        var lateResult = true;
        var late = new Thread(() => lateResult = gate.TryRun(() => lateRan = true));
        late.Start();

        var closer = new Thread(gate.Close);
        closer.Start();

        // Закриття мусить уже чекати, коли перший перезапуск відпустить ворота.
        Thread.Sleep(300);
        release.Set();

        Assert.True(first.Join(Wait));
        Assert.True(closer.Join(Wait));
        Assert.True(late.Join(Wait));

        Assert.False(lateRan);
        Assert.False(lateResult);
    }

    [Fact]
    public void Closing_twice_is_harmless()
    {
        var gate = new RestartGate();
        gate.Close();
        gate.Close();
    }

    [Fact]
    public void A_fault_inside_the_restart_still_frees_the_gate()
    {
        var gate = new RestartGate();

        Assert.Throws<COMException>(() => { gate.TryRun(() => throw new COMException()); });

        Assert.True(gate.TryRun(() => { }));
    }

    [Fact]
    public void Denied_microphone_access_is_a_device_fault()
    {
        // Engine.Start уже ловить цей виняток, а перезапуск під час запису — ні:
        // вимкнений у налаштуваннях дозвіл на мікрофон валив процес.
        Assert.True(RestartFault.IsDeviceFault(new UnauthorizedAccessException()));
    }

    [Fact]
    public void Vanished_or_released_devices_are_device_faults()
    {
        Assert.True(RestartFault.IsDeviceFault(new COMException()));
        Assert.True(RestartFault.IsDeviceFault(new InvalidOperationException()));
        Assert.True(RestartFault.IsDeviceFault(new InvalidComObjectException()));
    }

    [Fact]
    public void Programming_errors_are_not_device_faults()
    {
        Assert.False(RestartFault.IsDeviceFault(new ArgumentException()));
        Assert.False(RestartFault.IsDeviceFault(new NullReferenceException()));
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int seen;
        while (value > (seen = Volatile.Read(ref target)))
        {
            if (Interlocked.CompareExchange(ref target, value, seen) == seen)
            {
                return;
            }
        }
    }
}
