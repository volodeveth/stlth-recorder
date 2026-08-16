using Stlth.Core.Meetings;

namespace Stlth.Core.Tests;

public class MeetingDetectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 14, 0, 0, TimeSpan.FromHours(3));
    private static readonly Meeting Zoom = new("zoom", "Zoom");

    [Fact]
    public void A_brief_grab_of_the_microphone_is_not_a_meeting()
    {
        // Застосунки хапають мікрофон на мить, коли відкриваєш їхні налаштування звуку.
        Assert.Null(MeetingDetector.Decide(Zoom, Now.AddSeconds(-3), alreadyAnnounced: false, Now));
    }

    [Fact]
    public void Five_seconds_of_holding_makes_it_a_meeting()
        => Assert.NotNull(MeetingDetector.Decide(Zoom, Now.AddSeconds(-6), false, Now));

    [Fact]
    public void Exactly_the_threshold_counts()
        => Assert.NotNull(MeetingDetector.Decide(Zoom, Now - MeetingDetector.ConfirmationDelay, false, Now));

    [Fact]
    public void An_already_announced_meeting_is_not_announced_twice()
        => Assert.Null(MeetingDetector.Decide(Zoom, Now.AddSeconds(-60), alreadyAnnounced: true, Now));

    [Fact]
    public void Nothing_holding_the_microphone_means_nothing_to_announce()
        => Assert.Null(MeetingDetector.Decide(null, Now.AddSeconds(-60), false, Now));

    [Fact]
    public void Mute_does_not_end_a_meeting()
    {
        // Вимкнення мікрофона в дзвінку відпускає пристрій. Без витримки одна
        // розмова оголошувалася б заново після кожного mute.
        Assert.False(MeetingDetector.HasEnded(Now.AddSeconds(-30), Now));
        Assert.True(MeetingDetector.HasEnded(Now.AddSeconds(-61), Now));
    }

    [Fact]
    public void A_microphone_that_was_never_free_has_not_ended()
        => Assert.False(MeetingDetector.HasEnded(null, Now));

    [Fact]
    public void Conferencing_apps_are_recognised_with_or_without_the_extension()
    {
        Assert.True(MeetingDetector.IsMeetingProcess("zoom"));
        Assert.True(MeetingDetector.IsMeetingProcess("Zoom.exe"));
        Assert.True(MeetingDetector.IsMeetingProcess("ms-teams"));
        Assert.True(MeetingDetector.IsMeetingProcess("chrome.exe"));
    }

    [Fact]
    public void Dictation_and_recorders_do_not_trigger_a_reminder()
    {
        // Нагадування, що спрацьовує невлучно, привчає закривати нагадування не
        // читаючи — і тоді не спрацює те єдине, заради якого все робилося.
        Assert.False(MeetingDetector.IsMeetingProcess("notepad.exe"));
        Assert.False(MeetingDetector.IsMeetingProcess("SoundRecorder.exe"));
        Assert.False(MeetingDetector.IsMeetingProcess("Stlth.Cli"));
    }
}

