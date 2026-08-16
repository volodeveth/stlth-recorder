using System.Windows;
using Stlth.Core.Localization;
using Stlth.Core.Permissions;

namespace Stlth.App;

/// <summary>
/// Що робити, коли Windows не дає мікрофона.
///
/// Третій варіант — «писати без мікрофона» — існує навмисно: розмову, записану з
/// одного боку, ще можна послухати, а не записану — вже ні. Сесія при цьому чесно
/// позначається як <c>system-only</c>, а не вдає, ніби голос там є.
/// </summary>
public partial class PermissionWindow : Window
{
    private bool _continueAnyway;

    private PermissionWindow()
    {
        InitializeComponent();

        Title = Strings.PermissionTitle;
        Heading.Text = Strings.PermissionHeading;
        Body1.Text = Strings.PermissionBody1;
        Body2.Text = Strings.PermissionBody2;
        CancelButton.Content = Strings.Cancel;
        ContinueButton.Content = Strings.PermissionContinue;
        OpenButton.Content = Strings.PermissionOpen;

        Loaded += (_, _) => Activate();
    }

    /// <returns><c>true</c>, якщо запис усе одно треба почати.</returns>
    public static bool AskToContinue()
    {
        var window = new PermissionWindow();
        window.ShowDialog();
        return window._continueAnyway;
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        MicrophonePermission.OpenSettings();
        DialogResult = false;
    }

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        _continueAnyway = true;
        DialogResult = false;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
