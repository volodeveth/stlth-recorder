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

    private WasapiStream? _mic;
    private WasapiStream? _system;
    private TrackRecorder? _micTrack;
    private TrackRecorder? _systemTrack;
    private DeviceMonitor? _monitor;
    private Timer? _watchdog;

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
        _outputDeviceName = AudioDevices.NameOf(render);

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
            _inputDeviceName = AudioDevices.NameOf(capture);
            try
            {
                _mic = new WasapiStream(capture, loopback: false, AudioFormat.MicChannels);
                _mic.PacketCaptured += packet =>
                    _micTrack?.Write(packet.Data, packet.FrameCount, packet.TimestampSeconds);
                _mic.Start(_originSeconds);
            }
            catch (Exception e) when (e is System.Runtime.InteropServices.COMException
                                           or InvalidOperationException
                                           or UnauthorizedAccessException)
            {
                _mic?.Dispose();
                _mic = null;
                _mode = CaptureMode.SystemOnly;
            }
        }

        _system = new WasapiStream(render, loopback: true, AudioFormat.SystemChannels);
        _system.PacketCaptured += packet =>
            _systemTrack?.Write(packet.Data, packet.FrameCount, packet.TimestampSeconds);
        _system.Start(_originSeconds);

        _monitor = new DeviceMonitor();
        _monitor.DefaultDeviceChanged += OnDefaultDeviceChanged;
        _monitor.Start();

        _watchdog = new Timer(_ => CheckStreams(), null,
                              TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    public RecordingResult Stop()
    {
        if (_stopped)
        {
            // Внутрішній інваріант, не текст інтерфейсу: побачити його може лише розробник.
            throw new InvalidOperationException("The engine is already stopped.");
        }

        _stopped = true;

        _watchdog?.Dispose();
        _watchdog = null;

        if (_monitor is not null)
        {
            _monitor.DefaultDeviceChanged -= OnDefaultDeviceChanged;
            _monitor.Dispose();
            _monitor = null;
        }

        _mic?.Stop();
        _system?.Stop();

        // Довжину визначає тривалість сесії, а не останній пакет, який випадково
        // прилетів: потік міг замовкнути задовго до натискання «Зупинити».
        var end = AudioDevices.NowSeconds() - _originSeconds;
        _micTrack?.PadTo(end);
        _systemTrack?.PadTo(end);

        var frames = Math.Min(_micTrack?.TotalFrames ?? 0, _systemTrack?.TotalFrames ?? 0);
        var durationMs = (int)Math.Round(frames * 1000.0 / AudioFormat.SampleRate);
        var systemAudioDetected = _system?.SawRealAudio ?? false;

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
        _watchdog?.Dispose();
        _watchdog = null;
        _monitor?.Dispose();
        _monitor = null;
        _mic?.Dispose();
        _mic = null;
        _system?.Dispose();
        _system = null;
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

    private void Check(WasapiStream? stream, ref ulong lastClock, bool isLoopback)
    {
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
            Restart(isLoopback);
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException
                                       or InvalidOperationException)
        {
            // Перезапуск не вдався — сесія триває на тому, що є. Тиша в цьому місці
            // буде записана як тиша, і доріжка лишиться потрібної довжини.
        }
    }

    private void Restart(bool isLoopback)
    {
        if (isLoopback)
        {
            var device = AudioDevices.DefaultRender();
            if (device is null)
            {
                return;
            }

            _system?.Dispose();
            _system = new WasapiStream(device, loopback: true, AudioFormat.SystemChannels);
            _system.PacketCaptured += packet =>
                _systemTrack?.Write(packet.Data, packet.FrameCount, packet.TimestampSeconds);
            _system.Start(_originSeconds);
            _outputDeviceName = _system.DeviceName;
        }
        else
        {
            var device = AudioDevices.DefaultCapture();
            if (device is null)
            {
                return;
            }

            _mic?.Dispose();
            _mic = new WasapiStream(device, loopback: false, AudioFormat.MicChannels);
            _mic.PacketCaptured += packet =>
                _micTrack?.Write(packet.Data, packet.FrameCount, packet.TimestampSeconds);
            _mic.Start(_originSeconds);
            _inputDeviceName = _mic.DeviceName;
        }
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
            if (input is not null)
            {
                _inputDeviceName = input;
                Restart(isLoopback: false);
            }

            if (output is not null)
            {
                _outputDeviceName = output;
                Restart(isLoopback: true);
            }
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException
                                       or InvalidOperationException)
        {
            // Новий пристрій не піднявся: подія однаково лягла в meta.json, а
            // прогалину заллє тиша. Сесія лишається читабельною і чесною.
        }

        DeviceChanged?.Invoke(this, change);
    }
}
