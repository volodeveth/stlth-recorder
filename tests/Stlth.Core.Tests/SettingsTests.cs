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
    public void Enabling_and_disabling_leaves_no_trace()
    {
        var wasEnabled = Autostart.IsEnabled;
        try
        {
            Autostart.Enable(@"C:\test\stlth.exe");
            Assert.True(Autostart.IsEnabled);

            Autostart.Disable();
            Assert.False(Autostart.IsEnabled);
        }
        finally
        {
            if (!wasEnabled)
            {
                Autostart.Disable();
            }
        }
    }

    [Fact]
    public void Disabling_twice_is_not_an_error()
    {
        var wasEnabled = Autostart.IsEnabled;
        try
        {
            Autostart.Disable();
            Autostart.Disable();
        }
        finally
        {
            if (wasEnabled)
            {
                Autostart.Enable(Environment.ProcessPath ?? "stlth.exe");
            }
        }
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
