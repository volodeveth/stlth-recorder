using System.Runtime.InteropServices;
using Stlth.Core.Audio;

namespace Stlth.Core.Tests;

/// <summary>
/// Що лишається після перезапуску потоку, який не вдався, і після зміни пристрою,
/// де піднявся лише один бік.
/// </summary>
public class DeviceRestartTests
{
    private sealed class FakeStream : IDisposable
    {
        public bool Started { get; set; }
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    // ── StreamSlot: невдалий старт не лишає за собою напівживого потоку ─────────

    [Fact]
    public void A_successful_replace_disposes_the_old_stream_and_keeps_the_new_one()
    {
        var slot = new StreamSlot<FakeStream>();
        var old = new FakeStream();
        slot.Replace(() => old, s => s.Started = true);

        var fresh = new FakeStream();
        slot.Replace(() => fresh, s => s.Started = true);

        Assert.True(old.Disposed);
        Assert.Same(fresh, slot.Current);
        Assert.True(fresh.Started);
        Assert.False(fresh.Disposed);
        Assert.False(slot.NeedsRetry);
    }

    [Fact]
    public void A_stream_that_failed_to_start_is_disposed_and_not_kept()
    {
        var slot = new StreamSlot<FakeStream>();
        var old = new FakeStream();
        slot.Replace(() => old, _ => { });

        var broken = new FakeStream();
        Assert.Throws<UnauthorizedAccessException>(
            () => { slot.Replace(() => broken, _ => throw new UnauthorizedAccessException()); });

        Assert.True(old.Disposed);
        Assert.True(broken.Disposed);
        Assert.Null(slot.Current);
    }

    [Fact]
    public void A_failed_start_asks_for_another_attempt()
    {
        // Інакше watchdog бачить потік, що «ще не почав», і більше ніколи його не
        // перезапускає — доріжка мовчить до кінця сесії, навіть коли пристрій ожив.
        var slot = new StreamSlot<FakeStream>();

        Assert.Throws<COMException>(
            () => { slot.Replace(() => new FakeStream(), _ => throw new COMException()); });

        Assert.True(slot.NeedsRetry);
    }

    [Fact]
    public void A_device_that_cannot_even_be_opened_also_asks_for_another_attempt()
    {
        var slot = new StreamSlot<FakeStream>();
        var old = new FakeStream();
        slot.Replace(() => old, _ => { });

        Assert.Throws<COMException>(
            () => { slot.Replace(() => throw new COMException(), _ => { }); });

        Assert.True(old.Disposed);
        Assert.Null(slot.Current);
        Assert.True(slot.NeedsRetry);
    }

    [Fact]
    public void A_later_successful_replace_clears_the_retry()
    {
        var slot = new StreamSlot<FakeStream>();
        Assert.Throws<COMException>(
            () => { slot.Replace(() => new FakeStream(), _ => throw new COMException()); });

        var fresh = new FakeStream();
        slot.Replace(() => fresh, _ => { });

        Assert.False(slot.NeedsRetry);
        Assert.Same(fresh, slot.Current);
    }

    [Fact]
    public void A_new_slot_needs_no_retry()
    {
        var slot = new StreamSlot<FakeStream>();

        Assert.Null(slot.Current);
        Assert.False(slot.NeedsRetry);
    }

    // ── DeviceSwitch: кожен бік перемикається сам по собі ──────────────────────

    [Fact]
    public void A_failed_microphone_does_not_skip_the_speakers()
    {
        var restarted = new List<bool>();

        var result = DeviceSwitch.Apply("Headset Mic", "Headset Speakers", isLoopback =>
        {
            restarted.Add(isLoopback);
            if (!isLoopback)
            {
                throw new UnauthorizedAccessException();
            }

            return true;
        });

        Assert.Contains(true, restarted);
        Assert.Equal("Headset Speakers", result.OutputName);
        Assert.Null(result.InputName);
    }

    [Fact]
    public void Failed_speakers_do_not_hide_a_microphone_that_switched()
    {
        var result = DeviceSwitch.Apply("Headset Mic", "Headset Speakers", isLoopback =>
            isLoopback ? throw new COMException() : true);

        Assert.Equal("Headset Mic", result.InputName);
        Assert.Null(result.OutputName);
    }

    [Fact]
    public void A_device_is_named_only_when_recording_actually_moved_to_it()
    {
        // Ворота закриті — запис зупиняється, і перемикання не відбулося. meta.json
        // не має називати пристрій, на який ніхто не перейшов.
        var result = DeviceSwitch.Apply("Headset Mic", "Headset Speakers", _ => false);

        Assert.Null(result.InputName);
        Assert.Null(result.OutputName);
    }

    [Fact]
    public void Only_the_side_that_changed_is_restarted()
    {
        var restarted = new List<bool>();

        var result = DeviceSwitch.Apply(null, "Speakers", isLoopback =>
        {
            restarted.Add(isLoopback);
            return true;
        });

        Assert.Equal([true], restarted);
        Assert.Null(result.InputName);
        Assert.Equal("Speakers", result.OutputName);
    }

    [Fact]
    public void A_programming_error_during_the_switch_is_not_swallowed()
    {
        Assert.Throws<ArgumentException>(() => { DeviceSwitch.Apply("Mic", "Speakers", _ => throw new ArgumentException()); });
    }
}
