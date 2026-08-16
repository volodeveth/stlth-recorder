using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using Stlth.Core.Localization;
using Stlth.Core.Mixdown;
using Stlth.Core.Storage;
using Stlth.Core.Transcription;

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
        var root = new ToolStripMenuItem(Strings.RecentSessions);
        var sessions = store.List();

        if (sessions.Count == 0)
        {
            root.DropDownItems.Add(new ToolStripMenuItem(Strings.NoSessionsYet) { Enabled = false });
            return root;
        }

        foreach (var meta in sessions.Take(MaxItems))
        {
            root.DropDownItems.Add(BuildItem(store, meta, onChanged));
        }

        root.DropDownItems.Add(new ToolStripSeparator());
        root.DropDownItems.Add(new ToolStripMenuItem(Strings.OpenRecordingsFolder, null,
            (_, _) => OpenFolder(store.Root)));

        return root;
    }

    private static ToolStripMenuItem BuildItem(SessionStore store, SessionMeta meta, Action onChanged)
    {
        var dir = Path.Combine(store.Root, meta.SessionId.ToString());
        var item = new ToolStripMenuItem(Label(meta));

        if (meta.Status == SessionStatus.Interrupted)
        {
            item.ToolTipText = Strings.InterruptedHint;
        }
        else if (meta.AudioRemovedAt is not null)
        {
            item.ToolTipText = Strings.AudioRemoved(Label(meta));
        }

        item.DropDownItems.Add(new ToolStripMenuItem(Strings.ShowInExplorer, null,
            (_, _) => OpenFolder(dir)));

        var mix = Path.Combine(dir, SessionMixer.FileName);
        if (File.Exists(mix))
        {
            item.DropDownItems.Add(new ToolStripMenuItem(Strings.ListenToConversation, null,
                (_, _) => OpenFile(mix)));
        }

        // Сесія без вихідних доріжок — це не поломка, а наслідок увімкненої опції.
        // Пропонувати для неї розпізнавання означало б обіцяти те, чого вже не
        // зробити: аудіо немає.
        var hasAudio = File.Exists(Path.Combine(dir, Track.Mic.File)) ||
                       File.Exists(Path.Combine(dir, Track.System.File));

        var transcript = Path.Combine(dir, Transcriber.FileName);
        if (File.Exists(transcript) || hasAudio)
        {
            item.DropDownItems.Add(TranscriptionItem(store, dir, onChanged));
        }

        item.DropDownItems.Add(new ToolStripSeparator());
        item.DropDownItems.Add(new ToolStripMenuItem(Strings.Delete, null, (_, _) =>
        {
            var answer = System.Windows.MessageBox.Show(
                Strings.DeleteConfirm(Label(meta)),
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

    /// <summary>
    /// Транскрибація з'являється лише тоді, коли її справді можна запустити.
    ///
    /// Якщо моделей немає, пункт не зникає мовчки, а прямо каже, що зробити: пункт,
    /// якого немає, читається як «продукт цього не вміє».
    /// </summary>
    private static ToolStripItem TranscriptionItem(SessionStore store, string dir, Action onChanged)
    {
        var transcript = Path.Combine(dir, Transcriber.FileName);
        if (File.Exists(transcript))
        {
            return new ToolStripMenuItem(Strings.OpenTranscript, null, (_, _) => OpenFile(transcript));
        }

        var transcriber = new Transcriber();
        if (!transcriber.IsAvailable)
        {
            return new ToolStripMenuItem(Strings.EnableTranscription, null, (_, _) =>
            {
                var window = new TranscriptionSetupWindow();
                window.Show();
                window.Activate();
            });
        }

        return new ToolStripMenuItem(Strings.Transcribe, null, async (sender, _) =>
        {
            if (sender is ToolStripMenuItem menuItem)
            {
                menuItem.Enabled = false;
                menuItem.Text = Strings.TranscribeInProgress;
            }

            try
            {
                // Той самий шлях, що й після зупинки запису: увімкнена опція має
                // означати одне й те саме, звідки б розпізнавання не запустили.
                await TranscriptionService.RunAsync(transcriber, store, dir);
                onChanged();
            }
            catch (Exception e) when (e is TranscriptionException or IOException)
            {
                System.Windows.MessageBox.Show(e.Message, "STLTH Recorder",
                                               System.Windows.MessageBoxButton.OK,
                                               System.Windows.MessageBoxImage.Warning);
            }
        });
    }

    private static string Label(SessionMeta meta)
    {
        var when = meta.StartedAt.ToString("dd.MM HH:mm", CultureInfo.CurrentCulture);
        var duration = TimeSpan.FromMilliseconds(meta.DurationMs);

        var length = duration.TotalMinutes >= 1
            ? Strings.MinutesShort((int)duration.TotalMinutes)
            : Strings.SecondsShort((int)duration.TotalSeconds);

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
