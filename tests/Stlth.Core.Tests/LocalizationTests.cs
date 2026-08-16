using System.Reflection;
using Stlth.Core.Localization;

namespace Stlth.Core.Tests;

/// <summary>
/// Локалізація має одну властивість, яку легко втратити і важко помітити: **жодного
/// пропущеного рядка**. Забутий переклад не падає і не світиться в логах — він просто
/// показує українську людині, яка обрала англійську.
/// </summary>
public class LocalizationTests : IDisposable
{
    private readonly AppLanguage _original = Strings.Current;

    public void Dispose() => Strings.Current = _original;

    /// <summary>Усі рядкові властивості без параметрів.</summary>
    private static IEnumerable<PropertyInfo> SimpleStrings() =>
        typeof(Strings)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(string) && p.Name != nameof(Strings.WhisperCode));

    private static Dictionary<string, string> Snapshot(AppLanguage language)
    {
        Strings.Current = language;
        return SimpleStrings().ToDictionary(p => p.Name, p => (string)p.GetValue(null)!);
    }

    [Fact]
    public void Every_string_differs_between_the_two_languages()
    {
        var uk = Snapshot(AppLanguage.Uk);
        var en = Snapshot(AppLanguage.En);

        // Рядок, однаковий у двох мовах, — майже завжди забутий переклад. Виняток
        // лише той, де перекладати нічого.
        var same = uk.Where(pair => pair.Value == en[pair.Key]).Select(pair => pair.Key).ToList();

        Assert.Empty(same);
    }

    [Fact]
    public void No_string_is_empty_in_either_language()
    {
        foreach (var language in new[] { AppLanguage.Uk, AppLanguage.En })
        {
            foreach (var (name, value) in Snapshot(language))
            {
                Assert.False(string.IsNullOrWhiteSpace(value), $"{name} порожній у {language}");
            }
        }
    }

    [Fact]
    public void English_carries_no_cyrillic()
    {
        // Найпоширеніша помилка перекладу — скопіювати оригінал і забути змінити.
        foreach (var (name, value) in Snapshot(AppLanguage.En))
        {
            Assert.False(value.Any(c => c >= 'А' && c <= 'я'), $"{name} містить кирилицю: {value}");
        }
    }

    [Fact]
    public void Ukrainian_is_actually_ukrainian()
    {
        var cyrillic = Snapshot(AppLanguage.Uk)
            .Count(pair => pair.Value.Any(c => c >= 'А' && c <= 'я'));

        // Кілька рядків легітимно без кирилиці бути не можуть — їх тут просто немає.
        Assert.True(cyrillic > 40, $"кирилиця лише в {cyrillic} рядках");
    }

    [Fact]
    public void English_is_the_default()
    {
        // Продукт розрахований на ширшу аудиторію, ніж одна країна, тож типова мова —
        // англійська. Українську обирають свідомо: в інсталяторі або в налаштуваннях.
        Assert.Equal(AppLanguage.En, default(AppLanguage));
        Assert.Equal(AppLanguage.En, new Stlth.Core.Settings.AppSettings().Language);
    }

    [Fact]
    public void The_whisper_code_follows_the_interface_language()
    {
        Strings.Current = AppLanguage.Uk;
        Assert.Equal("uk", Strings.WhisperCode);

        Strings.Current = AppLanguage.En;
        Assert.Equal("en", Strings.WhisperCode);
    }

    [Fact]
    public void Formatted_strings_keep_their_argument_in_both_languages()
    {
        foreach (var language in new[] { AppLanguage.Uk, AppLanguage.En })
        {
            Strings.Current = language;

            Assert.Contains("Динаміки", Strings.ConsentDevice("Динаміки"));
            Assert.Contains("00:42", Strings.RecordingFor("00:42"));
            Assert.Contains("7", Strings.MinutesShort(7));
            Assert.Contains("Realtek", Strings.NoPeerAudioBody("Realtek"));
            Assert.Contains("abc", Strings.TranscriptHeader("abc"));
        }
    }

    [Fact]
    public void Speakers_are_named_in_the_chosen_language()
    {
        Strings.Current = AppLanguage.Uk;
        Assert.Equal("Я", Strings.SpeakerMe);

        Strings.Current = AppLanguage.En;
        Assert.Equal("Me", Strings.SpeakerMe);
    }
}
