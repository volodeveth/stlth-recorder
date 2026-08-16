namespace Stlth.Core.Timeline;

/// <summary>
/// Тримає доріжку вирівняною з часом сесії.
///
/// Потік може стати: зміна пристрою, збій драйвера, сон системи. Якщо просто
/// дописувати ті пакети, що прийшли, файл виходить <b>коротшим</b> за сесію, і дві
/// доріжки розповзаються тим сильніше, чим більше було розривів. Тому кожен пакет
/// кладеться на позицію, яка випливає з його таймстемпа, а прогалина заливається тишею.
///
/// Інваріант: <c>TotalFrames == тривалість × sampleRate</c> для кожної доріжки.
/// Тиша пишеться, а не вирізається.
///
/// <b>Облік абсолютний, а не інкрементний.</b> Позиція кожного пакета рахується від
/// початку сесії, а не додається до попередньої. Різниця не косметична: інкрементний
/// облік округлює на кожному кроці, і на тридцятисекундному записі це вже дало дві
/// доріжки, що відрізнялися на 2 кадри. Помилка мікроскопічна, але вона <i>накопичується</i>,
/// і на годинній розмові з неї виріс би реальний розсинхрон. Абсолютний облік не
/// накопичує нічого: обидві доріжки, дотягнуті до одного моменту, виходять рівно
/// однакової довжини.
/// </summary>
public sealed class TimelineAccountant
{
    private readonly double _sampleRate;

    /// <summary>Джитер планувальника нижче цього — норма, а не розрив.</summary>
    private readonly long _jitterFrames;

    /// <summary>Момент старту сесії; усі позиції відраховуються від нього.</summary>
    private double _origin;

    private bool _started;

    public TimelineAccountant(double sampleRate)
    {
        _sampleRate = sampleRate;
        _jitterFrames = (long)Math.Round(0.005 * sampleRate);
    }

    /// <summary>Кадрів пораховано — записане аудіо разом із вставленою тишею.</summary>
    public long TotalFrames { get; private set; }

    /// <summary>
    /// Прив'язати доріжку до моменту старту сесії.
    ///
    /// Без цього таймлайн починався б із першого пакета, а все до нього втрачалося б
    /// замість того, щоб стати тишею. Це не теоретичний випадок: WASAPI loopback не
    /// віддає нічого, доки жоден процес не заграв, тож розмова, яка почалася з паузи,
    /// зсунула б увесь системний канал.
    /// </summary>
    public void Start(double timestampSeconds)
    {
        _origin = timestampSeconds;
        _started = true;
    }

    /// <summary>
    /// Скільки кадрів тиші вставити перед пакетом, що починається о
    /// <paramref name="timestampSeconds"/>.
    /// </summary>
    /// <param name="timestampSeconds">Початок пакета в секундах на монотонному годиннику.</param>
    /// <param name="frameCount">Скільки кадрів у самому пакеті.</param>
    /// <returns>Кадрів тиші перед ним; 0, якщо пакет іде впритул.</returns>
    public long FramesToInsertBefore(double timestampSeconds, int frameCount)
    {
        var pad = 0L;
        if (_started)
        {
            var behind = PositionOf(timestampSeconds) - TotalFrames;

            // Пакет із таймстемпом раніше очікуваного не переписує минуле: драйвер,
            // який віддав зіпсований qpcPosition, інакше зсунув би всю решту доріжки.
            // Такий пакет просто дописується впритул.
            if (behind > _jitterFrames)
            {
                pad = behind;
            }
        }

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
        if (!_started)
        {
            return 0;
        }

        var pad = PositionOf(timestampSeconds) - TotalFrames;
        if (pad <= 0)
        {
            return 0;
        }

        TotalFrames += pad;
        return pad;
    }

    /// <summary>Кадр, на якому має стояти доріжка в цей момент часу.</summary>
    private long PositionOf(double timestampSeconds)
        => (long)Math.Round((timestampSeconds - _origin) * _sampleRate);
}
