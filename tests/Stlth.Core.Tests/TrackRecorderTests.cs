using Stlth.Core.Audio;
using Stlth.Core.Storage;

namespace Stlth.Core.Tests;

/// <summary>
/// Перевіряє інваріант там, де він насправді живе: у зшиванні обліку часу з
/// письменником. Аудіозаліза для цього не потрібно — потрібна лише послідовність
/// пакетів із таймстемпами.
/// </summary>
public class TrackRecorderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory().FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string At(string name) => Path.Combine(_dir, name);

    private static void Feed(TrackRecorder recorder, int channels, params (double At, int Frames)[] packets)
    {
        foreach (var (at, frames) in packets)
        {
            recorder.Write(new byte[frames * AudioFormat.BytesPerFrame(channels)], frames, at);
        }
    }

    [Fact]
    public void File_length_equals_session_duration_regardless_of_gaps()
    {
        var path = At("mic.wav");
        using (var recorder = new TrackRecorder(path, AudioFormat.MicChannels, startAt: 0))
        {
            Feed(recorder, AudioFormat.MicChannels, (0.0, 4800), (2.5, 4800), (2.6, 4800));
            recorder.PadTo(10.0);
        }

        Assert.Equal(10 * 48000, WavWriter.FramesInFile(path));
    }

    [Fact]
    public void A_track_that_never_received_a_packet_is_still_full_length()
    {
        // Саме це робить loopback, коли співрозмовник мовчав усю розмову.
        var path = At("system.wav");
        using (var recorder = new TrackRecorder(path, AudioFormat.SystemChannels, startAt: 0))
        {
            recorder.PadTo(8.0);
        }

        Assert.Equal(8 * 48000, WavWriter.FramesInFile(path));
    }

    [Fact]
    public void Two_tracks_fed_differently_end_up_the_same_length()
    {
        var mic = At("m.wav");
        var system = At("s.wav");

        using (var micTrack = new TrackRecorder(mic, AudioFormat.MicChannels, 0))
        using (var systemTrack = new TrackRecorder(system, AudioFormat.SystemChannels, 0))
        {
            Feed(micTrack, AudioFormat.MicChannels, (0.0, 48000));
            Feed(systemTrack, AudioFormat.SystemChannels, (5.0, 4800)); // заговорив на 5-й секунді
            micTrack.PadTo(30.0);
            systemTrack.PadTo(30.0);
        }

        Assert.Equal(WavWriter.FramesInFile(mic), WavWriter.FramesInFile(system));
        Assert.Equal(30 * 48000, WavWriter.FramesInFile(mic));
    }

    [Fact]
    public void Ten_minutes_of_silence_do_not_shift_the_timeline()
    {
        // Критерій приймання: мікрофон вимкнено 10 хвилин, а таймлайн не зсувається.
        // Тиша має бути записана як тиша, і клік після неї — опинитися на своєму місці.
        var path = At("mute.wav");
        using (var recorder = new TrackRecorder(path, AudioFormat.MicChannels, 0))
        {
            Feed(recorder, AudioFormat.MicChannels, (0.0, 48000), (601.0, 48000));
            recorder.PadTo(602.0);
        }

        Assert.Equal(602 * 48000, WavWriter.FramesInFile(path));
    }

    [Fact]
    public async Task Reading_the_length_while_the_pump_writes_does_not_corrupt_it()
    {
        // Доріжку завжди пише один потік-пампер по порядку — це контракт. А от
        // читати довжину може інший (UI, watchdog), і від цього результат не має
        // залежати.
        var path = At("race.wav");
        using (var recorder = new TrackRecorder(path, AudioFormat.MicChannels, 0))
        {
            var reader = Task.Run(() =>
            {
                for (var i = 0; i < 500; i++)
                {
                    _ = recorder.TotalFrames;
                }
            });

            for (var i = 0; i < 100; i++)
            {
                recorder.Write(new byte[960], 480, i * 0.01);
            }

            await reader;
            recorder.PadTo(5.0);
        }

        Assert.Equal(5 * 48000, WavWriter.FramesInFile(path));
    }

    [Fact]
    public void A_driver_reporting_a_stale_timestamp_does_not_stretch_the_track()
    {
        // Не гіпотетичний випадок: qpcPosition приходить від драйвера, і не кожен
        // заповнює його коректно. Доріжка однаково має бути завдовжки як сесія.
        var path = At("stale.wav");
        using (var recorder = new TrackRecorder(path, AudioFormat.MicChannels, 0))
        {
            Feed(recorder, AudioFormat.MicChannels, (0.0, 48000), (0.3, 480), (1.02, 480));
            recorder.PadTo(6.0);
        }

        Assert.Equal(6 * 48000, WavWriter.FramesInFile(path));
    }
}
