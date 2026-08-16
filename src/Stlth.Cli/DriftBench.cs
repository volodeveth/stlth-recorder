using System.Globalization;
using Stlth.Core;
using Stlth.Core.Audio;

namespace Stlth.Cli;

/// <summary>
/// Міряє, наскільки годинники двох аудіопристроїв розходяться зі спільною опорою QPC —
/// і, отже, один з одним.
///
/// <b>Чому саме так, а не клік-треком.</b> Класичний спосіб — пустити той самий
/// тест-сигнал у обидва тракти і знайти зсув крос-кореляцією. Але мікрофонний тракт
/// нічим замкнути: акустичний зв'язок «динаміки → мікрофон» вимірює ще й повітря, а
/// віртуальний драйвер у продукті заборонений і на робочій машині не ставиться.
///
/// Але питання, на яке насправді треба відповісти, вужче: <i>чи розходяться канали</i>.
/// А це — різниця темпів, з якими два пристрої віддають кадри відносно QPC, і її видно
/// з власної інструментації, без жодного заліза.
///
/// <b>Чого цей вимір НЕ доводить:</b> він не перевіряє акустичний шлях і не ловить
/// сталої затримки пристрою. Він відповідає лише на питання про накопичення — тобто
/// рівно на те, яке ставить вимога «менш ніж 300 мс за годину».
/// </summary>
internal static class DriftBench
{
    private sealed class Track(string label)
    {
        public string Label { get; } = label;

        public long Frames { get; private set; }

        public double FirstSeconds { get; private set; } = double.NaN;

        public List<(double Elapsed, double Offset)> Samples { get; } = [];

        public void Add(int frames, double timestampSeconds)
        {
            if (double.IsNaN(FirstSeconds))
            {
                FirstSeconds = timestampSeconds;
            }

            Frames += frames;
        }

        /// <summary>Наскільки годинник пристрою випередив QPC на цей момент, у секундах.</summary>
        public void Sample(double nowSeconds)
        {
            if (double.IsNaN(FirstSeconds))
            {
                return;
            }

            var elapsed = nowSeconds - FirstSeconds;
            if (elapsed <= 0)
            {
                return;
            }

            Samples.Add((elapsed, (Frames / (double)AudioFormat.SampleRate) - elapsed));
        }
    }

    public static int Run(string secondsArg, string intervalArg)
    {
        if (!double.TryParse(secondsArg, CultureInfo.InvariantCulture, out var seconds) || seconds <= 0)
        {
            Console.Error.WriteLine("Не число: " + secondsArg);
            return 1;
        }

        _ = double.TryParse(intervalArg, CultureInfo.InvariantCulture, out var interval);
        if (interval <= 0)
        {
            interval = 10;
        }

        var render = AudioDevices.DefaultRender();
        var capture = AudioDevices.DefaultCapture();
        if (render is null)
        {
            Console.Error.WriteLine("Немає пристрою відтворення.");
            return 1;
        }

        var origin = AudioDevices.NowSeconds();
        var tracks = new List<(Track Track, WasapiStream Stream)>();

        var systemTrack = new Track("system");
        var systemStream = new WasapiStream(render, loopback: true, AudioFormat.SystemChannels);
        systemStream.PacketCaptured += p => systemTrack.Add(p.FrameCount, p.TimestampSeconds);
        tracks.Add((systemTrack, systemStream));

        if (capture is not null)
        {
            var micTrack = new Track("mic");
            var micStream = new WasapiStream(capture, loopback: false, AudioFormat.MicChannels);
            micStream.PacketCaptured += p => micTrack.Add(p.FrameCount, p.TimestampSeconds);
            tracks.Add((micTrack, micStream));
        }

        Console.WriteLine($"Вимір дрейфу: {seconds / 60:F0} хв, проба кожні {interval:F0} с.");
        Console.WriteLine("Системний канал міряється лише поки щось грає — тримайте звук увімкненим.\n");

        foreach (var (_, stream) in tracks)
        {
            stream.Start(origin);
        }

        var deadline = AudioDevices.NowSeconds() + seconds;
        var nextSample = AudioDevices.NowSeconds() + interval;

        while (AudioDevices.NowSeconds() < deadline)
        {
            Thread.Sleep(200);

            var now = AudioDevices.NowSeconds();
            if (now < nextSample)
            {
                continue;
            }

            nextSample = now + interval;

            // Таймстемпи пакетів уже відраховані від origin, тож проба береться на
            // тій самій шкалі.
            foreach (var (track, _) in tracks)
            {
                track.Sample(now - origin);
            }

            Report(tracks, partial: true);
        }

        foreach (var (_, stream) in tracks)
        {
            stream.Stop();
        }

        Console.WriteLine();
        Report(tracks, partial: false);

        foreach (var (_, stream) in tracks)
        {
            stream.Dispose();
        }

        return 0;
    }

