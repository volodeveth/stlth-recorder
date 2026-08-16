namespace Stlth.Core.Storage;

/// <summary>Сесія, яка зараз пишеться.</summary>
public sealed record SessionHandle(Guid Id, string Dir);

/// <summary>
/// Результат одного прогону захоплення — те, що движок передає в сховище.
/// </summary>
/// <param name="SystemAudioDetected">
/// Чи ніс системний канал справжнє аудіо. Єдиний надійний доказ того, що
/// співрозмовника взагалі було чути: тиша в цьому каналі може означати і мовчання,
/// і те, що звук грає на іншому пристрої.
/// </param>
public sealed record RecordingResult(
    string MicPath,
    string SystemPath,
    int DurationMs,
    string InputDeviceName,
    string OutputDeviceName,
    CaptureMode Mode,
    IReadOnlyList<DeviceChange> DeviceChanges,
    bool SystemAudioDetected);
