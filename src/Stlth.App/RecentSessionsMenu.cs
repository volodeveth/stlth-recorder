using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using Stlth.Core.Mixdown;
using Stlth.Core.Storage;

namespace Stlth.App;

/// <summary>
/// Підменю «Останні записи».
///
/// Будується щоразу заново — саме тому воно і є функцією, а не полем: список, зібраний
/// один раз при старті, назавжди застигає на тому, що було до першого запису.
/// </summary>
internal static class RecentSessionsMenu
{
    private const int MaxItems = 10;

    public static ToolStripMenuItem Build(SessionStore store, Action onChanged)
    {
        var root = new ToolStripMenuItem("Останні записи");
        var sessions = store.List();

        if (sessions.Count == 0)
        {
            root.DropDownItems.Add(new ToolStripMenuItem("Записів ще немає") { Enabled = false });
            return root;
        }

        foreach (var meta in sessions.Take(MaxItems))
        {
            root.DropDownItems.Add(BuildItem(store, meta, onChanged));
        }

        root.DropDownItems.Add(new ToolStripSeparator());
        root.DropDownItems.Add(new ToolStripMenuItem("Відкрити теку записів", null,
            (_, _) => OpenFolder(store.Root)));

        return root;
    }

    private static ToolStripMenuItem BuildItem(SessionStore store, SessionMeta meta, Action onChanged)
    {
        var dir = Path.Combine(store.Root, meta.SessionId.ToString());
        var item = new ToolStripMenuItem(Label(meta));

        if (meta.Status == SessionStatus.Interrupted)
        {
            item.ToolTipText = "Сесію перервано аварійно — записане збережено.";
        }

        item.DropDownItems.Add(new ToolStripMenuItem("Показати в Провіднику", null,
            (_, _) => OpenFolder(dir)));

        var mix = Path.Combine(dir, SessionMixer.FileName);
        if (File.Exists(mix))
        {
            item.DropDownItems.Add(new ToolStripMenuItem("Прослухати розмову", null,
                (_, _) => OpenFile(mix)));
        }

        var transcript = Path.Combine(dir, "transcript.md");
        if (File.Exists(transcript))
        {
            item.DropDownItems.Add(new ToolStripMenuItem("Відкрити транскрипт", null,
                (_, _) => OpenFile(transcript)));
        }

        item.DropDownItems.Add(new ToolStripSeparator());
        item.DropDownItems.Add(new ToolStripMenuItem("Видалити", null, (_, _) =>
        {
            var answer = System.Windows.MessageBox.Show(
                $"Видалити запис від {Label(meta)}? Аудіо зникне назавжди.",
                "STLTH Recorder",
                System.Windows.MessageBoxButton.OKCancel,
                System.Windows.MessageBoxImage.Warning);

            if (answer != System.Windows.MessageBoxResult.OK)
            {
                return;
            }

            store.Delete(meta.SessionId);
            onChanged();
        }));

        return item;
    }

    private static string Label(SessionMeta meta)
    {
        var when = meta.StartedAt.ToString("dd.MM HH:mm", CultureInfo.CurrentCulture);
        var duration = TimeSpan.FromMilliseconds(meta.DurationMs);

        var length = duration.TotalMinutes >= 1
            ? $"{(int)duration.TotalMinutes} хв"
            : $"{(int)duration.TotalSeconds} с";

        var mark = meta.Status == SessionStatus.Interrupted ? "  ⚠" : string.Empty;
        return $"{when} · {length}{mark}";
    }

    private static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception)
        {
        }
    }

    private static void OpenFile(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception)
        {
        }
    }
}
