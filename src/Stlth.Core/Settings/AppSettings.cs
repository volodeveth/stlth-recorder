using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Stlth.Core.Permissions;

namespace Stlth.Core.Settings;

/// <summary>Налаштування застосунку. Живуть поруч із сесіями, у профілі користувача.</summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>
    /// Запускатися разом із Windows. <b>Увімкнено за замовчуванням.</b>
    ///
    /// Перемикач існує, щоб вимкнути. Продукт, який треба щоразу запускати руками, не
    /// розв'язує проблему забутого запису — а саме заради неї він і потрібен.
    /// </summary>
    public bool StartWithWindows { get; set; } = true;

    /// <summary>Нагадувати, коли починається зустріч і коли запис лишився увімкненим.</summary>
    public bool MeetingReminders { get; set; } = true;

    /// <summary>Будувати зведений файл для прослуховування після кожної сесії.</summary>
    public bool BuildMixdown { get; set; } = true;

    /// <summary>Чи встановлені моделі транскрибації.</summary>
    public bool TranscriptionEnabled { get; set; }

    /// <summary>
    /// Останній відомий стан дозволу на мікрофон.
    ///
    /// Зберігається між запусками навмисно: інакше застосунок пише «стан невідомий»
    /// тому, хто видав дозвіл місяць тому.
    /// </summary>
    public MicPermission RememberedMicPermission { get; set; } = MicPermission.Unknown;

    public static string Path { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "STLTH Recorder",
        "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(Path))
            {
                return new AppSettings();
            }

            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path), Options)
                   ?? new AppSettings();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // Зіпсовані налаштування не мають робити застосунок незапускабельним:
            // значення тут усі відновлювані, а запис — ні.
            return new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            var directory = System.IO.Path.GetDirectoryName(Path)!;
            Directory.CreateDirectory(directory);

            var temporary = Path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, Options));
            File.Move(temporary, Path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Не збереглося — застосунок працює далі з тим, що в пам'яті.
        }
    }
}
