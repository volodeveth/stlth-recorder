using Stlth.Core.Storage;
using Stlth.Core.Timeline;

namespace Stlth.Core.Audio;

/// <summary>
/// Пара «облік часу + письменник»: усе, що потрібно, щоб одна доріжка вийшла рівно
/// такої довжини, як сесія.
///
/// Винесено окремо від движка навмисно — саме тут живе інваріант, і його треба
/// перевіряти без аудіозаліза.
/// </summary>
public sealed class TrackRecorder : IDisposable
{
    private readonly WavWriter _writer;
    private readonly TimelineAccountant _timeline;
    private readonly object _gate = new();

    public TrackRecorder(string path, int channels, double startAt)
    {
        _writer = new WavWriter(path, channels);
        _timeline = new TimelineAccountant(AudioFormat.SampleRate);
        _timeline.Start(startAt);
    }

    public long TotalFrames
    {
        get
        {
            lock (_gate)
            {
                return _timeline.TotalFrames;
            }
        }
    }

    /// <summary>Записати пакет на його місце в таймлайні, заливши прогалину тишею.</summary>
    public void Write(ReadOnlySpan<byte> pcm, int frames, double atSeconds)
    {
        lock (_gate)
        {
            var pad = _timeline.FramesToInsertBefore(atSeconds, frames);
            if (pad > 0)
            {
                _writer.WriteSilence(pad);
            }

            _writer.Write(pcm);
        }
    }

    /// <summary>Дотягнути доріжку до вказаного моменту — викликається на зупинці.</summary>
    public void PadTo(double atSeconds)
    {
        lock (_gate)
        {
            var pad = _timeline.FramesToReach(atSeconds);
            if (pad > 0)
            {
                _writer.WriteSilence(pad);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _writer.Dispose();
        }
    }
}
