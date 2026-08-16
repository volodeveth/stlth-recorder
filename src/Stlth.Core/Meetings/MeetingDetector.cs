namespace Stlth.Core.Meetings;

/// <summary>Застосунок, який зараз тримає мікрофон.</summary>
public readonly record struct Meeting(string ProcessName, string AppName);

/// <summary>
/// Правила, за якими утримання мікрофона стає «зустріччю».
///
/// Винесені окремо від опитування пристроїв навмисно: це чисті функції, і саме вони
/// вирішують, чи побачить людина нагадування. Їх треба вміти перевірити без заліза.
///
/// <b>Чому взагалі мікрофон.</b> Зустріч має ознаку, якої жоден застосунок для дзвінків
/// не може уникнути: щось тримає мікрофон відкритим. Читати вкладки браузера означало б
/// просити дозвіл, який у продукті про приватні розмови виглядає гірше за саму задачу;
/// звірятися з назвами вікон — ламатися на кожному перейменуванні.
/// </summary>
public static class MeetingDetector
{
    /// <summary>
    /// Скільки мікрофон має бути зайнятий, перш ніж це вважати зустріччю.
    ///
    /// Застосунки хапають мікрофон на мить, коли відкриваєш їхні налаштування звуку
    /// або перевіряєш гарнітуру. Нагадування на це привчило б ігнорувати нагадування
    /// взагалі.
    /// </summary>
    public static readonly TimeSpan ConfirmationDelay = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Скільки мікрофон має бути вільний, перш ніж зустріч вважати завершеною.
    ///
    /// Вимкнення мікрофона в дзвінку відпускає пристрій. Без цієї витримки одна
    /// розмова оголошувалася б заново після кожного mute.
    /// </summary>
    public static readonly TimeSpan EndGrace = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Процеси, чиє утримання мікрофона означає саме зустріч.
    ///
    /// Список свідомо явний. Нагадування, що спрацьовує під час диктування або
    /// голосового повідомлення, гірше за відсутність нагадувань: воно привчає
    /// закривати їх не читаючи.
    /// </summary>
    public static readonly IReadOnlySet<string> MeetingProcesses = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase)
    {
        "zoom",
        "ms-teams",
        "teams",
        "chrome",
        "msedge",
        "firefox",
        "slack",
        "webexmta",
        "webex",
        "discord",
        "skype",
        "whatsapp",
        "telegram",
    };

    /// <summary>Чи зустріч справді завершилася, чи хтось просто вимкнув мікрофон.</summary>
    public static bool HasEnded(DateTimeOffset? freeSince, DateTimeOffset now)
        => freeSince is { } since && now - since >= EndGrace;

    /// <summary>Про що оголошувати; <c>null</c> — мовчати.</summary>
    public static Meeting? Decide(Meeting? candidate,
                                  DateTimeOffset? heldSince,
                                  bool alreadyAnnounced,
                                  DateTimeOffset now)
    {
        if (candidate is not { } meeting || heldSince is not { } since || alreadyAnnounced)
        {
            return null;
        }

        return now - since >= ConfirmationDelay ? meeting : null;
    }

    /// <summary>Чи це процес, чиє утримання мікрофона варте нагадування.</summary>
    public static bool IsMeetingProcess(string processName)
        => MeetingProcesses.Contains(Path.GetFileNameWithoutExtension(processName));
}
