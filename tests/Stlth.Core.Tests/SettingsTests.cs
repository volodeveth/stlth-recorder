using Microsoft.Win32;
using Stlth.Core.Permissions;
using Stlth.Core.Settings;

namespace Stlth.Core.Tests;

public class AppSettingsTests
{
    [Fact]
    public void Autostart_is_on_by_default()
    {
        // Продукт, який треба щоразу запускати руками, не розв'язує проблему
        // забутого запису. Перемикач існує, щоб вимкнути.
        Assert.True(new AppSettings().StartWithWindows);
    }

    [Fact]
    public void Reminders_and_mixdown_are_on_by_default_transcription_is_not()
    {
        var settings = new AppSettings();

        Assert.True(settings.MeetingReminders);
        Assert.True(settings.BuildMixdown);
        // Транскрибація тягне 548 МБ моделей — це має бути свідомий вибір.
        Assert.False(settings.TranscriptionEnabled);
    }

    [Fact]
    public void Permission_state_is_remembered_so_it_survives_a_restart()
    {
        // Інакше застосунок пише «стан невідомий» тому, хто видав дозвіл місяць тому.
        var settings = new AppSettings { RememberedMicPermission = MicPermission.Granted };

        Assert.Equal(MicPermission.Granted, settings.RememberedMicPermission);
    }
}

public class AutostartTests
{
    [Fact]
    public void Self_contained_host_is_the_whole_command()
    {
        // Звичайний випадок: apphost сам себе запускає, аргументи не потрібні.
        Assert.Equal(@"""C:\app\STLTH Recorder.exe""",
                     Autostart.CommandFor(@"C:\app\STLTH Recorder.exe", @"C:\app\STLTH Recorder.dll"));
    }

    [Fact]
    public void Dotnet_host_carries_the_assembly_as_an_argument()
    {
        // Під Smart App Control непідписаний apphost не запускається, і застосунок
        // піднімають через підписаний dotnet.exe. Голий шлях до хоста в ключі Run
        // не запустив би нічого: хост без аргументу не знає, що виконувати.
        Assert.Equal(@"""C:\dotnet\dotnet.exe"" exec ""C:\app\STLTH Recorder.dll""",
                     Autostart.CommandFor(@"C:\dotnet\dotnet.exe", @"C:\app\STLTH Recorder.dll"));
    }

    [Fact]
    public void Dotnet_host_without_an_assembly_yields_no_command()
    {
        // Краще не мати запису, ніж мати запис, який мовчки нічого не запускає.
        Assert.Null(Autostart.CommandFor(@"C:\dotnet\dotnet.exe", null));
    }

    [Fact]
    public void Unknown_host_yields_no_command()
    {
        Assert.Null(Autostart.CommandFor(null, @"C:\app\STLTH Recorder.dll"));
    }

    [Fact]
    public void Live_command_names_something_that_exists()
    {
        // Тест бачить рівно те, що побачить Windows при вході: шлях до реального
        // файлу. Саме тут ловиться підміна ProcessPath на хост без аргументу.
        var command = Autostart.LaunchCommand();
        Assert.NotNull(command);

        var first = command!.Split('"', StringSplitOptions.RemoveEmptyEntries)[0];
        Assert.True(File.Exists(first), $"немає файлу: {first}");
    }

    /// <summary>
    /// Власний ключ під тести. Живий <c>Run</c> не чіпається навіть на мить:
    /// прогін 04.11.2026 забрав автозапуск користувача рівно тому, що тест
    /// писав і видаляв справжнє значення, а відновлював його лише тоді, коли
    /// відновлювати було нічого.
    /// </summary>
    private const string TestKey = @"Software\STLTH Recorder Tests\Run";

    private const string TestTree = @"Software\STLTH Recorder Tests";

    [Fact]
    public void Enabling_and_disabling_touches_only_the_given_key()
    {
        try
        {
            Assert.False(Autostart.IsEnabledIn(TestKey));

            Autostart.EnableIn(TestKey);
            Assert.True(Autostart.IsEnabledIn(TestKey));

            Autostart.DisableIn(TestKey);
            Assert.False(Autostart.IsEnabledIn(TestKey));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(TestTree, throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public void Disabling_twice_is_not_an_error()
    {
        try
        {
            Autostart.DisableIn(TestKey);
            Autostart.DisableIn(TestKey);
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(TestTree, throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public void The_product_itself_still_targets_the_real_run_key()
    {
        // Ключ під тести існує для тестів. Застосунок мусить лишитися там, де
        // Windows його шукає, інакше автозапуску не буде вже через цей сейм.
        Assert.Equal(@"Software\Microsoft\Windows\CurrentVersion\Run", Autostart.KeyPath);
    }
}

public class MicrophonePermissionTests
{
    [Fact]
    public void Probe_never_throws()
    {
        // Дозвіл визначається фактичною спробою активації, а не приватним API, тож
        // проба мусить бути безпечною для виклику будь-коли — у тому числі з меню.
        var result = MicrophonePermission.Probe();

        Assert.True(Enum.IsDefined(result));
    }

    [Fact]
    public void Every_state_has_a_human_description()
    {
        foreach (var value in Enum.GetValues<MicPermission>())
        {
            Assert.False(string.IsNullOrWhiteSpace(MicrophonePermission.Describe(value)));
        }
    }
}