    private static void Report(List<(Track Track, WasapiStream Stream)> tracks, bool partial)
    {
        var rates = new List<(string Label, double MsPerHour, double Interval, int Points)>();

        foreach (var (track, _) in tracks)
        {
            var fit = Regression(track.Samples);
            rates.Add((track.Label, fit.SlopeMsPerHour, fit.HalfWidthMsPerHour, track.Samples.Count));
        }

        if (partial)
        {
            var line = string.Join("   ", rates.Select(r =>
                $"{r.Label}: {r.MsPerHour,+8:F1} мс/год"));
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {line}");
            return;
        }

        Console.WriteLine("Темп годинника пристрою відносно QPC");
        Console.WriteLine("────────────────────────────────────────────────────────");
        foreach (var (label, msPerHour, halfWidth, points) in rates)
        {
            Console.WriteLine($"{label,8}: {msPerHour,+9:F1} мс/год  (95% ДІ ±{halfWidth:F1}, точок {points})");
        }

        if (rates.Count == 2)
        {
            var difference = rates[0].MsPerHour - rates[1].MsPerHour;
            var combined = Math.Sqrt((rates[0].Interval * rates[0].Interval) +
                                     (rates[1].Interval * rates[1].Interval));
            Console.WriteLine("────────────────────────────────────────────────────────");
            Console.WriteLine($"РОЗБІЖНІСТЬ КАНАЛІВ: {Math.Abs(difference),8:F1} мс/год (95% ДІ ±{combined:F1})");
            Console.WriteLine($"Поріг вимоги:        {300.0,8:F1} мс/год");
            Console.WriteLine(Math.Abs(difference) + combined < 300
                ? "→ вимога виконується із запасом"
                : "→ ВИМОГА ПІД ПИТАННЯМ, потрібен довший прогін");
        }
    }

    /// <summary>
    /// Лінійна регресія зсуву за часом.
    ///
    /// Не «перша точка проти останньої»: оцінка по двох точках не є вимірюванням.
    /// На короткій базі довірчий інтервал виходить ширшим за сам ефект, і це видно
    /// лише тоді, коли інтервал справді рахують.
    /// </summary>
    private static (double SlopeMsPerHour, double HalfWidthMsPerHour) Regression(
        List<(double Elapsed, double Offset)> samples)
    {
        if (samples.Count < 3)
        {
            return (0, double.PositiveInfinity);
        }

        var n = samples.Count;
        var meanX = samples.Average(s => s.Elapsed);
        var meanY = samples.Average(s => s.Offset);
        var sxx = samples.Sum(s => (s.Elapsed - meanX) * (s.Elapsed - meanX));
        if (sxx <= 0)
        {
            return (0, double.PositiveInfinity);
        }

        var sxy = samples.Sum(s => (s.Elapsed - meanX) * (s.Offset - meanY));
        var slope = sxy / sxx;
        var intercept = meanY - (slope * meanX);

        var residual = samples.Sum(s =>
        {
            var predicted = intercept + (slope * s.Elapsed);
            var error = s.Offset - predicted;
            return error * error;
        });

        var standardError = Math.Sqrt(residual / (n - 2) / sxx);

        // 1.96 — нормальне наближення; при десятках точок різниця зі Стьюдентом
        // менша за саму похибку вимірювання.
        return (slope * 3600 * 1000, 1.96 * standardError * 3600 * 1000);
    }
}
