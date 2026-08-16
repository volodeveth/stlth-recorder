using Stlth.Core.Audio;

namespace Stlth.Core.Tests;

public class WatchdogRuleTests
{
    private static readonly TimeSpan LongSilence = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ShortPause = TimeSpan.FromSeconds(1);

    [Fact]
    public void Silence_before_the_stream_ever_started_is_not_a_fault()
    {
        // Loopback не віддає нічого, доки жоден процес не заграв. «Нуль пакетів»
        // саме по собі — НЕ ознака поломки, і watchdog на голому лічильнику
        // перезапускав би граф на кожній зустрічі, що починається з тиші.
        Assert.False(WatchdogRule.ShouldRestart(streamStarted: false, deviceRunning: true, LongSilence));
    }

    [Fact]
    public void A_stopped_device_is_not_restarted_either()
        => Assert.False(WatchdogRule.ShouldRestart(streamStarted: true, deviceRunning: false, LongSilence));

    [Fact]
    public void Running_device_with_no_packets_is_the_real_fault_signature()
        => Assert.True(WatchdogRule.ShouldRestart(streamStarted: true, deviceRunning: true, LongSilence));

    [Fact]
    public void A_normal_pause_in_conversation_is_not_a_fault()
        => Assert.False(WatchdogRule.ShouldRestart(streamStarted: true, deviceRunning: true, ShortPause));

    [Fact]
    public void Threshold_is_three_seconds()
    {
        Assert.False(WatchdogRule.ShouldRestart(true, true, TimeSpan.FromSeconds(2.9)));
        Assert.True(WatchdogRule.ShouldRestart(true, true, TimeSpan.FromSeconds(3.1)));
    }
}
