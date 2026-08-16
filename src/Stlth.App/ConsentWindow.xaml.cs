using System.Windows;
using Stlth.Core.Localization;
using Stlth.Core.Storage;

namespace Stlth.App;

/// <summary>
/// Підтвердження перед стартом.
///
/// Сенс не в діалозі як такому, а в тому, що факт і час підтвердження лягають у запис
/// сесії. Тому час береться в момент натискання, а не в момент старту движка.
/// </summary>
public partial class ConsentWindow : Window
{
    private DateTimeOffset? _confirmedAt;

    private ConsentWindow(string sessionRoot, string outputDevice)
    {
        InitializeComponent();

        Title = Strings.ConsentTitle;
        Heading.Text = Strings.ConsentQuestion;
        Explain.Text = Strings.ConsentExplain;
        DeviceText.Text = Strings.ConsentDevice(outputDevice);
        CancelButton.Content = Strings.Cancel;
        StartButton.Content = Strings.ConsentStart;

        var free = DiskGuard.FreeBytes(sessionRoot);
        var level = DiskGuard.Level(free);

        if (level != DiskLevel.Ok)
        {
            DiskWarning.Visibility = Visibility.Visible;
            DiskText.Text = level == DiskLevel.Critical
                ? Strings.DiskCritical
                : Strings.DiskLow(DiskGuard.EstimatedMinutesRemaining(free));
        }

        // Агент у треї ніколи не є активним застосунком, тож вікно, показане
        // «як є», відкривається за чужими — і виглядає так, ніби кнопка не працює.
        Loaded += (_, _) =>
        {
            Activate();
            StartButton.Focus();
        };
    }

    /// <returns>Момент підтвердження або <c>null</c>, якщо запис скасовано.</returns>
    public static DateTimeOffset? Ask(string sessionRoot, string outputDevice)
    {
        var window = new ConsentWindow(sessionRoot, outputDevice);
        return window.ShowDialog() == true ? window._confirmedAt : null;
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        _confirmedAt = DateTimeOffset.Now;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
