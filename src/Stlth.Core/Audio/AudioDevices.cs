using System.Diagnostics;
using NAudio.CoreAudioApi;

namespace Stlth.Core.Audio;

/// <summary>
/// Пристрої і час — дві речі, без яких не піднімається жоден потік.
///
/// <b>Про час.</b> Мікрофон і системний вивід — два різні пристрої з двома різними
/// годинниками, і покласти їх на один системного механізму немає. Спільною опорою
/// служить QPC: WASAPI віддає для кожного пакета <c>qpcPosition</c>, і обидва потоки
/// зводяться на одну шкалу за цим значенням, а не за порядком надходження.
///
/// Одиниці тут — головна пастка. WASAPI рахує в сотнях наносекунд, а
/// <see cref="Stopwatch"/> — у тіках власної частоти. Обидва походять від того самого
/// QPC, але переплутати їх означає помилитися в мільйони разів, і на око цього не
/// видно. Тому перерахунок живе рівно в одному місці — тут.
/// </summary>
public static class AudioDevices
{
    /// <summary>Скільки одиниць WASAPI-часу в одній секунді (100 нс).</summary>
    private const double WasapiTicksPerSecond = 10_000_000.0;

    /// <summary>Поточний момент на шкалі QPC, у секундах.</summary>
    public static double NowSeconds() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    /// <summary>Момент пакета, який WASAPI віддав у сотнях наносекунд, — у секундах.</summary>
    public static double PacketSeconds(long qpcPosition) => qpcPosition / WasapiTicksPerSecond;

    public static MMDevice? DefaultRender() => TryGetDefault(DataFlow.Render);

    public static MMDevice? DefaultCapture() => TryGetDefault(DataFlow.Capture);

    public static string NameOf(MMDevice? device)
    {
        if (device is null)
        {
            return "—";
        }

        try
        {
            return device.FriendlyName;
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            return "—";
        }
    }

    private static MMDevice? TryGetDefault(DataFlow flow)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            return enumerator.HasDefaultAudioEndpoint(flow, Role.Console)
                ? enumerator.GetDefaultAudioEndpoint(flow, Role.Console)
                : null;
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            return null;
        }
    }
}
