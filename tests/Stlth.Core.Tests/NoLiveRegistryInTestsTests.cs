using System.Text.RegularExpressions;

namespace Stlth.Core.Tests;

/// <summary>
/// Ловить тест, який чіпає автозапуск користувача, а не свій ключ.
///
/// Коштувало це так: прогін 04.11.2026 15:09 видалив значення
/// <c>STLTH Recorder</c> з <c>HKCU\…\CurrentVersion\Run</c> і не повернув його.
/// Тест звався «leaves no trace», писав і видаляв справжнє значення, а
/// відновлював його лише за умови <c>if (!wasEnabled)</c> — тобто рівно тоді, коли
/// відновлювати було нічого. Автозапуск зник мовчки й надовго: наступний старт
/// застосунку був ручний, і помітилося це лише через вісім днів.
///
/// Тест на поведінку такого не бачить за побудовою: кожен окремий тест робить те,
/// що обіцяє, а шкода лишається поза ним — у реєстрі машини, на якій його
/// запустили. Тому ознака тут синтаксична: виклик, що пише в реєстр без явного
/// ключа, у тестовому дереві не має права існувати.
/// </summary>
public partial class NoLiveRegistryInTestsTests
{
    /// <summary>Цей файл називає заборонені виклики, тож сам себе не перевіряє.</summary>
    private static readonly string[] Allowed = ["NoLiveRegistryInTestsTests.cs"];

    private static string TestRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "tests")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory!.FullName, "tests");
    }

    [Fact]
    public void No_test_writes_autostart_without_naming_its_key()
    {
        var offenders = new List<string>();

        var files = Directory.EnumerateFiles(TestRoot(), "*.cs", SearchOption.AllDirectories);

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

                if (LiveRegistryCall().IsMatch(line))
                {
                    offenders.Add($"{Path.GetFileName(file)}:{lineNumber}  {code}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Тест пише в автозапуск користувача:\n" + string.Join("\n", offenders));
    }

    /// <summary>
    /// Безключові <c>Enable</c>, <c>Disable</c> і <c>Apply</c> ідуть у живий
    /// <c>Run</c>. Варіанти з ключем (<c>EnableIn</c>, <c>DisableIn</c>) під
    /// заборону не потрапляють: після назви там літера, а не дужка.
    /// </summary>
    [GeneratedRegex(@"Autostart\s*\.\s*(Enable|Disable|Apply)\s*\(")]
    private static partial Regex LiveRegistryCall();
}
