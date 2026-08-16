using NAudio.MediaFoundation;
using NAudio.Wave;
using Stlth.Core.Storage;

namespace Stlth.Core.Mixdown;

public sealed class MixerException(string message) : Exception(message);

/// <summary>
/// Складає дві доріжки сесії в один файл, який можна просто послухати як розмову.
///
/// <b>Похідне, ніколи не джерело правди.</b> <c>mic.wav</c> і <c>system.wav</c>
/// лишаються недоторканими; зведення існує тільки для людського вуха. Помилка тут не
/// робить сесію невдалою і не блокує зупинку запису.
///
/// <b>Вирівнювання не робиться, і воно не потрібне.</b> Обидві доріжки покладені на
/// одну шкалу QPC, а всі розриви вже залиті тишею — вони однакової довжини і
/// вирівняні посемплово. Шукати тут зсув означало б лікувати проблему, яку
/// архітектура вже усунула.
/// </summary>
public static class SessionMixer
{
    public const string FileName = "session.m4a";

    /// <summary>
    /// Наскільки кожен голос відсунутий від центру.
    ///
    /// Не жорстке панорамування. Повне розділення найлегше розбирати — миттєво чути,
    /// хто кого перебив, — але година такого стомлює, а на одній колонці один зі
    /// співрозмовників зникає повністю. При 85/15 розділення лишається очевидним, і
    /// обидва голоси переживають зведення в моно.
    /// </summary>
    private const float Dominant = 0.85f;

    private const float Bleed = 0.15f;

    /// <summary>
    /// Запас перед кодуванням.
    ///
    /// Не тому, що дві доріжки в сумі перевищують одиницю — при такому паноруванні
    /// вони перекриваються лише частково. Причина інша: <b>AAC дає міжсемплові викиди
    /// вище шкали при декодуванні</b>. Мікс, що впирається рівно в 1.0, повертається
    /// з-за кодека вищим і загортається.
    /// </summary>
    private const float Headroom = 0.8f;

    /// <summary>AAC для мови: ~43 МБ на годину замість ~1 ГБ вихідних доріжок.</summary>
    private const int Bitrate = 96_000;

    /// <summary>
    /// Один вихідний кадр із пари вхідних. Чиста функція — форма міксу перевіряється
    /// без жодного файлу.
    /// </summary>
    public static (float Left, float Right) Frame(float me, float peer)
    {
        var left = ((me * Dominant) + (peer * Bleed)) * Headroom;
        var right = ((peer * Dominant) + (me * Bleed)) * Headroom;
        return (Math.Clamp(left, -1f, 1f), Math.Clamp(right, -1f, 1f));
    }

    public static string MixPath(string sessionDir) => Path.Combine(sessionDir, FileName);

    public static bool MixExists(string sessionDir) => File.Exists(MixPath(sessionDir));

    /// <param name="force">Перебудувати, навіть якщо зведення вже є.</param>
    public static string Mix(string sessionDir, bool force = false)
    {
        var target = MixPath(sessionDir);
        if (!force && File.Exists(target))
        {
            return target;
        }

        var micPath = Path.Combine(sessionDir, Track.Mic.File);
        var systemPath = Path.Combine(sessionDir, Track.System.File);

        foreach (var (path, name) in new[] { (micPath, Track.Mic.File), (systemPath, Track.System.File) })
        {
            if (!File.Exists(path))
            {
                throw new MixerException($"У теці сесії немає {name}");
            }

            // Сесія, під час якої застосунок убили, несе заголовок із нульовим
            // розміром. Частина читачів його толерує — тобто зведення вдалося б і
            // виглядало нормальним, просто обірваним. Лікуємо завжди, а не лише
            // під час відновлення.
            WavRepair.RepairIfNeeded(path);
        }

        MediaFoundationApi.Startup();

        // Розширення мусить лишитися .m4a: Media Foundation добирає кодувальник саме
        // за ним, і звичне «писати в .part, потім перейменувати» тут падає з
        // «was not able to create a sink writer for this file extension».
        var temporary = Path.Combine(sessionDir, "session.part.m4a");
        DeleteQuietly(temporary);

        try
        {
            using (var provider = new MixProvider(micPath, systemPath))
            {
                CheckSpace(provider.TotalFrames, sessionDir);
                MediaFoundationEncoder.EncodeToAac(provider, temporary, Bitrate);
            }

            // У ціль потрапляє лише готовий файл: напівзаписане зведення не має
            // виглядати для меню завершеним.
            DeleteQuietly(target);
            File.Move(temporary, target);
            return target;
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or ArgumentException)
        {
            DeleteQuietly(temporary);
            throw new MixerException($"Не вдалося записати зведений файл: {e.Message}");
        }
    }

