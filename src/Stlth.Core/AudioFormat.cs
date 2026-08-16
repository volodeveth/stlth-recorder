namespace Stlth.Core;

/// <summary>
/// Один формат для обох доріжок.
///
/// 48 кГц / 16 біт LPCM — те, що читає будь-який плеєр без кодеків, і те, чого
/// чекає інваріант таймлайна: кількість семплів у файлі рівно дорівнює
/// тривалості сесії, помноженій на цю частоту.
/// </summary>
public static class AudioFormat
{
    public const int SampleRate = 48000;
    public const int BitsPerSample = 16;

    /// <summary>Мій голос: мікрофон моно.</summary>
    public const int MicChannels = 1;

    /// <summary>Співрозмовник: системний вивід стерео.</summary>
    public const int SystemChannels = 2;

    public static int BytesPerFrame(int channels) => channels * (BitsPerSample / 8);
}
