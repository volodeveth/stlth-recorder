namespace Stlth.Core.Storage;

public enum SessionStatus
{
    Recording,
    Completed,
    Interrupted,
}

/// <summary>Як були захоплені доріжки — діагностика для звіту про дрейф.</summary>
public enum CaptureMode
{
    /// <summary>
    /// Норма: обидва потоки прив'язані до спільної шкали QPC. У Windows немає
    /// аналога a shared-clock device, тож синхронність тримає спільна опора плюс
    /// інваріант таймлайна — і саме тому вона вимірюється, а не декларується.
    /// </summary>
    QpcAnchored,

    /// <summary>
    /// Мікрофона немає або дозвіл не видано: <c>mic.wav</c> пишеться тишею повної
    /// довжини, щоб сесія лишалася цілісною, а не зникала.
    /// </summary>
    SystemOnly,
}

/// <summary>Факт згоди і момент, коли вона була підтверджена.</summary>
public sealed record Consent(bool Confirmed, DateTimeOffset At);

/// <summary>Одна доріжка сесії.</summary>
public sealed record Track(
    string Channel,
    string Speaker,
    string File,
    string Format,
    int SampleRate,
    int Channels)
{
    public static readonly Track Mic =
        new("mic", "me", "mic.wav", "wav/lpcm", AudioFormat.SampleRate, AudioFormat.MicChannels);

    public static readonly Track System =
        new("system", "peer", "system.wav", "wav/lpcm", AudioFormat.SampleRate, AudioFormat.SystemChannels);
}

public sealed record Devices(string Input, string Output);

/// <summary>Пристрій, на який перемкнулися посеред запису.</summary>
public sealed record DeviceChange(DateTimeOffset At, string? Input, string? Output);

/// <summary>
/// <c>meta.json</c> — запис сесії на диску.
///
/// Схема свідомо збігається з another platform-версією продукту: сесії обох платформ мають
/// читатися одним кодом.
/// </summary>
public sealed class SessionMeta
{
    public Guid SessionId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public int DurationMs { get; set; }
    public SessionStatus Status { get; set; }
    public Consent Consent { get; set; } = new(false, DateTimeOffset.MinValue);
    public IReadOnlyList<Track> Tracks { get; set; } = [];
    public Devices Devices { get; set; } = new(string.Empty, string.Empty);
    public IReadOnlyList<DeviceChange> DeviceChanges { get; set; } = [];

    /// <summary>Як саме захоплювали. Опційне: сесії старших версій його не мають.</summary>
    public CaptureMode? CaptureMode { get; set; }

    /// <summary>
    /// Назва зведеного файлу, якщо його вже побудовано.
    ///
    /// Опційне навмисно: зведення робиться у фоні вже після того, як сесія стала
    /// завершеною, і його відсутність не робить сесію неповною. Джерело правди —
    /// дві вихідні доріжки.
    /// </summary>
    public string? MixFile { get; set; }

    public string AppVersion { get; set; } = "0.0.0";
    public string OsVersion { get; set; } = string.Empty;
}
