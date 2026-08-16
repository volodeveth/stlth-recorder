using System.Net;
using System.Net.Http.Headers;

namespace Stlth.Core.Transcription;

/// <summary>Модель, яку треба довантажити.</summary>
/// <param name="ApproxBytes">
/// Приблизна вага — щоб показати людині число до того, як почати качати півгігабайта,
/// і щоб малювати прогрес. <b>Не критерій цілісності:</b> моделі час від часу
/// перезаливають, і файл, що відрізняється на кілька сотень байтів від зашитої
/// константи, — це нова версія моделі, а не пошкоджений файл.
/// </param>
public sealed record ModelSpec(string Name, string Url, long ApproxBytes);

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
    /// Нижче цієї частки від очікуваної ваги файл вважається сміттям, а не моделлю.
    ///
    /// Поріг м'який навмисно: точний розмір належить серверу, а не нам.
    /// </summary>
    private const double MinimumShare = 0.9;

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
            574_041_195),
        new ModelSpec(
            "ggml-silero-v5.1.2.bin",
            "https://huggingface.co/ggml-org/whisper-vad/resolve/main/ggml-silero-v5.1.2.bin?download=true",
            885_098),
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
    public static long TotalBytes => Required.Sum(model => model.ApproxBytes);

    public bool IsInstalled => Required.All(model => IsComplete(PathOf(model), model));

    public string PathOf(ModelSpec model) => Path.Combine(Directory, model.Name);

    /// <summary>
    /// Модель, а не її недокачаний хвіст.
    ///
    /// У кінцеве ім'я файл потрапляє лише після перевіреного завантаження, тож саме
    /// існування файлу вже майже все каже. Поріг на розмір лишається запобіжником
    /// проти обірваного файлу від старих версій.
    /// </summary>
    public static bool IsComplete(string path, ModelSpec model)
    {
        var info = new FileInfo(path);
        return info.Exists && info.Length >= model.ApproxBytes * MinimumShare;
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

            done += model.ApproxBytes;
        }

        progress?.Report(1.0);
    }

    private async Task DownloadAsync(ModelSpec model,
                                     IProgress<long> progress,
                                     CancellationToken cancellation,
                                     bool fromScratch = false)
    {
        var target = PathOf(model);
        if (IsComplete(target, model))
        {
            progress.Report(model.ApproxBytes);
            return;
        }

        var partial = target + ".part";
        if (fromScratch && File.Exists(partial))
        {
            File.Delete(partial);
        }

        var have = new FileInfo(partial) is { Exists: true } info ? info.Length : 0;

        using var request = new HttpRequestMessage(HttpMethod.Get, model.Url);
        if (have > 0)
        {
            request.Headers.Range = new RangeHeaderValue(have, null);
        }

        using var response = await _http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellation);

        // 416 означає, що ми просимо байти за межею файлу — тобто наш хвіст уже не
        // коротший за те, що є на сервері. Якщо довжини збігаються, файл насправді
        // готовий: це рівно той випадок, коли зашита константа розміру розійшлася з
        // реальністю. Інакше хвіст чужий, і його треба викинути.
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            if (response.Content.Headers.ContentRange?.Length is { } total && have == total)
            {
                Finalise(partial, target);
                progress.Report(have);
                return;
            }

            if (!fromScratch)
            {
                await DownloadAsync(model, progress, cancellation, fromScratch: true);
                return;
            }
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new ModelInstallException(
                $"Не вдалося завантажити {model.Name}: сервер відповів {(int)response.StatusCode}");
        }

        // Сервер не підтримав продовження і почав спочатку — приймаємо це чесно,
        // а не дописуємо початок файлу в його середину.
        var append = have > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (!append)
        {
            have = 0;
        }

        // Скільки має вийти — знає сервер, а не ми.
        var expected = response.Content.Headers.ContentLength is { } length
            ? have + length
            : (long?)null;

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

        var actual = new FileInfo(partial).Length;
        if (expected is { } total2 && actual != total2)
        {
            throw new ModelInstallException(
                $"{model.Name} завантажився не повністю ({actual:N0} з {total2:N0} Б) — " +
                "спробуйте ще раз, завантаження продовжиться.");
        }

        Finalise(partial, target);
    }

    /// <summary>
    /// У кінцеве ім'я файл потрапляє лише цілим: інакше наступний запуск вважав би
    /// недокачану модель встановленою.
    /// </summary>
    private static void Finalise(string partial, string target) =>
        File.Move(partial, target, overwrite: true);
}
