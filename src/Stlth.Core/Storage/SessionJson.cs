using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Stlth.Core.Storage;

/// <summary>Читання і запис <c>meta.json</c>.</summary>
public static class SessionJson
{
    /// <summary>
    /// ISO 8601 зі зміщенням і дробовими секундами.
    ///
    /// Дробові секунди — не косметика: без них дві сесії, стартовані в межах однієї
    /// секунди, сортуються недетерміновано в «Останніх записах», а відновлена після
    /// краху тривалість втрачає до секунди.
    /// </summary>
    private const string DateFormat = "yyyy-MM-dd'T'HH:mm:ss.fffzzz";

    private static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            // Кирилиця в назвах пристроїв мусить лишатися кирилицею: meta.json
            // читають очима не рідше, ніж кодом.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        options.Converters.Add(new OffsetDateConverter());
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower));
        return options;
    }

    public static string Serialize(SessionMeta meta) => JsonSerializer.Serialize(meta, Options);

    public static SessionMeta Deserialize(string json) =>
        JsonSerializer.Deserialize<SessionMeta>(json, Options)
        ?? throw new JsonException("meta.json порожній");

    public static SessionMeta Load(string path) => Deserialize(File.ReadAllText(path));

    /// <summary>
    /// Записати атомарно: крах посеред запису не має лишати обрізаний
    /// <c>meta.json</c>. Сесія, чиї метадані не читаються, втрачена так само
    /// надійно, як сесія без аудіо.
    /// </summary>
    public static void WriteAtomic(SessionMeta meta, string path)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, Serialize(meta));
        File.Move(temporary, path, overwrite: true);
    }

    private sealed class OffsetDateConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
            => DateTimeOffset.Parse(reader.GetString()!, CultureInfo.InvariantCulture,
                                    DateTimeStyles.RoundtripKind);

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.ToString(DateFormat, CultureInfo.InvariantCulture));
    }
}
