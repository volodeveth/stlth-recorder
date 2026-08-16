using Stlth.Core.Storage;

namespace Stlth.Core.Audio;

/// <summary>
/// Те, що контролеру потрібно від движка захоплення.
///
/// Справжня реалізація — <c>AudioEngine</c>; тести підставляють підробку, щоб
/// state machine перевірялася без аудіозаліза.
/// </summary>
public interface IAudioEngine : IDisposable
{
    void Start();

    RecordingResult Stop();
}
