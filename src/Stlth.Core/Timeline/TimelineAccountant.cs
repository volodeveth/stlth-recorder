namespace Stlth.Core.Timeline;

/// <summary>
/// Тримає доріжку вирівняною з часом сесії.
///
/// Потік може стати: зміна пристрою, збій драйвера, сон системи. Якщо просто
/// дописувати ті пакети, що прийшли, файл виходить <b>коротшим</b> за сесію, і дві
/// доріжки розповзаються тим сильніше, чим більше було розривів. Тому кожен пакет
/// звіряється з таймстемпом, на якому його чекали, а прогалина заливається тишею.
///
/// Інваріант: <c>TotalFrames == тривалість × sampleRate</c> для кожної доріжки.
/// Тиша пишеться, а не вирізається.
/// </summary>
public sealed class TimelineAccountant
{
    /// <summary>Джитер планувальника нижче цього — норма, а не розрив.</summary>
    private const double JitterTolerance = 0.005;

    private readonly double _sampleRate;

    /// <summary>Момент, на якому чекають наступний пакет, у секундах.</summary>
    private double? _expectedNext;

    public TimelineAccountant(double sampleRate) => _sampleRate = sampleRate;

    /// <summary>Кадрів пораховано — записане аудіо плюс вставлена тиша.</summary>
    public long TotalFrames { get; private set; }

    /// <summary>
    /// Прив'язати доріжку до моменту старту сесії.
    ///
    /// Без цього таймлайн починався б із першого пакета, а все до нього втрачалося б
    /// замість того, щоб стати тишею. Це не теоретичний випадок: loopback не віддає
    /// нічого, доки жоден процес не заграв, тож розмова, яка почалася з паузи,
    /// зсунула б увесь системний канал.
    /// </summary>
    public void Start(double timestampSeconds) => _expectedNext = timestampSeconds;

    /// <summary>
    /// Скільки кадрів тиші вставити перед пакетом, що починається о
    /// <paramref name="timestampSeconds"/>.
    /// </summary>
    /// <param name="timestampSeconds">Початок пакета в секундах на монотонному годиннику.</param>
    /// <param name="frameCount">Скільки кадрів у самому пакеті.</param>
    /// <returns>Кадрів тиші перед ним; 0, якщо пакет іде впритул.</returns>
    public long FramesToInsertBefore(double timestampSeconds, int frameCount)
    {
        long pad = 0;
        if (_expectedNext is { } expected && timestampSeconds - expected > JitterTolerance)
        {
            pad = (long)Math.Round((timestampSeconds - expected) * _sampleRate);
        }

        _expectedNext = timestampSeconds + frameCount / _sampleRate;
        TotalFrames += pad + frameCount;
        return pad;
    }

    /// <summary>
    /// Скільки кадрів тиші дописати, щоб доріжка дотягнулася до
    /// <paramref name="timestampSeconds"/>.
    ///
    /// Викликається на зупинці: потік міг замовкнути задовго до неї, а файл однаково
    /// мусить бути завдовжки як сесія. Повертає 0, якщо доріжка вже там.
    /// </summary>
    public long FramesToReach(double timestampSeconds)
    {
        if (_expectedNext is not { } expected || timestampSeconds <= expected)
        {
            return 0;
        }

        var pad = (long)Math.Round((timestampSeconds - expected) * _sampleRate);
        if (pad <= 0)
        {
            return 0;
        }

        _expectedNext = timestampSeconds;
        TotalFrames += pad;
        return pad;
    }
}
