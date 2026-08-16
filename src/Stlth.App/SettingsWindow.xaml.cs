using System.Diagnostics;
using System.IO;
using System.Windows;
using Stlth.Core.Localization;
using Stlth.Core.Permissions;
using Stlth.Core.Storage;
using Stlth.Core.Transcription;

namespace Stlth.App;

public partial class SettingsWindow : Window
{
    private bool _loaded;

    public SettingsWindow()
    {
        InitializeComponent();

        LanguageBox.SelectedIndex = App.Settings.Language == AppLanguage.Uk ? 1 : 0;
        Autostart.IsChecked = App.Settings.StartWithWindows;
        Reminders.IsChecked = App.Settings.MeetingReminders;
        Mixdown.IsChecked = App.Settings.BuildMixdown;
        AutoTranscribe.IsChecked = App.Settings.AutoTranscribe;
        DeleteAudio.IsChecked = App.Settings.DeleteAudioAfterTranscription;

        ApplyLanguage();
        _loaded = true;
    }

    /// <summary>
    /// Перемалювати весь текст вікна.
    ///
    /// Викликається і при відкритті, і після зміни мови: перемикач, який подіє лише
    /// після перезапуску, читається як зламаний.
    /// </summary>
    private void ApplyLanguage()
    {
        Title = Strings.SettingsWindowTitle;
        Heading.Text = Strings.SettingsTitle;

        LanguageLabel.Text = Strings.LanguageLabel;
        LanguageHint.Text = Strings.LanguageHint;

        AutostartLabel.Text = Strings.AutostartLabel;
        AutostartHint.Text = Strings.AutostartHint;
        RemindersLabel.Text = Strings.RemindersLabel;
        RemindersHint.Text = Strings.RemindersHint;
        MixdownLabel.Text = Strings.MixdownLabel;
        MixdownHint.Text = Strings.MixdownHint;
        AutoTranscribeLabel.Text = Strings.AutoTranscribeLabel;
        AutoTranscribeHint.Text = Strings.AutoTranscribeHint;

        // Перемикач лишається доступним, але коли моделей немає, він нічого не
        // зробить — і про це чесніше сказати одразу, ніж дати людині чекати
        // транскрипт, якого не буде.
        AutoTranscribeWarning.Text = Strings.AutoTranscribeNeedsModels;
        AutoTranscribeWarning.Visibility = new Transcriber().IsAvailable
            ? Visibility.Collapsed
            : Visibility.Visible;

        DeleteAudioLabel.Text = Strings.DeleteAudioLabel;
        DeleteAudioHint.Text = Strings.DeleteAudioHint;

        // Разом із вимкненим зведенням ця опція лишає від сесії саму лише текстову
        // розшифровку. Це законний вибір, але людина має зробити його з відкритими
        // очима, а не виявити через тиждень.
        DeleteAudioWarning.Text = Strings.DeleteAudioNoMixdown;
        DeleteAudioWarning.Visibility =
            App.Settings.DeleteAudioAfterTranscription && !App.Settings.BuildMixdown
                ? Visibility.Visible
                : Visibility.Collapsed;

        PermissionText.Text = MicrophonePermission.Describe(App.Settings.RememberedMicPermission);

        var free = DiskGuard.FreeBytes(SessionStore.DefaultRoot);
        StorageText.Text = Strings.StorageFree(free / 1_073_741_824.0,
                                               DiskGuard.EstimatedMinutesRemaining(free) / 60);

        FolderButton.Content = Strings.OpenFolder;
        CloseButton.Content = Strings.Close;
    }

    private void Language_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!_loaded)
        {
            return;
        }

        App.Settings.Language = LanguageBox.SelectedIndex == 1 ? AppLanguage.Uk : AppLanguage.En;
        Strings.Current = App.Settings.Language;
        App.Settings.Save();

        ApplyLanguage();
        App.RefreshTray();
    }

    private void Save(object sender, RoutedEventArgs e)
    {
        if (!_loaded)
        {
            return;
        }

        App.Settings.StartWithWindows = Autostart.IsChecked == true;
        App.Settings.MeetingReminders = Reminders.IsChecked == true;
        App.Settings.BuildMixdown = Mixdown.IsChecked == true;
        App.Settings.AutoTranscribe = AutoTranscribe.IsChecked == true;
        App.Settings.DeleteAudioAfterTranscription = DeleteAudio.IsChecked == true;
        App.Settings.Save();

        ApplyLanguage();

        // Реєстр правиться одразу, а не «колись при виході».
        Core.Settings.Autostart.Apply(App.Settings.StartWithWindows,
                                      Environment.ProcessPath ?? "STLTH Recorder.exe");
        App.ApplyMeetingReminders();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(SessionStore.DefaultRoot);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{SessionStore.DefaultRoot}\"")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception exception) when (exception is IOException
                                              or System.ComponentModel.Win32Exception)
        {
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
