using System.IO;
using Stlth.Core.Mixdown;
using Stlth.Core.Storage;

namespace Stlth.App;

/// <summary>
/// Будує зведений файл у фоні, вже після того, як сесія стала завершеною.
///
/// Саме у фоні і саме після: зведення похідне. Воно не має ані затримувати кінець
/// запису, ані підвішувати трей, ані робити сесію невдалою, якщо не вдалося.
/// </summary>
internal static class MixdownService
{
    public static void BuildInBackground(SessionStore store, string sessionDir)
        => Task.Run(() =>
        {
            try
            {
                SessionMixer.Mix(sessionDir);
                store.NoteMix(sessionDir, SessionMixer.FileName);
            }
            catch (MixerException)
            {
                // Дві вихідні доріжки на місці й лишаються джерелом правди —
                // відсутність зведення сесію не псує.
            }
            catch (Exception e) when (e is IOException or InvalidOperationException)
            {
            }
        });

    /// <summary>Перебудувати зведення для сесій, відновлених після краху.</summary>
    public static void RebuildAll(SessionStore store, IReadOnlyList<string> sessionDirs)
    {
        foreach (var dir in sessionDirs)
        {
            if (!SessionMixer.MixExists(dir))
            {
                BuildInBackground(store, dir);
            }
        }
    }
}
