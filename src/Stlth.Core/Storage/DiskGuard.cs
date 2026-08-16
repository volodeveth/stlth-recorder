namespace Stlth.Core.Storage;

public enum DiskLevel
{
    Ok,

    /// <summary>Попередити перед стартом.</summary>
    Low,

    /// <summary>Зупинити сесію, що триває.</summary>
    Critical,
}

/// <summary>
/// Стежить за вільним місцем, щоб довга розмова не заповнила том мовчки.
///
/// Контрольована зупинка лишає аудіо придатним; запис у переповнений том — ні.
/// </summary>
public static class DiskGuard
{
    /// <summary>Нижче цього сесію, що триває, зупиняють: 200 МіБ ≈ 12 хвилин обох доріжок.</summary>
    public const long CriticalThreshold = 200L * 1024 * 1024;

    /// <summary>Нижче цього діалог згоди показує попередження: 1 ГіБ ≈ 62 хвилини.</summary>
    public const long LowThreshold = 1024L * 1024 * 1024;

    /// <summary>48 кГц × 16 біт × (1 моно + 2 стерео) канали.</summary>
    public const long BytesPerSecond = 48000L * 2 * 3;

    /// <summary>
    /// Вільних байтів на томі, якому належить <paramref name="path"/>.
    ///
    /// Нуль повертається лише тоді, коли тому взагалі не досягти.
    ///
    /// Запит іде вгору по дереву тек до найближчого наявного предка. Причина
    /// конкретна: на свіжому встановленні теки сесій ще не існує, а API вільного
    /// місця відповідають лише для наявних шляхів. Наслідок — застосунок оголошує
    /// диск повним на машині з сотнями вільних гігабайтів і блокує найперший запис
    /// кожного нового користувача. Том той самий незалежно від того, чи існує листок.
    ///
    /// Нуль від системного API вважається «не знаю», а не «місця немає»: та сама
    /// вада вилізла вдруге саме тому, що нулю повірили як вимірюванню.
    /// </summary>
    public static long FreeBytes(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return 0;
        }

        try
        {
            var candidate = Path.GetFullPath(path);

            while (true)
            {
                if (Directory.Exists(candidate))
                {
                    var free = FreeBytesForExistingPath(candidate);
                    if (free > 0)
                    {
                        return free;
                    }
                }

                var parent = Path.GetDirectoryName(candidate);
                if (string.IsNullOrEmpty(parent) || parent == candidate)
                {
                    return 0;
                }

                candidate = parent;
            }
        }
        catch (ArgumentException)
        {
            return 0;
        }
        catch (NotSupportedException)
        {
            return 0;
        }
    }

    public static DiskLevel Level(long freeBytes)
    {
        if (freeBytes < CriticalThreshold)
        {
            return DiskLevel.Critical;
        }

        return freeBytes < LowThreshold ? DiskLevel.Low : DiskLevel.Ok;
    }

    public static DiskLevel LevelAt(string path) => Level(FreeBytes(path));

    /// <summary>Скільки ще хвилин запису вміститься у <paramref name="freeBytes"/>.</summary>
    public static int EstimatedMinutesRemaining(long freeBytes)
        => freeBytes <= 0 ? 0 : (int)(freeBytes / BytesPerSecond / 60);

    private static long FreeBytesForExistingPath(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root))
            {
                return 0;
            }

            var drive = new DriveInfo(root);
            return drive.IsReady ? drive.AvailableFreeSpace : 0;
        }
        catch (IOException)
        {
            return 0;
        }
        catch (ArgumentException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