    /// <summary>AAC 96 кбіт/с — це ~12 КБ на секунду; просимо вдвічі більше перед стартом.</summary>
    private static void CheckSpace(long frames, string sessionDir)
    {
        var seconds = frames / (double)AudioFormat.SampleRate;
        var need = Math.Max((long)(seconds * 12_000) * 2, DiskGuard.CriticalThreshold);

        if (DiskGuard.FreeBytes(sessionDir) <= need)
        {
            throw new MixerException($"Замало місця для зведеного файлу (потрібно ≈ {need / 1_048_576} МБ)");
        }
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }

    /// <summary>
    /// Потік, який читає обидві доріжки і віддає готовий стерео-мікс.
    ///
    /// Кодеру нічого не треба знати про сесію — він просто тягне кадри, доки вони є.
    /// </summary>
    private sealed class MixProvider : IWaveProvider, IDisposable
    {
        private readonly WaveFileReader _mic;
        private readonly WaveFileReader _system;

        public MixProvider(string micPath, string systemPath)
        {
            _mic = new WaveFileReader(micPath);
            _system = new WaveFileReader(systemPath);

            // Мікс завдовжки як довша доріжка: та, що скінчилася раніше, далі просто
            // мовчить. Але за інваріантом вони й так однакові — це запобіжник, а не
            // очікуваний випадок.
            TotalFrames = Math.Max(_mic.SampleCount, _system.SampleCount);
        }

        public long TotalFrames { get; }

        public WaveFormat WaveFormat { get; } = new(AudioFormat.SampleRate, AudioFormat.BitsPerSample, 2);

        public int Read(byte[] buffer, int offset, int count)
        {
            var frames = count / 4;                 // стерео, 16 біт
            var written = 0;

            for (var i = 0; i < frames; i++)
            {
                var me = ReadFrame(_mic);
                var peer = ReadFrame(_system);

                if (me is null && peer is null)
                {
                    break;
                }

                var (left, right) = Frame(me ?? 0f, peer ?? 0f);
                var position = offset + (i * 4);

                BitConverter.TryWriteBytes(buffer.AsSpan(position), (short)(left * short.MaxValue));
                BitConverter.TryWriteBytes(buffer.AsSpan(position + 2), (short)(right * short.MaxValue));
                written += 4;
            }

            return written;
        }

        public void Dispose()
        {
            _mic.Dispose();
            _system.Dispose();
        }

        /// <summary>
        /// Один кадр як моно-значення; <c>null</c>, коли доріжка скінчилася.
        ///
        /// Стерео-доріжка співрозмовника згортається в моно <b>перед</b> паноруванням:
        /// інакше два боки прийшли б у мікс із різними рівнями.
        /// </summary>
        private static float? ReadFrame(WaveFileReader reader)
        {
            var samples = reader.ReadNextSampleFrame();
            if (samples is null || samples.Length == 0)
            {
                return null;
            }

            var sum = 0f;
            foreach (var sample in samples)
            {
                sum += sample;
            }

            return sum / samples.Length;
        }
    }
}
