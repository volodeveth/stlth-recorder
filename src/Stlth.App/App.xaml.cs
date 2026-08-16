using System.Windows;
using Stlth.Core;
using Stlth.Core.Audio;
using Stlth.Core.Localization;
using Stlth.Core.Permissions;
using Stlth.Core.Settings;
using Stlth.Core.Storage;

// Проєкт тягне і WPF, і WinForms (трей), а вони мають однойменний Application.
// Псевдонім знімає двозначність там, де вона виникає, не глушачи неявні using.
using Application = System.Windows.Application;

namespace Stlth.App;

public partial class App : Application
{
    private TrayIcon? _tray;
    private System.Threading.Mutex? _singleInstance;

    public static AppSettings Settings { get; private set; } = new();

    public static RecorderController Controller { get; private set; } = null!;

    /// <summary>
    /// Привести спостереження за зустрічами у відповідність до налаштування.
    ///
    /// Перемикач, який подіє лише після перезапуску, читається як зламаний.
    /// </summary>
    public static void ApplyMeetingReminders()
        => ((App)Current)._tray?.SetMeetingReminders(Settings.MeetingReminders);

    /// <summary>Перебудувати меню трею — після зміни мови воно має заговорити нею одразу.</summary>
    public static void RefreshTray() => ((App)Current)._tray?.Rebuild();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Дві копії агента писали б у ту саму теку сесій і сварилися б за пристрої.
        _singleInstance = new System.Threading.Mutex(true, @"Local\STLTH-Recorder", out var isFirst);
        if (!isFirst)
        {
            Shutdown();
            return;
        }

        Settings = AppSettings.Load();

        // Мову, обрану в інсталяторі, підхоплюємо один раз — далі нею володіють
        // налаштування застосунку.
        if (InstallerLanguage.TakeIfPresent() is { } chosen)
        {
            Settings.Language = chosen;
            Settings.Save();
        }

        Strings.Current = Settings.Language;

        // Усе, що має працювати без участі користувача, будується тут — а не при
        // першому відкритті меню. Лінива побудова меню робить такий код мертвим без
        // жодного попередження: обробник існує і не викликається нізвідки, поки
        // хтось не клацне, — а момент, заради якого він потрібен, це рівно той, коли
        // меню ніхто не відкривав.
        Controller = new RecorderController(new SessionStore(), dir => new AudioEngine(dir));
        Controller.RecoverInterruptedSessions();

        Autostart.Apply(Settings.StartWithWindows, Environment.ProcessPath ?? "STLTH Recorder.exe");

        _tray = new TrayIcon(Controller);
        _tray.Show();

        if (Controller.RecoveredSessions.Count > 0)
        {
            _tray.Notify(Strings.RecoveredTitle,
                         Controller.RecoveredSessions.Count == 1
                             ? Strings.RecoveredOne
                             : Strings.RecoveredMany(Controller.RecoveredSessions.Count));

            // Сесія, обірвана крахом, зведення не встигла отримати — а послухати її
            // хочеться саме тоді, коли щось пішло не так.
            if (Settings.BuildMixdown)
            {
                MixdownService.RebuildAll(Controller.Store, Controller.RecoveredSessions);
            }
        }

        RefreshPermissionInBackground();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Запис, що триває, треба закрити по-людськи: файли без дописаних розмірів
        // читаються не всіма, і чинити їх довелося б наступного запуску.
        if (Controller is { IsRecording: true })
        {
            Controller.Stop();
        }

        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// Дозвіл перевіряється у фоні: проба піднімає потік, і на деяких драйверах це
    /// займає помітний час. Робити це на потоці UI означало б підвісити трей при
    /// старті — а підвішений трей не має способу повідомити про себе.
    /// </summary>
    private static void RefreshPermissionInBackground()
        => Task.Run(() =>
        {
            var state = MicrophonePermission.Probe();
            if (state == MicPermission.Unknown)
            {
                return;
            }

            Settings.RememberedMicPermission = state;
            Settings.Save();
        });
}
