using NAudio.CoreAudioApi;
using Stlth.Core.Storage;

namespace Stlth.Core.Audio;

/// <summary>
/// Зводить два незалежні потоки WASAPI на одну шкалу часу і пише їх у два файли.
///
/// <b>Головне рішення тут одне.</b> Мікрофон і системний вивід не можуть ділити
/// годинник — системного механізму для цього немає. Спільною опорою служить QPC: кожен
/// пакет несе власний <c>qpcPosition</c>, і саме за ним він кладеться в таймлайн —
/// не за порядком надходження і не за моментом, коли до нього дійшли руки.
///
/// Наслідок, який варто тримати в голові: синхронність тут забезпечена <i>вимірюваною</i>
/// величиною, а не побудовою. Тому вона й вимірюється — клік-треком на годинному прогоні.
/// </summary>
public sealed class AudioEngine : IAudioEngine
{
    private readonly string _sessionDir;
    private readonly List<DeviceChange> _deviceChanges = [];
    private readonly object _changeGate = new();
    private readonly RestartGate _restartGate = new();

    private readonly StreamSlot<WasapiStream> _mic = new();
    private readonly StreamSlot<WasapiStream> _system = new();
    private TrackRecorder? _micTrack;
    private TrackRecorder? _systemTrack;
    private DeviceMonitor? _monitor;
    private WatchdogTimer? _watchdog;

    private double _originSeconds;
    private string _inputDeviceName = "—";
    private string _outputDeviceName = "—";
    private CaptureMode _mode = CaptureMode.QpcAnchored;
    private ulong _lastSystemClock;
    private ulong _lastMicClock;
    private bool _stopped;

    public AudioEngine(string sessionDir) => _sessionDir = sessionDir;

    /// <summary>Пристрій змінився посеред запису — подія для <c>meta.json</c>.</summary>
    public event EventHandler<DeviceChange>? DeviceChanged;

    public void Start()
    {
        _originSeconds = AudioDevices.NowSeconds();

        var render = AudioDevices.DefaultRender()
            ?? throw new InvalidOperationException(
                Localization.Strings.NoRenderDevice);
        _micTrack = new TrackRecorder(Path.Combine(_sessionDir, Track.Mic.File),
                                      AudioFormat.MicChannels, startAt: 0);
        _systemTrack = new TrackRecorder(Path.Combine(_sessionDir, Track.System.File),
                                         AudioFormat.SystemChannels, startAt: 0);

        // Мікрофон піднімається першим і навмисно не є фатальним: якщо його немає або
        // дозвіл не видано, розмову однаково варто записати з одного боку, ніж не
        // записати взагалі. Сесія тоді чесно позначається як system-only.
        var capture = AudioDevices.DefaultCapture();
        if (capture is null)
        {
            _mode = CaptureMode.SystemOnly;
            _inputDeviceName = "—";
        }
        else
        {
            try
            {
                _mic.Replace(
                    () => CreateStream(capture, loopback: false, AudioFormat.MicChannels),
                    stream =>
                    {
                        AttachMic(stream);
                        stream.Start(_originSeconds);
                    });
                _inputDeviceName = _mic.Current!.DeviceName;
            }
            catch (Exception e) when (RestartFault.IsDeviceFault(e))
            {
                _mic.Clear();
                _mode = CaptureMode.SystemOnly;
            }
        }

        _system.Replace(
            () => CreateStream(render, loopback: true, AudioFormat.SystemChannels),
            stream =>
            {
                AttachSystem(stream);
                stream.Start(_originSeconds);
            });
        _outputDeviceName = _system.Current!.DeviceName;

        _monitor = new DeviceMonitor();
        _monitor.DefaultDeviceChanged += OnDefaultDeviceChanged;
        _monitor.Start();

        _watchdog = new WatchdogTimer(CheckStreams, TimeSpan.FromSeconds(1));
    }

    public RecordingResult Stop()
    {
        if (_stopped)
        {
            // Внутрішній інваріант, не текст інтерфейсу: побачити його може лише розробник.
            throw new InvalidOperationException("The engine is already stopped.");
        }

        _stopped = true;
        _restartGate.Close();

        _watchdog?.Stop();
        _watchdog = null;

        if (_monitor is not null)
        {
            _monitor.DefaultDeviceChanged -= OnDefaultDeviceChanged;
            _monitor.Dispose();
            _monitor = null;
        }

        _mic.Current?.Stop();
        _system.Current?.Stop();

        // Довжину визначає тривалість сесії, а не останній пакет, який випадково
        // прилетів: потік міг замовкнути задовго до натискання «Зупинити».
        var end = AudioDevices.NowSeconds() - _originSeconds;
        _micTrack?.PadTo(end);
        _systemTrack?.PadTo(end);

        var frames = Math.Min(_micTrack?.TotalFrames ?? 0, _systemTrack?.TotalFrames ?? 0);
        var durationMs = (int)Math.Round(frames * 1000.0 / AudioFormat.SampleRate);
        var systemAudioDetected = _system.Current?.SawRealAudio ?? false;

        _micTrack?.Dispose();
        _systemTrack?.Dispose();
        _micTrack = null;
        _systemTrack = null;

        DeviceChange[] changes;
        lock (_changeGate)
        {
            changes = [.. _deviceChanges];
        }

        return new RecordingResult(
            Path.Combine(_sessionDir, Track.Mic.File),
            Path.Combine(_sessionDir, Track.System.File),
            durationMs,
            _inputDeviceName,
            _outputDeviceName,
            _mode,
            changes,
            systemAudioDetected);
    }

