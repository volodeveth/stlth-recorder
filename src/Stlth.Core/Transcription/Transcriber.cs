using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Stlth.Core.Storage;

namespace Stlth.Core.Transcription;

public sealed class TranscriptionException(string message) : Exception(message);

/// <summary>
/// Локальна транскрибація сесії через whisper.cpp.
///
/// Аудіо не залишає комп'ютер — ні під час розпізнавання, ні для завантаження: із
/// мережі тягнуться <b>лише моделі</b>, і лише на явну дію людини.
///
/// <b>Атрибуція реплік не вгадується.</b> Більшість пайплайнів витрачає на це окрему
/// модель діаризації з власною похибкою. Тут вона не потрібна: <c>mic.wav</c> — це
/// завжди я, <c>system.wav</c> — завжди співрозмовник. Те, що зазвичай є задачею, тут
/// є властивістю запису.
/// </summary>
public sealed partial class Transcriber
{
    public const string FileName = "transcript.md";

    /// <summary>
    /// <c>ERROR_VIRUS_INFECTED</c> — цим кодом Windows відповідає, коли запуск
    /// заблокувала політика Application Control, а не антивірус.
    /// </summary>
    private const int BlockedByPolicy = 4551;

    private readonly string _executable;
    private readonly ModelInstaller _models;

    public Transcriber(string? executable = null, ModelInstaller? models = null)
    {
        _executable = executable ?? DefaultExecutable;
        _models = models ?? new ModelInstaller();
    }

    /// <summary>
    /// <c>whisper-cli.exe</c> кладеться поруч із застосунком: це виконуваний файл, а
    /// не модель, і важить одиниці мегабайтів.
    /// </summary>
    public static string DefaultExecutable { get; } = Path.Combine(
        AppContext.BaseDirectory, "whisper", "whisper-cli.exe");

    public bool IsAvailable => File.Exists(_executable) && _models.IsInstalled;

    /// <summary>Чому транскрибація недоступна — щоб меню могло сказати це прямо.</summary>
    public string? UnavailableReason
    {
        get
        {
            if (!File.Exists(_executable))
            {
                return "Не знайдено whisper-cli.exe поруч із застосунком.";
            }

            return _models.IsInstalled
                ? null
                : $"Моделі не встановлені (потрібно ≈ {ModelInstaller.TotalBytes / 1_048_576} МБ).";
        }
    }

    /// <returns>Шлях до <c>transcript.md</c>.</returns>
    public async Task<string> TranscribeAsync(string sessionDir,
                                              IProgress<string>? progress = null,
                                              CancellationToken cancellation = default)
    {
        if (UnavailableReason is { } reason)
        {
            throw new TranscriptionException(reason);
        }

        var lines = new List<Line>();

        foreach (var (track, speaker) in new[]
        {
            (Track.Mic, "Я"),
            (Track.System, "Співрозмовник"),
        })
        {
            var audio = Path.Combine(sessionDir, track.File);
            if (!File.Exists(audio))
            {
                continue;
            }

            // Крах міг лишити заголовок без розмірів — whisper прочитав би такий файл
            // як обірваний і мовчки видав би шматок розмови.
            WavRepair.RepairIfNeeded(audio);

            progress?.Report($"Розпізнаю: {speaker.ToLowerInvariant()}…");
            lines.AddRange(await RunAsync(audio, speaker, cancellation));
        }

        lines.Sort((a, b) => a.At.CompareTo(b.At));

        var target = Path.Combine(sessionDir, FileName);
        await File.WriteAllTextAsync(target, Render(sessionDir, lines), Encoding.UTF8, cancellation);
        return target;
    }

    private async Task<List<Line>> RunAsync(string audio, string speaker, CancellationToken cancellation)
    {
        var vad = _models.PathOf(ModelInstaller.Required[1]);

        var arguments = new List<string>
        {
            "-m", _models.PathOf(ModelInstaller.Required[0]),
            "-f", audio,
            "-l", "uk",
            "--output-txt", "false",
            "--no-prints",
            // VAD — не оздоба: без нього модель заповнює тишу вигадкою, а зустріч
            // здебільшого з тиші й складається.
            "--vad",
            "--vad-model", vad,
            "--vad-threshold", "0.5",
            // Придушення нерозмовних токенів прибирає решту сміття, яке VAD пропустив.
            "--suppress-nst",
        };

        var startInfo = new ProcessStartInfo(_executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process process;
        try
        {
            process = Process.Start(startInfo)
                ?? throw new TranscriptionException("Не вдалося запустити whisper-cli.exe");
        }
        catch (System.ComponentModel.Win32Exception e) when (e.NativeErrorCode == BlockedByPolicy)
        {
            // Smart App Control блокує непідписані сторонні бінарники наглухо: у нього
            // немає «дозволити один раз», а вимкнути його можна лише незворотно.
            // Тому тут не «спробуйте ще раз», а пояснення, що саме сталося.
            throw new TranscriptionException(
                "Windows заблокував whisper-cli.exe політикою Smart App Control. " +
                "Транскрибація недоступна, доки цей файл не підписаний або політику не змінено. " +
                "Запис і зведення від цього не залежать.");
        }

        using var _ = process;

        var output = await process.StandardOutput.ReadToEndAsync(cancellation);
        var errors = await process.StandardError.ReadToEndAsync(cancellation);
        await process.WaitForExitAsync(cancellation);

        if (process.ExitCode != 0)
        {
            throw new TranscriptionException(
                $"whisper-cli завершився з кодом {process.ExitCode}: {errors.Trim()}");
        }

        return Parse(output, speaker);
    }

    /// <summary>Рядки виду <c>[00:00:12.340 --&gt; 00:00:15.000]   текст</c>.</summary>
    internal static List<Line> Parse(string output, string speaker)
    {
        var lines = new List<Line>();

        foreach (Match match in TimestampPattern().Matches(output))
        {
            var text = match.Groups["text"].Value.Trim();
            if (text.Length == 0)
            {
                continue;
            }

            if (TimeSpan.TryParseExact(match.Groups["from"].Value, @"hh\:mm\:ss\.fff",
                                       CultureInfo.InvariantCulture, out var at))
            {
                lines.Add(new Line(at, speaker, text));
            }
        }

        return lines;
    }

    internal static string Render(string sessionDir, IReadOnlyList<Line> lines)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# Транскрипт сесії {Path.GetFileName(sessionDir)}");
        builder.AppendLine();
        builder.AppendLine("Розпізнано локально, whisper.cpp. Ролі не вгадані моделлю: доріжка");
        builder.AppendLine("мікрофона — це завжди я, доріжка системного звуку — співрозмовник.");
        builder.AppendLine();

        if (lines.Count == 0)
        {
            builder.AppendLine("_Мовлення не розпізнано._");
            return builder.ToString();
        }

        var lastSpeaker = string.Empty;
        foreach (var line in lines)
        {
            if (line.Speaker != lastSpeaker)
            {
                builder.AppendLine();
                builder.AppendLine($"**{line.Speaker}**");
                lastSpeaker = line.Speaker;
            }

            builder.AppendLine($"- `{line.At:hh\\:mm\\:ss}` {line.Text}");
        }

        return builder.ToString();
    }

    internal readonly record struct Line(TimeSpan At, string Speaker, string Text);

    [GeneratedRegex(@"\[(?<from>\d{2}:\d{2}:\d{2}\.\d{3})\s*-->\s*(?<to>\d{2}:\d{2}:\d{2}\.\d{3})\]\s*(?<text>[^\r\n]*)")]
    private static partial Regex TimestampPattern();
}
