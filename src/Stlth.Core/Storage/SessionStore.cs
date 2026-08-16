using System.Reflection;

namespace Stlth.Core.Storage;

/// <summary>
/// Володіє розкладкою записів на диску:
/// <c>%LOCALAPPDATA%\STLTH Recorder\Sessions\&lt;UUID&gt;\{mic.wav,system.wav,meta.json}</c>
///
/// Нічого не залишає цієї теки: застосунок не робить мережевих запитів.
/// </summary>
public sealed class SessionStore
{
    public static string DefaultRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "STLTH Recorder",
        "Sessions");

    public SessionStore(string? root = null) => Root = root ?? DefaultRoot;

    public string Root { get; }

    /// <summary>
    /// Створити теку сесії і записати <c>meta.json</c> зі статусом «пишеться».
    ///
    /// Згода фіксується <b>до</b> того, як записано перший семпл: сенс потоку саме в
    /// тому, що підтвердження і його час лежать у записі.
    /// </summary>
    public SessionHandle Begin(DateTimeOffset consentAt, string inputDevice, string outputDevice)
    {
        var id = Guid.NewGuid();
        var dir = Path.Combine(Root, id.ToString());
        Directory.CreateDirectory(dir);

        var meta = new SessionMeta
        {
            SessionId = id,
            StartedAt = DateTimeOffset.Now,
            DurationMs = 0,
            Status = SessionStatus.Recording,
            Consent = new Consent(true, consentAt),
            Tracks = [Track.Mic, Track.System],
            // Пристрої пишуться наперед, а не лише на завершенні: сесія, яку крах
            // лишає перерваною, — саме та, де знати їх найважливіше.
            Devices = new Devices(inputDevice, outputDevice),
            DeviceChanges = [],
            AppVersion = AppVersion,
            OsVersion = Environment.OSVersion.Version.ToString(),
        };

        SessionJson.WriteAtomic(meta, MetaPath(dir));
        return new SessionHandle(id, dir);
    }

    /// <summary>Завершити сесію: тривалість, пристрої, статус.</summary>
    public void Complete(SessionHandle handle, RecordingResult result)
        => Update(handle.Dir, meta =>
        {
            meta.Status = SessionStatus.Completed;
            meta.DurationMs = result.DurationMs;
            meta.Devices = new Devices(result.InputDeviceName, result.OutputDeviceName);
            meta.Tracks = [Track.Mic, Track.System];
            meta.CaptureMode = result.Mode;
            meta.DeviceChanges = result.DeviceChanges;
        });

    /// <summary>
    /// Позначити сесію перерваною — коли движок не піднявся або помер посеред запису,
    /// щоб напівзаписана сесія ніколи не лишалася такою, що нібито «пишеться».
    /// </summary>
    public void Interrupt(SessionHandle handle)
        => Update(handle.Dir, meta =>
        {
            meta.Status = SessionStatus.Interrupted;
            if (meta.DurationMs == 0)
            {
                meta.DurationMs = DurationMsOfAudioIn(handle.Dir);
            }
        });

    public void AppendDeviceChange(SessionHandle handle, DateTimeOffset at, string? input, string? output)
        => Update(handle.Dir, meta => meta.DeviceChanges = [.. meta.DeviceChanges, new DeviceChange(at, input, output)]);

    public void Delete(Guid id)
    {
        var dir = Path.Combine(Root, id.ToString());
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// Усі сесії, найновіші перші. Нечитабельні теки пропускаються, а не валять
    /// увесь перелік: одна зіпсована сесія не має ховати решту.
    /// </summary>
    public IReadOnlyList<SessionMeta> List()
    {
        if (!Directory.Exists(Root))
        {
            return [];
        }

        var result = new List<SessionMeta>();
        foreach (var dir in Directory.EnumerateDirectories(Root))
        {
            var meta = TryLoad(dir);
            if (meta is not null)
            {
                result.Add(meta);
            }
        }

        result.Sort((a, b) => b.StartedAt.CompareTo(a.StartedAt));
        return result;
    }

    /// <summary>
    /// Відновити сесії, які крах лишив у стані «пишеться».
    ///
    /// Аудіо йшло на диск потоково, тож файли цілі — справжня тривалість це просто
    /// те, скільки аудіо туди встигло лягти.
    /// </summary>
    /// <returns>
    /// Теки, які справді полікували, — щоб той, хто викликав, міг перебудувати
    /// похідне від них (зведення). Повертати їх означає лишити це рішення поза
    /// ядром, якому не належить читати налаштування.
    /// </returns>
    public IReadOnlyList<string> RecoverInterrupted()
    {
        if (!Directory.Exists(Root))
        {
            return [];
        }

        var recovered = new List<string>();

        foreach (var dir in Directory.EnumerateDirectories(Root))
        {
            var meta = TryLoad(dir);
            if (meta is null || meta.Status == SessionStatus.Completed)
            {
                continue;
            }

            // Спершу заголовки: вбитий письменник не дописав розміри, і файл
            // читається не всіма. Ідемпотентно, тож уже перервані сесії теж лікуються.
            var repaired = false;
            foreach (var track in new[] { Track.Mic.File, Track.System.File })
            {
                repaired |= WavRepair.RepairIfNeeded(Path.Combine(dir, track));
            }

            if (meta.Status != SessionStatus.Recording && !repaired)
            {
                continue;
            }

            meta.Status = SessionStatus.Interrupted;
            meta.DurationMs = DurationMsOfAudioIn(dir);
            SessionJson.WriteAtomic(meta, MetaPath(dir));
            recovered.Add(dir);
        }

        return recovered;
    }

    /// <summary>
    /// Чи можна видаляти вихідні доріжки після розпізнавання.
    ///
    /// Чиста функція, бо це рішення про безповоротну втрату даних, і воно мусить бути
    /// перевіреним, а не вгаданим по ходу.
    ///
    /// Умова друга — не формальність. Транскрипт без жодної репліки означає, що
    /// розпізнати не вдалося або говорити не було кому. Видалити аудіо в цей момент —
    /// найгірший можливий результат: запис зник, а натомість лишився файл із написом
    /// «мовлення не розпізнано».
    /// </summary>
    public static bool MayRemoveAudio(bool enabled, bool transcriptHasSpeech)
        => enabled && transcriptHasSpeech;

    /// <summary>
    /// Видалити вихідні доріжки, лишивши все похідне: зведення, транскрипт, метадані.
    /// </summary>
    /// <returns>Скільки байтів звільнено.</returns>
    public long RemoveAudio(string sessionDir)
    {
        long freed = 0;

        foreach (var name in new[] { Track.Mic.File, Track.System.File })
        {
            var path = Path.Combine(sessionDir, name);
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists)
                {
                    continue;
                }

                freed += info.Length;
                File.Delete(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Файл зайнятий — лишається на місці. Наступного разу видалиться.
            }
        }

        if (freed > 0)
        {
            try
            {
                Update(sessionDir, meta => meta.AudioRemovedAt = DateTimeOffset.Now);
            }
            catch (Exception e) when (e is IOException or System.Text.Json.JsonException)
            {
            }
        }

        return freed;
    }

    /// <summary>
    /// Зафіксувати, що поруч із доріжками з'явилося зведення.
    ///
    /// Пишеться постфактум, а не в <see cref="Complete"/>: зведення робиться у фоні і
    /// ніколи не має затримувати кінець запису.
    /// </summary>
    public void NoteMix(string sessionDir, string fileName)
    {
        try
        {
            Update(sessionDir, meta => meta.MixFile = fileName);
        }
        catch (IOException)
        {
            // Похідний файл не вартий того, щоб через нього щось падало.
        }
    }

    private static string MetaPath(string dir) => Path.Combine(dir, "meta.json");

    private static SessionMeta? TryLoad(string dir)
    {
        try
        {
            return SessionJson.Load(MetaPath(dir));
        }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException
                                       or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void Update(string dir, Action<SessionMeta> mutate)
    {
        var meta = SessionJson.Load(MetaPath(dir));
        mutate(meta);
        SessionJson.WriteAtomic(meta, MetaPath(dir));
    }

    /// <summary>
    /// Тривалість теки сесії, взята з <b>коротшої</b> доріжки — точки, далі за яку
    /// обох каналів уже немає.
    /// </summary>
    private static int DurationMsOfAudioIn(string dir)
    {
        var frames = new[] { Track.Mic.File, Track.System.File }
            .Select(name => WavWriter.FramesInFile(Path.Combine(dir, name)))
            .Where(f => f > 0)
            .ToArray();

        if (frames.Length == 0)
        {
            return 0;
        }

        return (int)Math.Round(frames.Min() * 1000.0 / AudioFormat.SampleRate);
    }

    private static string AppVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3)
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
        ?? "0.0.0";
}