    public void Dispose()
    {
        _stopped = true;
        _restartGate.Close();

        _watchdog?.Stop();
        _watchdog = null;

        if (_monitor is not null)
        {
            _monitor.DefaultDeviceChanged -= OnDefaultDeviceChanged;
            _monitor.Dispose();
            _monitor = null;
        }

        _mic.Clear();
        _system.Clear();
        _micTrack?.Dispose();
        _micTrack = null;
        _systemTrack?.Dispose();
        _systemTrack = null;
    }

    /// <summary>
    /// Раз на секунду звіряє кожен потік із правилом watchdog.
    ///
    /// Ознака збою — не мовчання саме по собі, а мовчання при годиннику пристрою, що
    /// продовжує йти. Розрив, який виникне при перезапуску, заллється тишею
    /// автоматично: таймлайн рахує від QPC, а не від кількості отриманих пакетів.
    /// </summary>
    private void CheckStreams()
    {
        if (_stopped)
        {
            return;
        }

        Check(_system, ref _lastSystemClock, isLoopback: true);
        Check(_mic, ref _lastMicClock, isLoopback: false);
    }

    private void Check(StreamSlot<WasapiStream> slot, ref ulong lastClock, bool isLoopback)
    {
        if (slot.NeedsRetry)
        {
            try
            {
                _restartGate.TryRun(() => Restart(isLoopback));
            }
            catch (Exception e) when (RestartFault.IsDeviceFault(e))
            {
                // The next watchdog check will retry the failed slot.
            }

            return;
        }

        var stream = slot.Current;
        if (stream is null)
        {
            return;
        }

        var clock = stream.DeviceClockPosition;
        var running = clock > lastClock;
        lastClock = clock;

        var silence = TimeSpan.FromSeconds(AudioDevices.NowSeconds() - stream.LastPacketSeconds);
        if (!WatchdogRule.ShouldRestart(stream.HasStarted, running, silence))
        {
            return;
        }

        try
        {
            _restartGate.TryRun(() => Restart(isLoopback));
        }
        catch (Exception e) when (RestartFault.IsDeviceFault(e))
        {
            // Перезапуск не вдався — сесія триває на тому, що є. Тиша в цьому місці
            // буде записана як тиша, і доріжка лишиться потрібної довжини.
        }
    }

    private bool Restart(bool isLoopback)
    {
        if (isLoopback)
        {
            var device = AudioDevices.DefaultRender();
            if (device is null)
            {
                return false;
            }

            _system.Replace(
                () => CreateStream(device, loopback: true, AudioFormat.SystemChannels),
                stream =>
                {
                    AttachSystem(stream);
                    stream.Start(_originSeconds);
                });
            _outputDeviceName = _system.Current!.DeviceName;
            return true;
        }

        var capture = AudioDevices.DefaultCapture();
        if (capture is null)
        {
            return false;
        }

        _mic.Replace(
            () => CreateStream(capture, loopback: false, AudioFormat.MicChannels),
            stream =>
            {
                AttachMic(stream);
                stream.Start(_originSeconds);
            });
        _inputDeviceName = _mic.Current!.DeviceName;
        return true;
    }

    private void OnDefaultDeviceChanged(string? input, string? output)
    {
        if (_stopped)
        {
            return;
        }

        var change = new DeviceChange(DateTimeOffset.Now, input, output);
        lock (_changeGate)
        {
            _deviceChanges.Add(change);
        }

        try
        {
            DeviceSwitch.Apply(input, output, isLoopback =>
            {
                var restartRan = false;
                var gateOpened = _restartGate.TryRun(() => restartRan = Restart(isLoopback));
                return gateOpened && restartRan;
            });
        }
        catch (Exception e) when (RestartFault.IsDeviceFault(e))
        {
            // Новий пристрій не піднявся: подія однаково лягла в meta.json, а
            // прогалину заллє тиша. Сесія лишається читабельною і чесною.
        }

        DeviceChanged?.Invoke(this, change);
    }

    private static WasapiStream CreateStream(NAudio.CoreAudioApi.MMDevice device, bool loopback, int channels)
    {
        try
        {
            return new WasapiStream(device, loopback, channels);
        }
        catch
        {
            device.Dispose();
            throw;
        }
    }

    private void AttachMic(WasapiStream stream)
    {
        stream.PacketCaptured += packet =>
            _micTrack?.Write(packet.Data, packet.FrameCount, packet.TimestampSeconds);
    }

    private void AttachSystem(WasapiStream stream)
    {
        stream.PacketCaptured += packet =>
            _systemTrack?.Write(packet.Data, packet.FrameCount, packet.TimestampSeconds);
    }
}
