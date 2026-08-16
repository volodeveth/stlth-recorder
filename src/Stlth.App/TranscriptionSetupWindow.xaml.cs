using System.IO;
using System.Net.Http;
using System.Windows;
using Stlth.Core.Localization;
using Stlth.Core.Transcription;

namespace Stlth.App;

/// <summary>
/// Встановлення моделей розпізнавання — з меню, без термінала і без прав
/// адміністратора.
///
/// Розмір показується <b>до</b> того, як почати: гігабайт — це рішення, яке людина має
/// ухвалити свідомо, а не виявити постфактум у лічильнику трафіку.
/// </summary>
public partial class TranscriptionSetupWindow : Window
{
    private readonly ModelInstaller _installer = new();
    private CancellationTokenSource? _cancellation;

    public TranscriptionSetupWindow()
    {
        InitializeComponent();

        Title = Strings.TranscriptionSetupTitle;
        Heading.Text = Strings.TranscriptionHeading;
        Explain.Text = Strings.TranscriptionExplain;
        SizeText.Text = Strings.TranscriptionSize(ModelInstaller.TotalBytes / 1_048_576);
        CancelButton.Content = Strings.Close;
        InstallButton.Content = Strings.TranscriptionDownload;

        if (_installer.IsInstalled)
        {
            StatusText.Text = Strings.TranscriptionInstalled;
            InstallButton.IsEnabled = false;
        }

        Loaded += (_, _) => Activate();
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        InstallButton.IsEnabled = false;
        CancelButton.Content = Strings.TranscriptionInterrupt;
        Progress.Visibility = Visibility.Visible;
        StatusText.Text = Strings.TranscriptionDownloading;

        _cancellation = new CancellationTokenSource();
        var progress = new Progress<double>(value =>
        {
            Progress.Value = value;
            StatusText.Text = Strings.TranscriptionProgress(value);
        });

        try
        {
            await _installer.InstallAsync(progress, _cancellation.Token);

            App.Settings.TranscriptionEnabled = true;
            App.Settings.Save();

            StatusText.Text = Strings.TranscriptionDone;
            CancelButton.Content = Strings.Close;
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = Strings.TranscriptionCancelled;
            InstallButton.IsEnabled = true;
            CancelButton.Content = Strings.Close;
        }
        catch (Exception exception) when (exception is ModelInstallException or HttpRequestException
                                              or IOException)
        {
            StatusText.Text = exception.Message;
            InstallButton.IsEnabled = true;
            CancelButton.Content = Strings.Close;
        }
        finally
        {
            Progress.Visibility = Visibility.Collapsed;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_cancellation is { IsCancellationRequested: false } source && Progress.Visibility == Visibility.Visible)
        {
            source.Cancel();
            return;
        }

        Close();
    }
}
