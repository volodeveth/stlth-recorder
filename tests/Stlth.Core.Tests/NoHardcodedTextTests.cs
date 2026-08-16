using System.Text.RegularExpressions;

namespace Stlth.Core.Tests;

/// <summary>
/// Ловить рядок, який показують людині, але який ніколи не проходив через
/// <c>Strings</c>.
///
/// Тест на повноту перекладу такого не бачить за побудовою: він перевіряє те, що в
/// <c>Strings</c> лежить, і нічого не знає про текст, якого там немає. Саме так
/// нагадування про зустрічі лишилися українськими в англійському інтерфейсі —
/// локалізували меню й діалоги, а два виклики <c>Notify</c> проґавили.
///
/// Ознака проста й надійна: кирилиця в рядковому літералі. Українські коментарі й
/// XML-документація — це не текст інтерфейсу, тож вони не рахуються.
/// </summary>
public partial class NoHardcodedTextTests
{
    /// <summary>Файли, де українські літерали доречні.</summary>
    private static readonly string[] Allowed =
    [
        "Strings.cs",       // саме тут текст і має жити
        "SessionJson.cs",   // повідомлення про пошкоджений meta.json — для лога, не для UI
    ];

    /// <summary>
    /// Проєкти, які показують текст людині. Консольний стенд
    /// (<c>Stlth.Cli</c>) сюди не входить: він існує для вимірювань і розбору
    /// польотів, його читає розробник, і локалізувати його означало б подвоїти
    /// роботу заради аудиторії з однієї людини.
    /// </summary>
    private static readonly string[] UserFacingProjects = ["Stlth.App", "Stlth.Core"];

    private static string SourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory!.FullName, "src");
    }

    [Fact]
    public void No_ukrainian_string_literal_lives_outside_Strings()
    {
        var offenders = new List<string>();

        var files = UserFacingProjects
            .Select(project => Path.Combine(SourceRoot(), project))
            .Where(Directory.Exists)
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories));

        foreach (var file in files)
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
                Allowed.Contains(Path.GetFileName(file)))
            {
                continue;
            }

            var lineNumber = 0;
            foreach (var line in File.ReadLines(file))
            {
                lineNumber++;

                var code = line.TrimStart();
                if (code.StartsWith("//") || code.StartsWith("///") || code.StartsWith("*"))
                {
                    continue;
                }

                foreach (Match match in StringLiteral().Matches(line))
                {
                    if (match.Value.Any(c => c is >= 'А' and <= 'я'))
                    {
                        offenders.Add($"{Path.GetFileName(file)}:{lineNumber}  {match.Value}");
                    }
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Текст поза Strings:\n" + string.Join("\n", offenders));
    }

    [GeneratedRegex("\"[^\"\\n]*\"")]
    private static partial Regex StringLiteral();
}
