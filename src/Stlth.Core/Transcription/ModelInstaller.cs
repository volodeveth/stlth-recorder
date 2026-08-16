using System.Security.Cryptography;

namespace Stlth.Core.Transcription;

/// <summary>Модель, яку треба довантажити.</summary>
/// <param name="Bytes">Очікуваний розмір — перша й найдешевша перевірка цілісності.</param>
public sealed record ModelSpec(string Name, string Url, long Bytes, string? Sha256 = null);

public sealed class ModelInstallException(string message) : Exception(message);

/// <summary>
/// Довантажує моделі розпізнавання на вимогу.
///
/// <b>Чому не всередині застосунку.</b> Півгігабайта моделей у рекордері, чия основна
/// робота їх не потребує, — це півгігабайта, які качає кожен, кому транскрибація не
/// потрібна. Запис працює без них; транскрибація просто не з'являється в меню.
///
/// <b>Завантаження з продовженням.</b> Півгігабайта по поганому каналу без відновлення —
/// це функція, якою не скористаються: одна обірвана спроба, і людина більше не
/// натисне. Тому файл тягнеться у <c>.part</c> із заголовком Range, і наступна спроба
/// починає з того місця, де зупинилася.
/// </summary>
public sealed class ModelInstaller
{
    /// <summary>
    /// Обидві моделі потрібні, і друга — не опція.
    ///
    /// Whisper на вхідному вікні завжди щось декодує: дай йому тишу — отримаєш
    /// правдоподібне речення, якого ніхто не казав. А зустріч здебільшого з тиші й
    /// складається. VAD ріже запис на ділянки з мовленням ще до розпізнавання, і це
    /// виграш за всіма осями одразу — точніше й швидше.
    /// </summary>
    public static IReadOnlyList<ModelSpec> Required { get; } =
    [
        new ModelSpec(
            "ggml-large-v3-turbo-q5_0.bin",
            "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3-turbo-q5_0.bin?download=true",
            574_041_600),
        new ModelSpec(
            "ggml-silero-v5.1.2.bin",
            "https://huggingface.co/ggml-org/whisper-vad/resolve/main/ggml-silero-v5.1.2.bin?download=true",
            884_800),
    ];

    private readonly HttpClient _http;

    public ModelInstaller(string? directory = null, HttpClient? http = null)
    {
        Directory = directory ?? DefaultDirectory;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
    }

    public static string DefaultDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "STLTH Recorder",
        "models");

    public string Directory { get; }

    /// <summary>Скільки важать усі моделі разом — це число показують до завантаження.</summary>
    public static long TotalBytes => Required.Sum(model => model.Bytes);

    public bool IsInstalled => Required.All(model => IsComplete(PathOf(model), model));

    public string PathOf(ModelSpec model) => Path.Combine(Directory, model.Name);

    /// <summary>Модель, а не її недокачаний хвіст.</summary>
    public static bool IsComplete(string path, ModelSpec model)
    {
        var info = new FileInfo(path);

        // Розмір — перша перевірка: обірваний файл виглядає як модель і поводиться
        // як сміття, а помітити це на етапі розпізнавання набагато дорожче.
        return info.Exists && info.Length == model.Bytes;
    }

    /// <param name="progress">Частка від 0 до 1 по всіх моделях разом.</param>
    public async Task InstallAsync(IProgress<double>? progress = null,
                                   CancellationToken cancellation = default)
    {
        System.IO.Directory.CreateDirectory(Directory);

        var done = 0L;
        foreach (var model in Required)
        {
            var alreadyDone = done;
            await DownloadAsync(
                model,
                new Progress<long>(bytes => progress?.Report(
                    Math.Min(1.0, (alreadyDone + bytes) / (double)TotalBytes))),
                cancellation);

            done += model.Bytes;
        }

        progress?.Report(1.0);
    }

    private async Task DownloadAsync(ModelSpec model,
                                     IProgress<long> progress,
                                     CancellationToken cancellation)
    {
        var target = PathOf(model);
        if (IsComplete(target, model))
        {
            progress.Report(model.Bytes);
            return;
        }

        var partial = target + ".part";
        var have = new FileInfo(partial) is { Exists: true } info ? info.Length : 0;

        // Хвіст довший за модель — це не наш хвіст.
        if (have > model.Bytes)
        {
            File.Delete(partial);
            have = 0;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, model.Url);
        if (have > 0)
        {
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(have, null);
        }

        using var response = await _http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellation);

        // Сервер не підтримав продовження і почав спочатку — приймаємо це чесно,
        // а не дописуємо початок файлу в його середину.
        var append = have > 0 && response.StatusCode == System.Net.HttpStatusCode.PartialContent;
        if (!append)
        {
            have = 0;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new ModelInstallException(
                $"Не вдалося завантажити {model.Name}: сервер відповів {(int)response.StatusCode}");
        }

        await using (var source = await response.Content.ReadAsStreamAsync(cancellation))
        await using (var destination = new FileStream(
            partial, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 256 * 1024, useAsync: true))
        {
            var buffer = new byte[256 * 1024];
            int read;
            while ((read = await source.ReadAsync(buffer, cancellation)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellation);
                have += read;
                progress.Report(have);
            }
        }

        if (new FileInfo(partial).Length != model.Bytes)
        {
            throw new ModelInstallException(
                $"{model.Name} завантажився не повністю — спробуйте ще раз, завантаження продовжиться.");
        }

        if (model.Sha256 is { } expected && !Matches(partial, expected))
        {
            File.Delete(partial);
            throw new ModelInstallException($"{model.Name} пошкоджений при завантаженні.");
        }

        // У кінцеве ім'я файл потрапляє лише цілим: інакше наступний запуск вважав
        // би недокачану модель встановленою.
        File.Move(partial, target, overwrite: true);
    }

    private static bool Matches(string path, string expected)
    {
        using var stream = File.OpenRead(path);
        var hash = Convert.ToHexString(SHA256.HashData(stream));
        return hash.Equals(expected, StringComparison.OrdinalIgnoreCase);
    }
}
