using System.IO;
using Stlth.Core.Storage;
using Stlth.Core.Transcription;

namespace Stlth.App;

/// <summary>
/// Розпізнає сесію у фоні одразу після зупинки запису.
///
/// <b>По одній за раз.</b> Розпізнавання завантажує процесор приблизно на стільки ж
/// часу, скільки тривала розмова, — і то на кожну доріжку. Дві сесії поспіль запустили
/// б два whisper паралельно, і обидва пішли б удвічі повільніше, ще й забравши машину
/// в людини, яка щойно поклала слухавку. Тому черга.
///
/// <b>Тихо, якщо нічого немає.</b> Без моделей це не робить нічого і нічого не питає:
/// пропозиція їх поставити живе в меню сесії, а не вискакує після кожного запису.
/// </summary>
internal static class TranscriptionService
{
    private static readonly SemaphoreSlim Queue = new(1, 1);

    /// <summary>Скільки сесій чекає на черзі, включно з тією, що зараз обробляється.</summary>
    private static int _pending;

    public static bool IsBusy => Volatile.Read(ref _pending) > 0;

    /// <summary>
    /// Поставити сесію в чергу на розпізнавання.
    /// </summary>
    /// <param name="onDone">
    /// Викликається після завершення: успіх — із шляхом до транскрипту, невдача — з
    /// поясненням. Помилка тут не робить сесію невдалою: аудіо на місці, а транскрипт
    /// завжди можна зібрати з меню руками.
    /// </param>
    public static void Enqueue(SessionStore store, string sessionDir, Action<string?, string?> onDone)
    {
        var transcriber = new Transcriber();
        if (!transcriber.IsAvailable)
        {
            return;
        }

        if (File.Exists(Path.Combine(sessionDir, Transcriber.FileName)))
        {
            return;
        }

        Interlocked.Increment(ref _pending);

        _ = Task.Run(async () =>
        {
            await Queue.WaitAsync();
            try
            {
                var path = await transcriber.TranscribeAsync(sessionDir);
                onDone(path, null);
            }
            catch (Exception e) when (e is TranscriptionException or IOException
                                           or UnauthorizedAccessException)
            {
                onDone(null, e.Message);
            }
            finally
            {
                Queue.Release();
                Interlocked.Decrement(ref _pending);
            }
        });
    }
}
