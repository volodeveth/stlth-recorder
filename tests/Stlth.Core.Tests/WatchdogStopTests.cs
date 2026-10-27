using System.Runtime.InteropServices;
using Stlth.Core.Audio;

namespace Stlth.Core.Tests;

/// <summary>
/// Зупинка запису не має перетинатися з перевіркою watchdog.
///
/// Перевірка, що вже почалася на пулі потоків, дочитувала годинник пристрою, коли
/// потоки вже звільнили свої COM-об'єкти, і процес падав саме на «Зупинити» — разом
/// зі зведенням і транскрибацією сесії, що щойно записалася.
/// </summary>
public class WatchdogStopTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    [Fact]
    public void Stop_waits_for_a_check_that_is_already_running()
    {
        using var entered = new ManualResetEventSlim();
        var finished = false;

        var watchdog = new WatchdogTimer(() =>
        {
            entered.Set();
            Thread.Sleep(300);
            Volatile.Write(ref finished, true);
        }, TimeSpan.FromMilliseconds(20));

        Assert.True(entered.Wait(Wait), "the check never ran");

        watchdog.Stop();

        Assert.True(Volatile.Read(ref finished),
                    "Stop returned while a check was still touching the streams");
    }

    [Fact]
    public void No_check_starts_after_stop_returns()
    {
        var count = 0;
        var watchdog = new WatchdogTimer(() => Interlocked.Increment(ref count),
                                         TimeSpan.FromMilliseconds(5));

        SpinWait.SpinUntil(() => Volatile.Read(ref count) >= 3, Wait);
        watchdog.Stop();
        var atStop = Volatile.Read(ref count);

        Thread.Sleep(200);

        Assert.Equal(atStop, Volatile.Read(ref count));
    }

    [Fact]
    public void Stopping_twice_and_disposing_after_stop_is_harmless()
    {
        var watchdog = new WatchdogTimer(() => { }, TimeSpan.FromMilliseconds(10));

        watchdog.Stop();
        watchdog.Stop();
        watchdog.Dispose();
    }

    [Fact]
    public void A_released_com_object_reads_as_an_unavailable_clock()
    {
        // Саме цей виняток звалив процес: він не є COMException, тож старий фільтр
        // його пропускав.
        Assert.Equal(0UL, DeviceClock.ReadOrZero(() => throw new InvalidComObjectException()));
    }

    [Fact]
    public void A_vanished_device_reads_as_an_unavailable_clock()
    {
        Assert.Equal(0UL, DeviceClock.ReadOrZero(() => throw new COMException()));
        Assert.Equal(0UL, DeviceClock.ReadOrZero(() => throw new InvalidOperationException()));
        Assert.Equal(0UL, DeviceClock.ReadOrZero(() => throw new NullReferenceException()));
    }

    [Fact]
    public void A_working_clock_is_passed_through()
        => Assert.Equal(123_456UL, DeviceClock.ReadOrZero(() => 123_456UL));

    [Fact]
    public void An_unrelated_fault_is_not_swallowed()
    {
        // «Годинник недоступний» — це про пристрій, а не про будь-яку помилку в коді.
        Assert.Throws<ArgumentException>(() => DeviceClock.ReadOrZero(() => throw new ArgumentException()));
    }
}
