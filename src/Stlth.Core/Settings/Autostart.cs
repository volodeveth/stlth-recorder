using Microsoft.Win32;

namespace Stlth.Core.Settings;

/// <summary>
/// Автозапуск через ключ <c>Run</c> поточного користувача.
///
/// Саме HKCU, а не HKLM: продукт мусить ставитися і працювати <b>без прав
/// адміністратора</b>. Це вимога, а не зручність — застосунок, який просить пароль
/// адміністратора, щоб запускатися, ставити не будуть.
///
/// Кожен член, що чіпає реєстр, має близнюка з явним ключем
/// (<see cref="IsEnabledIn"/>, <see cref="EnableIn"/>, <see cref="DisableIn"/>), і
/// це не косметика. Прогін тестів 04.11.2026 пішов у живий <c>Run</c>, видалив
/// автозапуск користувача й не повернув його: застосунок не піднімався вісім днів,
/// і помітили це не тести, а людина. Тести тепер працюють у власному ключі, а
/// продуктовий дістають лише безключові члени, кликати які їм заборонено окремим
/// тестом.
/// </summary>
public static class Autostart
{
    public const string ValueName = "STLTH Recorder";

    /// <summary>
    /// Ключ, у який пише сам продукт. <c>internal</c> лише для того, щоб тест
    /// міг перевірити: сейм не переселив застосунок кудись убік від того місця,
    /// де Windows його шукає.
    /// </summary>
    internal const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsEnabled => IsEnabledIn(KeyPath);

    internal static bool IsEnabledIn(string keyPath)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath);
            return key?.GetValue(ValueName) is not null;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    /// <summary>
    /// Команда, якою Windows підніме застосунок при вході.
    ///
    /// Не просто шлях до процесу. Під Smart App Control непідписаний apphost не
    /// запускається взагалі, і застосунок доводиться піднімати через підписаний
    /// <c>dotnet.exe</c>. Тоді <see cref="Environment.ProcessPath"/> — це вже сам
    /// хост, і записаний у ключ Run він не запустив би нічого: хост без аргументу
    /// не знає, що виконувати. Автозапуск при цьому не падав би з помилкою, він
    /// просто мовчки зникав.
    /// </summary>
    /// <returns><c>null</c>, якщо робочої команди скласти не вдалося.</returns>
    public static string? LaunchCommand() =>
        CommandFor(Environment.ProcessPath, System.Reflection.Assembly.GetEntryAssembly()?.Location);

    /// <summary>
    /// Чиста функція: команда з хоста і збірки. Форма команди перевіряється без
    /// реєстру і без залежності від того, як саме запущено тести.
    /// </summary>
    internal static string? CommandFor(string? hostPath, string? assemblyPath)
    {
        if (string.IsNullOrEmpty(hostPath))
        {
            return null;
        }

        // Лапки обов'язкові: шлях містить пробіл, і без них Windows спробує
        // запустити «C:\Program».
        if (!IsDotnetHost(hostPath))
        {
            return Quote(hostPath);
        }

        // Самодостатня збірка завжди йде через власний apphost, тож сюди
        // потрапляє лише framework-dependent запуск, де Location заповнений.
        return string.IsNullOrEmpty(assemblyPath)
            ? null
            : $"{Quote(hostPath)} exec {Quote(assemblyPath)}";
    }

    private static bool IsDotnetHost(string path) =>
        string.Equals(Path.GetFileNameWithoutExtension(path), "dotnet",
                      StringComparison.OrdinalIgnoreCase);

    private static string Quote(string value) => $"\"{value}\"";

    public static void Enable() => EnableIn(KeyPath);

    internal static void EnableIn(string keyPath)
    {
        if (LaunchCommand() is not { } command)
        {
            // Запис, який нічого не запускає, гірший за відсутній: він виглядає
            // як налаштований автозапуск і тихо не працює.
            return;
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(keyPath);
            key?.SetValue(ValueName, command);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Політика заборонила запис — перемикач просто лишиться вимкненим.
        }
    }

    public static void Disable() => DisableIn(KeyPath);

    internal static void DisableIn(string keyPath)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException)
        {
        }
    }

    /// <summary>Привести реєстр у відповідність до налаштування.</summary>
    public static void Apply(bool enabled)
    {
        if (enabled)
        {
            Enable();
        }
        else
        {
            Disable();
        }
    }
}