public class MeetingWatcherTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 16, 14, 0, 0, TimeSpan.FromHours(3));
    private static readonly Meeting Zoom = new("zoom", "Zoom");
    private static readonly Meeting Teams = new("ms-teams", "Microsoft Teams");

    private sealed class Probe
    {
        public Meeting? Holder { get; set; }

        public Meeting? Read() => Holder;
    }

    [Fact]
    public void A_meeting_is_announced_once_the_delay_passes()
    {
        var probe = new Probe { Holder = Zoom };
        var watcher = new MeetingWatcher(probe.Read);
        var announced = new List<Meeting>();
        watcher.Started += announced.Add;

        watcher.Poll(Start);
        watcher.Poll(Start.AddSeconds(3));
        Assert.Empty(announced);

        watcher.Poll(Start.AddSeconds(6));
        Assert.Single(announced);
        Assert.Equal("Zoom", announced[0].AppName);
    }

    [Fact]
    public void A_meeting_is_announced_only_once()
    {
        var probe = new Probe { Holder = Zoom };
        var watcher = new MeetingWatcher(probe.Read);
        var announced = new List<Meeting>();
        watcher.Started += announced.Add;

        watcher.Poll(Start);
        for (var second = 6; second < 60; second += 6)
        {
            watcher.Poll(Start.AddSeconds(second));
        }

        Assert.Single(announced);
    }

    [Fact]
    public void Mute_and_unmute_do_not_announce_a_second_meeting()
    {
        // Без витримки живий прогін оголошує одну розмову двічі за півхвилини —
        // рівно стільки, скільки триває пауза з вимкненим мікрофоном.
        var probe = new Probe { Holder = Zoom };
        var watcher = new MeetingWatcher(probe.Read);
        var announced = new List<Meeting>();
        var ended = 0;
        watcher.Started += announced.Add;
        watcher.Ended += () => ended++;

        watcher.Poll(Start);
        watcher.Poll(Start.AddSeconds(6));       // оголошено

        probe.Holder = null;                     // вимкнув мікрофон
        watcher.Poll(Start.AddSeconds(20));
        watcher.Poll(Start.AddSeconds(40));

        probe.Holder = Zoom;                     // увімкнув назад
        watcher.Poll(Start.AddSeconds(50));
        watcher.Poll(Start.AddSeconds(70));

        Assert.Single(announced);
        Assert.Equal(0, ended);
    }

    [Fact]
    public void The_end_is_reported_after_the_grace_period()
    {
        var probe = new Probe { Holder = Zoom };
        var watcher = new MeetingWatcher(probe.Read);
        var ended = 0;
        watcher.Ended += () => ended++;

        watcher.Poll(Start);
        watcher.Poll(Start.AddSeconds(6));

        // Витримка рахується від першого спостереження вільного мікрофона, а не від
        // моменту, коли його відпустили: детектор знає лише те, що бачив. При
        // опитуванні раз на дві секунди різниця не перевищує двох секунд.
        probe.Holder = null;
        watcher.Poll(Start.AddSeconds(30));
        Assert.Equal(0, ended);

        watcher.Poll(Start.AddSeconds(80));
        Assert.Equal(0, ended);

        watcher.Poll(Start.AddSeconds(95));
        Assert.Equal(1, ended);
    }

    [Fact]
    public void An_unannounced_meeting_does_not_report_an_end()
    {
        // Хтось відкрив налаштування звуку на дві секунди — і нічого не сталося.
        var probe = new Probe { Holder = Zoom };
        var watcher = new MeetingWatcher(probe.Read);
        var ended = 0;
        watcher.Ended += () => ended++;

        watcher.Poll(Start);
        probe.Holder = null;
        watcher.Poll(Start.AddSeconds(2));
        watcher.Poll(Start.AddSeconds(90));

        Assert.Equal(0, ended);
    }

    [Fact]
    public void Another_app_taking_over_is_a_new_meeting()
    {
        var probe = new Probe { Holder = Zoom };
        var watcher = new MeetingWatcher(probe.Read);
        var announced = new List<Meeting>();
        watcher.Started += announced.Add;

        watcher.Poll(Start);
        watcher.Poll(Start.AddSeconds(6));

        probe.Holder = Teams;
        watcher.Poll(Start.AddSeconds(10));
        watcher.Poll(Start.AddSeconds(20));

        Assert.Equal(2, announced.Count);
        Assert.Equal("Microsoft Teams", announced[1].AppName);
    }

    [Fact]
    public void A_manual_recording_without_a_meeting_never_reports_one_ending()
    {
        // Побачено в бою: людина сама почала запис, жодної зустрічі не було — і через
        // хвилину застосунок спитав, чи зупинити запис «зустрічі, що завершилася».
        // Причина була в одному прапорці на два питання: приглушення нагадування про
        // старт виглядало для коду як «зустріч оголошено».
        var probe = new Probe { Holder = null };
        var watcher = new MeetingWatcher(probe.Read);
        var ended = 0;
        watcher.Ended += () => ended++;

        watcher.SuppressForCurrentMeeting();
        watcher.Poll(Start);
        watcher.Poll(Start.AddSeconds(30));
        watcher.Poll(Start.AddSeconds(120));

        Assert.Equal(0, ended);
    }

    [Fact]
    public void A_real_meeting_still_reports_its_end_even_if_the_reminder_was_suppressed()
    {
        // Зворотний бік тієї самої монети: якщо зустріч справді була, а людина почала
        // запис сама, нагадування про кінець потрібне — воно найважливіше з двох.
        var probe = new Probe { Holder = Zoom };
        var watcher = new MeetingWatcher(probe.Read);
        var announced = new List<Meeting>();
        var ended = 0;
        watcher.Started += announced.Add;
        watcher.Ended += () => ended++;

        watcher.Poll(Start);
        watcher.SuppressForCurrentMeeting();     // людина сама натиснула «Почати запис»
        watcher.Poll(Start.AddSeconds(10));      // зустріч підтверджена, але мовчки

        probe.Holder = null;
        watcher.Poll(Start.AddSeconds(20));
        watcher.Poll(Start.AddSeconds(90));

        Assert.Empty(announced);                 // про старт не нагадували
        Assert.Equal(1, ended);                  // про кінець — нагадали
    }

    [Fact]
    public void Suppressing_stops_the_reminder_for_the_current_meeting()
    {
        var probe = new Probe { Holder = Zoom };
        var watcher = new MeetingWatcher(probe.Read);
        var announced = new List<Meeting>();
        watcher.Started += announced.Add;

        watcher.Poll(Start);
        watcher.SuppressForCurrentMeeting();
        watcher.Poll(Start.AddSeconds(30));

        Assert.Empty(announced);
    }
}
