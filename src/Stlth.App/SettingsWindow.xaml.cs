using System.Diagnostics;
using System.IO;
using System.Windows;
using Stlth.Core.Permissions;
using Stlth.Core.Settings;
using Stlth.Core.Storage;

namespace Stlth.App;

public partial class SettingsWindow : Window
{
    private bool _loaded;

    public SettingsWindow()
    {
        InitializeComponent();

        Autostart.IsChecked = App.Settings.StartWithWindows;
        Reminders.IsChecked = App.Settings.MeetingReminders;
        Mixdown.IsChecked = App.Settings.BuildMixdown;

        PermissionText.Text = MicrophonePermission.Describe(App.Settings.RememberedMicPermission);

        var free = DiskGuard.FreeBytes(SessionStore.DefaultRoot);
        StorageText.Text = $"Вільно {free / 1_073_741_824.0:F1} ГБ — приблизно " +
                           $"{DiskGuard.EstimatedMinutesRemaining(free) / 60} год запису.";

        _loaded = true;
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
        App.Settings.Save();

        // Реєстр правиться одразу, а не «колись при виході»: перемикач, який не
        // подіяв до перезапуску, читається як зламаний.
        Core.Settings.Autostart.Apply(App.Settings.StartWithWindows,
                                      Environment.ProcessPath ?? "STLTH Recorder.exe");
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
