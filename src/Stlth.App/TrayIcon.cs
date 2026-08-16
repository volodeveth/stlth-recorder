using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Threading;
using Stlth.Core;
using Stlth.Core.Audio;
using Stlth.Core.Localization;
using Stlth.Core.Meetings;
using Stlth.Core.Permissions;
using Stlth.Core.Storage;
using Application = System.Windows.Application;

namespace Stlth.App;

/// <summary>
/// Іконка в треї — весь інтерфейс застосунку у звичайному стані.
///
/// Меню перебудовується <b>при кожному відкритті</b>, а не один раз на старті: інакше
/// список записів застигає на моменті запуску і дозволи показуються ті, що були
/// колись. Це не гіпотеза, а спостережена поведінка.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly RecorderController _controller;
    private readonly NotifyIcon _icon;
    private readonly DispatcherTimer _tick;
    private readonly Icon _idleIcon;
    private readonly Icon _recordingIcon;
    private readonly MeetingWatcher _meetings;

    private ToolStripMenuItem _startStop = null!;
    private ToolStripMenuItem _status = null!;

    /// <summary>
    /// Чи відкритий зараз діалог перед стартом.
    ///
    /// State machine не дає створити другу сесію, але вона нічого не знає про
    /// діалоги: до неї справа доходить уже після підтвердження. Без цього прапорця
    /// два кліки по іконці дають два вікна згоди одне поверх одного — саме це і
    /// показав перший прохід по інтерфейсу.
    /// </summary>
    private bool _dialogOpen;

    public TrayIcon(RecorderController controller)
    {
        _controller = controller;
        _idleIcon = LoadIcon("idle");
        _recordingIcon = LoadIcon("recording");

        _icon = new NotifyIcon
        {
            Icon = _idleIcon,
            Text = "STLTH Recorder",
            Visible = false,
            ContextMenuStrip = new ContextMenuStrip { ShowImageMargin = false },
        };

        _icon.ContextMenuStrip.Opening += (_, _) => BuildMenu();
        _icon.MouseClick += OnMouseClick;

        _tick = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(0.5),
        };
        _tick.Tick += (_, _) => Refresh();

        _controller.Changed += (_, _) => Application.Current.Dispatcher.Invoke(Refresh);

        _meetings = new MeetingWatcher();
        _meetings.Started += OnMeetingStarted;
        _meetings.Ended += OnMeetingEnded;
    }

    public void Show()
    {
        BuildMenu();
        _icon.Visible = true;
        Refresh();

        if (App.Settings.MeetingReminders)
        {
            _meetings.Start();
        }
    }

    /// <summary>
    /// Почалася зустріч — варто спитати, чи вмикати запис.
    ///
    /// Саме спитати. Продукт ніколи не вмикає і не вимикає запис сам: автостарт
    /// писав би розмови, на які згоди не давали, а автостоп обірвав би першу ж
    /// зустріч, у якій хтось на хвилину вимкнув мікрофон.
    /// </summary>
    private void OnMeetingStarted(Meeting meeting)
    {
        if (_controller.IsRecording)
        {
            return;
        }

        Application.Current.Dispatcher.Invoke(() =>
            Notify(Strings.MeetingStartedTitle(meeting.AppName), Strings.MeetingStartedBody));
    }

    /// <summary>
    /// Зустріч завершилася, а запис триває.
    ///
    /// Це нагадування важливіше за перше. Забути ввімкнути — втратити розмову;
    /// забути вимкнути — писати кімнату далі, збираючи аудіо, на яке ніхто згоди не
    /// давав, у сесію, чий meta.json стверджує протилежне.
    /// </summary>
    private void OnMeetingEnded()
    {
        if (!_controller.IsRecording)
        {
            return;
        }

        Application.Current.Dispatcher.Invoke(() =>
            Notify(Strings.MeetingEndedTitle, Strings.MeetingEndedBody));
    }

    /// <summary>Увімкнути або вимкнути спостереження за зустрічами на льоту.</summary>
    public void SetMeetingReminders(bool enabled)
    {
        if (enabled)
        {
            _meetings.Start();
        }
        else
        {
            _meetings.Stop();
        }
    }

    public void Notify(string title, string message)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = message;
        _icon.ShowBalloonTip(6000);
    }

    public void Dispose()
    {
        _tick.Stop();
        _meetings.Dispose();
        _icon.Visible = false;
        _icon.Dispose();
        _idleIcon.Dispose();
        _recordingIcon.Dispose();
    }

    private static Icon LoadIcon(string name)
    {
        var uri = new Uri($"pack://application:,,,/Resources/{name}.ico");
        using var stream = Application.GetResourceStream(uri)!.Stream;
        return new Icon(stream);
    }

    private void OnMouseClick(object? sender, MouseEventArgs e)
    {
        // Лівий клік — найшвидший шлях до старту й зупинки. Меню лишається для
        // всього іншого.
        if (e.Button == MouseButtons.Left)
        {
            ToggleRecording();
        }
    }

    private void BuildMenu()
    {
        var menu = _icon.ContextMenuStrip!;
        menu.Items.Clear();

        _startStop = new ToolStripMenuItem(
            _controller.IsRecording ? Strings.StopRecording : Strings.StartRecording,
            null, (_, _) => ToggleRecording())
        {
            Font = new Font(menu.Font, System.Drawing.FontStyle.Bold),
        };
        menu.Items.Add(_startStop);

        _status = new ToolStripMenuItem(StatusText()) { Enabled = false };
        menu.Items.Add(_status);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(RecentSessionsMenu.Build(_controller.Store, Rebuild));
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add(PermissionItem());
        menu.Items.Add(new ToolStripMenuItem(Strings.SettingsMenu, null, (_, _) => OpenSettings()));
        menu.Items.Add(new ToolStripMenuItem(Strings.About, null, (_, _) => ShowAbout()));

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem(Strings.Quit, null, (_, _) => Application.Current.Shutdown()));
    }

    public void Rebuild() => Application.Current.Dispatcher.Invoke(BuildMenu);

    private ToolStripItem PermissionItem()
    {
        var state = App.Settings.RememberedMicPermission;
        if (state == MicPermission.Granted)
        {
            return new ToolStripMenuItem(Strings.MicGranted) { Enabled = false };
        }

        var label = state switch
        {
            MicPermission.Denied => Strings.MicDeniedMenu,
            MicPermission.NoDevice => Strings.MicMissing,
            _ => Strings.MicCheck,
        };

        return new ToolStripMenuItem(label, null, (_, _) =>
        {
            if (state == MicPermission.Denied)
            {
                MicrophonePermission.OpenSettings();
                return;
            }

            App.Settings.RememberedMicPermission = MicrophonePermission.Probe();
            App.Settings.Save();
            Rebuild();
        });
    }

    private string StatusText() => _controller.State switch
    {
        RecorderState.Recording => Strings.RecordingFor(Format(_controller.Elapsed)),
        RecorderState.Preparing => Strings.Preparing,
        RecorderState.Stopping => Strings.Stopping,
        _ => _controller.LastError ?? (TranscriptionService.IsBusy
            ? Strings.TranscribeInProgress
            : Strings.Ready),
    };

    private static string Format(TimeSpan elapsed) =>
        elapsed.TotalHours >= 1
            ? $"{(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}"
            : $"{elapsed.Minutes:00}:{elapsed.Seconds:00}";

    private void Refresh()
    {
        var recording = _controller.IsRecording;

        _icon.Icon = recording ? _recordingIcon : _idleIcon;

        // Стан має бути видно, не відкриваючи меню: підказка над іконкою — єдине,
        // що людина бачить, не клікнувши.
        _icon.Text = recording
            ? Strings.TrayRecording(Format(_controller.Elapsed))
            : "STLTH Recorder";

        if (_icon.ContextMenuStrip?.Visible == true)
        {
            _status.Text = StatusText();
            _startStop.Text = recording ? Strings.StopRecording : Strings.StartRecording;
        }

        if (recording && !_tick.IsEnabled)
        {
            _tick.Start();
        }
        else if (!recording && _tick.IsEnabled)
        {
            _tick.Stop();
        }
    }

    private void ToggleRecording()
    {
        if (_controller.IsRecording)
        {
            StopRecording();
            return;
        }

        StartRecording();
    }

    private void StartRecording()
    {
        if (_controller.State != RecorderState.Idle || _dialogOpen)
        {
            return;
        }

        _dialogOpen = true;
        try
        {
            // Порядок тут строгий і не випадковий: дозвіл питається ДО того, як движок
            // торкнеться аудіо-API. Заблокований системний виклик підвішує інтерфейс
            // намертво, без діалога на екрані і без способу вийти.
            var permission = MicrophonePermission.Probe();
            App.Settings.RememberedMicPermission = permission;
            App.Settings.Save();

            if (permission == MicPermission.Denied && !PermissionWindow.AskToContinue())
            {
                return;
            }

            var input = DeviceMonitor.CurrentInputName;
            var output = DeviceMonitor.CurrentOutputName;

            var consent = ConsentWindow.Ask(SessionStore.DefaultRoot, output);
            if (consent is not { } consentAt)
            {
                return;
            }

            _controller.Start(consentAt, input, output);

            if (_controller.LastError is { } error)
            {
                Notify(Strings.StartFailedTitle, error);
                return;
            }

            // Людина щойно ухвалила рішення — питати її про ту саму зустріч удруге
            // означало б навчити її закривати нагадування не читаючи.
            _meetings.SuppressForCurrentMeeting();
        }
        finally
        {
            _dialogOpen = false;
        }
    }

    private void StopRecording()
    {
        _controller.Stop();

        if (_controller.LastResult is not { } result)
        {
            return;
        }

        if (!result.SystemAudioDetected)
        {
            // Мовчазна доріжка співрозмовника — найгірший спосіб дізнатися, що
            // звук грав не на тому пристрої: про це треба казати одразу, а не
            // залишати людину відкривати файл через тиждень.
            Notify(Strings.NoPeerAudioTitle, Strings.NoPeerAudioBody(result.OutputDeviceName));
        }

        if (_controller.LastSessionDir is not { } dir)
        {
            return;
        }

        if (App.Settings.BuildMixdown)
        {
            MixdownService.BuildInBackground(_controller.Store, dir);
        }

        if (App.Settings.AutoTranscribe)
        {
            StartTranscription(dir);
        }
    }

    /// <summary>
    /// Поставити щойно завершену сесію в чергу на розпізнавання.
    ///
    /// Мовчить, якщо моделей немає: пропозиція їх поставити живе в меню сесії і не
    /// має вискакувати після кожного запису.
    /// </summary>
    private void StartTranscription(string sessionDir)
    {
        TranscriptionService.Enqueue(_controller.Store, sessionDir, (path, error) =>
            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (path is not null)
                {
                    Notify(Strings.TranscriptReadyTitle, Strings.TranscriptReadyBody);
                }
                else if (error is not null)
                {
                    Notify(Strings.TranscriptFailedTitle, error);
                }

                Refresh();
            }));

        Refresh();
    }

    private static void OpenSettings()
    {
        var window = new SettingsWindow();
        window.Show();
        window.Activate();
    }

    private void ShowAbout()
    {
        var version = typeof(TrayIcon).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        Notify("STLTH Recorder " + version, Strings.AboutBody);
    }
}
