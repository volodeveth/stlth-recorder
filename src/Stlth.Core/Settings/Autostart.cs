using Microsoft.Win32;

namespace Stlth.Core.Settings;

/// <summary>
/// Автозапуск через ключ <c>Run</c> поточного користувача.
///
/// Саме HKCU, а не HKLM: продукт мусить ставитися і працювати <b>без прав
/// адміністратора</b>. Це вимога, а не зручність — застосунок, який просить пароль
/// адміністратора, щоб запускатися, ставити не будуть.
/// </summary>
public static class Autostart
{
    public const string ValueName = "STLTH Recorder";

    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
                return key?.GetValue(ValueName) is not null;
            }
            catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException)
            {
                return false;
            }
        }
    }

    public static void Enable(string exePath)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
            // Лапки обов'язкові: шлях містить пробіл, і без них Windows спробує
            // запустити «C:\Program».
            key?.SetValue(ValueName, $"\"{exePath}\"");
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Політика заборонила запис — перемикач просто лишиться вимкненим.
        }
    }

    public static void Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException)
        {
        }
    }

    /// <summary>Привести реєстр у відповідність до налаштування.</summary>
    public static void Apply(bool enabled, string exePath)
    {
        if (enabled)
        {
            Enable(exePath);
        }
        else
        {
            Disable();
        }
    }
}
