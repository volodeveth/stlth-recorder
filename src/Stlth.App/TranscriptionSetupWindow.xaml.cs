using System.IO;
using System.Net.Http;
using System.Windows;
using Stlth.Core.Transcription;

namespace Stlth.App;

/// <summary>
/// Встановлення моделей розпізнавання — з меню, без термінала і без прав
/// адміністратора.
///
/// Розмір показується <b>до</b> того, як почати: півгігабайта — це рішення, яке людина
/// має ухвалити свідомо, а не виявити постфактум у лічильнику трафіку.
/// </summary>
public partial class TranscriptionSetupWindow : Window
{
    private readonly ModelInstaller _installer = new();
    private CancellationTokenSource? _cancellation;

    public TranscriptionSetupWindow()
    {
        InitializeComponent();

        SizeText.Text = $"Потрібно завантажити ≈ {ModelInstaller.TotalBytes / 1_048_576} МБ: " +
                        "модель розпізнавання і модель визначення мовлення. " +
                        "Завантаження можна перервати — наступна спроба продовжить із того самого місця.";

        if (_installer.IsInstalled)
        {
            StatusText.Text = "Моделі вже встановлені.";
            InstallButton.IsEnabled = false;
        }

        Loaded += (_, _) => Activate();
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        InstallButton.IsEnabled = false;
        CancelButton.Content = "Перервати";
        Progress.Visibility = Visibility.Visible;
        StatusText.Text = "Завантаження…";

        _cancellation = new CancellationTokenSource();
        var progress = new Progress<double>(value =>
        {
            Progress.Value = value;
            StatusText.Text = $"Завантажено {value * 100:F0}%";
        });

        try
        {
            await _installer.InstallAsync(progress, _cancellation.Token);

            App.Settings.TranscriptionEnabled = true;
            App.Settings.Save();

            StatusText.Text = "Готово. Транскрибація доступна в меню кожної сесії.";
            CancelButton.Content = "Закрити";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Перервано. Наступна спроба продовжить із того самого місця.";
            InstallButton.IsEnabled = true;
            CancelButton.Content = "Закрити";
        }
        catch (Exception exception) when (exception is ModelInstallException or HttpRequestException
                                              or IOException)
        {
            StatusText.Text = exception.Message;
            InstallButton.IsEnabled = true;
            CancelButton.Content = "Закрити";
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
