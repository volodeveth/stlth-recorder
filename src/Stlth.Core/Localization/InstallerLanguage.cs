using Microsoft.Win32;

namespace Stlth.Core.Localization;

/// <summary>
/// Місток від інсталятора до застосунку.
///
/// Мову людина обирає на першому екрані встановлення, а застосунок запускається
/// окремим процесом і про той вибір нічого не знає. Інсталятор лишає його в реєстрі
/// поточного користувача, застосунок забирає <b>один раз</b> і одразу прибирає ключ.
///
/// Саме «забирає, а не читає»: інакше повторне встановлення поверх мовчки скидало б
/// мову, яку людина потім змінила в налаштуваннях.
/// </summary>
public static class InstallerLanguage
{
    private const string KeyPath = @"Software\STLTH Recorder";
    private const string ValueName = "SetupLanguage";

    /// <summary>Записує вибір; викликається інсталятором через реєстр, а не кодом.</summary>
    public static string RegistryPath => $@"HKCU\{KeyPath}\{ValueName}";

    /// <returns>Обрана мова, якщо інсталятор її лишив; інакше <c>null</c>.</returns>
    public static AppLanguage? TakeIfPresent()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
            if (key?.GetValue(ValueName) is not string value)
            {
                return null;
            }

            key.DeleteValue(ValueName, throwOnMissingValue: false);

            return value.Equals("en", StringComparison.OrdinalIgnoreCase)
                ? AppLanguage.En
                : AppLanguage.Uk;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }
}
