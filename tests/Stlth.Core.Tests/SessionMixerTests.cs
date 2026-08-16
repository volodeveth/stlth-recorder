using Stlth.Core.Mixdown;
using Stlth.Core.Storage;

namespace Stlth.Core.Tests;

public class SessionMixerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory().FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>Сесія з двох доріжок: у мікрофоні тон, у системному — тиша.</summary>
    private void WriteSession(long frames, short micLevel = 8000, short systemLevel = 0)
    {
        using (var mic = new WavWriter(Path.Combine(_dir, Track.Mic.File), AudioFormat.MicChannels))
        {
            mic.Write(Tone(frames, AudioFormat.MicChannels, micLevel));
        }

        using var system = new WavWriter(Path.Combine(_dir, Track.System.File), AudioFormat.SystemChannels);
        system.Write(Tone(frames, AudioFormat.SystemChannels, systemLevel));
    }

    private static byte[] Tone(long frames, int channels, short level)
    {
        var bytes = new byte[frames * AudioFormat.BytesPerFrame(channels)];
        for (var frame = 0; frame < frames; frame++)
        {
            var value = (short)(Math.Sin(frame * 0.05) * level);
            for (var channel = 0; channel < channels; channel++)
            {
                var offset = ((frame * channels) + channel) * 2;
                BitConverter.TryWriteBytes(bytes.AsSpan(offset), value);
            }
        }

        return bytes;
    }

    [Fact]
    public void Panning_is_85_15_not_hard()
    {
        var (left, right) = SessionMixer.Frame(me: 1f, peer: 0f);

        Assert.True(left > right);                          // я ліворуч
        Assert.True(right > 0);                             // але й праворуч чутно
        Assert.Equal(0.15f / 0.85f, right / left, 3);       // задане співвідношення
    }

    [Fact]
    public void The_peer_is_panned_the_other_way()
    {
        var (left, right) = SessionMixer.Frame(me: 0f, peer: 1f);

        Assert.True(right > left);
        Assert.True(left > 0);
    }

    [Fact]
    public void Both_voices_survive_a_mono_downmix()
    {
        // Причина, з якої панорамування не жорстке: на одній колонці канали
        // складаються, і при повному розділенні один зі співрозмовників зник би.
        var (left, right) = SessionMixer.Frame(me: 1f, peer: 0f);

        Assert.True(left + right > 0.5f);
    }

    [Fact]
    public void Two_full_scale_sources_do_not_clip()
    {
        var (left, right) = SessionMixer.Frame(1f, 1f);

        Assert.InRange(left, -1f, 1f);
        Assert.InRange(right, -1f, 1f);
    }

    [Fact]
    public void Headroom_is_applied_before_encoding()
    {
        // Не через суму каналів — при такому паноруванні перекриття невелике, — а
        // тому що AAC дає міжсемплові викиди вище шкали при декодуванні. Мікс, що
        // впирається рівно в 1.0, повертається з-за кодека вищим і загортається.
        var (left, _) = SessionMixer.Frame(1f, 1f);

        Assert.True(left <= 0.8f + 1e-5f);
    }

    [Fact]
    public void Silence_stays_silence()
    {
        var (left, right) = SessionMixer.Frame(0f, 0f);

        Assert.Equal(0f, left);
        Assert.Equal(0f, right);
    }

    [Fact]
    public void Mix_produces_a_playable_file()
    {
        WriteSession(AudioFormat.SampleRate * 2);

        var path = SessionMixer.Mix(_dir);

        Assert.True(File.Exists(path));
        Assert.EndsWith(SessionMixer.FileName, path);
        // AAC на 96 кбіт/с — приблизно 12 КБ на секунду; порожній контейнер був би
        // на порядок меншим.
        Assert.True(new FileInfo(path).Length > 8_000);
    }

    [Fact]
    public void No_partial_file_is_left_behind()
    {
        // Кодувальник Media Foundation добирається за розширенням, тож тимчасовий
        // файл теж мусить бути .m4a — і не мусить лишатися в теці сесії.
        WriteSession(AudioFormat.SampleRate);

        SessionMixer.Mix(_dir);

        Assert.Empty(Directory.GetFiles(_dir, "*part*"));
    }

    [Fact]
    public void Mix_is_idempotent_without_force()
    {
        WriteSession(AudioFormat.SampleRate);
        var path = SessionMixer.Mix(_dir);
        var stamp = File.GetLastWriteTimeUtc(path);

        Thread.Sleep(30);
        SessionMixer.Mix(_dir);

        Assert.Equal(stamp, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void Force_rebuilds_an_existing_mix()
    {
        WriteSession(AudioFormat.SampleRate);
        SessionMixer.Mix(_dir);

        var rebuilt = SessionMixer.Mix(_dir, force: true);

        Assert.True(File.Exists(rebuilt));
    }

    [Fact]
    public void A_missing_track_gives_a_typed_error_in_ukrainian()
    {
        using (var mic = new WavWriter(Path.Combine(_dir, Track.Mic.File), AudioFormat.MicChannels))
        {
            mic.WriteSilence(48000);
        }

        var error = Assert.Throws<MixerException>(() => SessionMixer.Mix(_dir));

        Assert.Contains(Track.System.File, error.Message);
    }

    [Fact]
    public void A_crashed_session_is_repaired_before_mixing()
    {
        // Заголовок із нульовим розміром читається не всіма: без лікування зведення
        // вдалося б і виглядало нормальним, просто обірваним.
        WriteSession(AudioFormat.SampleRate * 2);
        foreach (var name in new[] { Track.Mic.File, Track.System.File })
        {
            using var stream = new FileStream(Path.Combine(_dir, name), FileMode.Open, FileAccess.Write);
            stream.Seek(40, SeekOrigin.Begin);
            stream.Write(new byte[4]);
        }

        var path = SessionMixer.Mix(_dir);

        Assert.True(new FileInfo(path).Length > 8_000);
    }

    [Fact]
    public void Mix_exists_reports_the_derived_file()
    {
        WriteSession(AudioFormat.SampleRate);
        Assert.False(SessionMixer.MixExists(_dir));

        SessionMixer.Mix(_dir);

        Assert.True(SessionMixer.MixExists(_dir));
    }
}
