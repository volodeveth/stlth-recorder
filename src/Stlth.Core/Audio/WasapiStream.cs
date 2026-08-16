using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Stlth.Core.Audio;

/// <summary>Один пакет, знятий з пристрою, уже в форматі доріжки.</summary>
/// <param name="TimestampSeconds">
/// Момент <b>першого кадру</b> пакета на спільній шкалі QPC, відрахований від початку
/// сесії. Саме за ним пакет кладеться в таймлайн — не за порядком надходження.
/// </param>
public readonly record struct CapturedPacket(byte[] Data, int FrameCount, double TimestampSeconds, bool Silent);

/// <summary>
/// Один потік захоплення: мікрофон або системний вивід (loopback).
///
/// Обидва режими — це той самий WASAPI shared-mode capture; різниця рівно в одному
/// прапорці. Loopback знімає мікс усього, що грає система, і не потребує ані
/// віртуальних драйверів, ані прав адміністратора.
///
/// <b>Що варто знати про loopback:</b> він не віддає жодного пакета, поки жоден процес
/// нічого не грає. Це не збій — це його природа, і саме тому довжину доріжки визначає
/// таймлайн сесії, а не потік (див. <see cref="Timeline.TimelineAccountant"/>), а
/// watchdog дивиться на стан пристрою, а не на лічильник пакетів.
/// </summary>
public sealed class WasapiStream : IDisposable
{
    /// <summary>Розмір буфера пристрою в одиницях по 100 нс — 100 мс.</summary>
    private const long BufferDuration = 1_000_000;

    private readonly MMDevice _device;
    private readonly bool _loopback;
    private readonly int _channels;

    private AudioClient? _client;
    private AudioCaptureClient? _capture;
    private Thread? _pump;
    private WaveFormat? _deviceFormat;
    private volatile bool _running;
    private double _originSeconds;

    public WasapiStream(MMDevice device, bool loopback, int channels)
    {
        _device = device;
        _loopback = loopback;
        _channels = channels;
    }

    public event Action<CapturedPacket>? PacketCaptured;

    /// <summary>Чи прийшов хоч один пакет. Доки ні — мовчання нічого не означає.</summary>
    public bool HasStarted { get; private set; }

    /// <summary>Коли востаннє приходили дані, на шкалі QPC.</summary>
    public double LastPacketSeconds { get; private set; }

    /// <summary>Чи ніс потік справжнє аудіо, а не самі лише тихі буфери.</summary>
    public bool SawRealAudio { get; private set; }

    public string DeviceName => AudioDevices.NameOf(_device);

    /// <summary>
    /// Позиція годинника самого пристрою.
    ///
    /// Це і є та «друга умова» правила watchdog, заради якої воно існує: позиція
    /// зростає, поки пристрій справді стрімить, — навіть у повній тиші, коли жодного
    /// пакета не приходить. Власний прапорець «я його запустив» такої різниці не
    /// бачить, а лічильник пакетів бачить її неправильно.
    ///
    /// Повертає 0, якщо годинник недоступний: тоді watchdog просто не спрацьовує,
    /// що безпечніше за перезапуск наосліп.
    /// </summary>
    public ulong DeviceClockPosition
    {
        get
        {
            try
            {
                return _client?.AudioClockClient.AdjustedPosition ?? 0;
            }
            catch (Exception e) when (e is COMException or InvalidOperationException or NullReferenceException)
            {
                return 0;
            }
        }
    }

    /// <param name="originSeconds">Момент старту сесії на шкалі QPC.</param>
    public void Start(double originSeconds)
    {
        _originSeconds = originSeconds;

        var client = _device.AudioClient;
        var desired = new WaveFormat(AudioFormat.SampleRate, AudioFormat.BitsPerSample, _channels);

        var flags = AudioClientStreamFlags.AutoConvertPcm | AudioClientStreamFlags.SrcDefaultQuality;
        if (_loopback)
        {
            flags |= AudioClientStreamFlags.Loopback;
        }

        // Спершу просимо саме той формат, який потрібен доріжці: тоді конвертацію
        // робить системний мікшер, і вона відбувається до того, як пакет отримає
        // таймстемп. Якщо драйвер відмовляє — беремо його рідний формат і
        // конвертуємо самі, але ніколи не пишемо аудіо у форматі, який не той,
        // що заявлений у заголовку файлу.
        try
        {
            client.Initialize(AudioClientShareMode.Shared, flags, BufferDuration, 0, desired, Guid.Empty);
            _deviceFormat = desired;
        }
        catch (COMException)
        {
            client = _device.AudioClient;
            var mix = client.MixFormat;
            client.Initialize(AudioClientShareMode.Shared,
                              _loopback ? AudioClientStreamFlags.Loopback : AudioClientStreamFlags.None,
                              BufferDuration, 0, mix, Guid.Empty);
            _deviceFormat = mix;

            if (mix.SampleRate != AudioFormat.SampleRate)
            {
                throw new InvalidOperationException(
                    $"Пристрій «{DeviceName}» працює на {mix.SampleRate} Гц і не приймає перетворення " +
                    $"до {AudioFormat.SampleRate} Гц. Змініть частоту пристрою в налаштуваннях звуку Windows.");
            }
        }

        _client = client;
        _capture = client.AudioCaptureClient;
        _running = true;
        client.Start();

        _pump = new Thread(Pump)
        {
            IsBackground = true,
            Name = _loopback ? "STLTH loopback" : "STLTH mic",
            Priority = ThreadPriority.AboveNormal,
        };
        _pump.Start();
    }

    public void Stop()
    {
        _running = false;
        _pump?.Join(TimeSpan.FromSeconds(2));
        _pump = null;

        try
        {
            _client?.Stop();
        }
        catch (COMException)
        {
            // Пристрій уже зник — зупиняти нічого.
        }
    }

    public void Dispose()
    {
        Stop();
        _capture = null;
        _client?.Dispose();
        _client = null;
        _device.Dispose();
    }

    private void Pump()
    {
        var format = _deviceFormat!;
        var passthrough = PcmConverter.IsPassthrough(format, _channels);
        var bytesPerDeviceFrame = format.BlockAlign;

        while (_running)
        {
            var captured = false;

            try
            {
                while (_running && _capture is not null && _capture.GetNextPacketSize() > 0)
                {
                    var pointer = _capture.GetBuffer(out var frames, out var flags,
                                                     out _, out var qpcPosition);
                    if (frames == 0)
                    {
                        _capture.ReleaseBuffer(0);
                        continue;
                    }

                    var silent = (flags & AudioClientBufferFlags.Silent) != 0;
                    var bytes = frames * bytesPerDeviceFrame;

                    byte[] data;
                    if (silent)
                    {
                        // Тихий буфер: дані в пам'яті недостовірні, але кадри реальні
                        // і мусять опинитися у файлі — інакше пауза вкоротить доріжку.
                        data = new byte[frames * AudioFormat.BytesPerFrame(_channels)];
                    }
                    else
                    {
                        var raw = new byte[bytes];
                        Marshal.Copy(pointer, raw, 0, bytes);
                        data = passthrough ? raw : PcmConverter.Convert(raw, frames, format, _channels);
                        SawRealAudio = true;
                    }

                    _capture.ReleaseBuffer(frames);

                    var timestamp = TimestampOf(qpcPosition, frames);
                    HasStarted = true;
                    LastPacketSeconds = AudioDevices.NowSeconds();
                    captured = true;

                    PacketCaptured?.Invoke(new CapturedPacket(data, frames, timestamp, silent));
                }
            }
            catch (COMException)
            {
                // Пристрій зник або граф розвалився: пампер завершується, а
                // перезапуском займається watchdog у движку — тут для цього немає
                // ані контексту, ані права ухвалювати рішення.
                _running = false;
                return;
            }

            if (!captured)
            {
                Thread.Sleep(5);
            }
        }
    }

    /// <summary>
    /// Момент першого кадру пакета, відрахований від початку сесії.
    ///
    /// Якщо драйвер не заповнив <paramref name="qpcPosition"/> (буває — значення
    /// приходить нулем), відступаємо на власний вимір часу мінус довжина пакета.
    /// Це гірша опора, і саме тому вона окремо помічена: далі така сесія однаково
    /// лишається цілісною, просто її синхронність тримається на джитері планувальника,
    /// а не на годиннику пристрою.
    /// </summary>
    private double TimestampOf(long qpcPosition, int frames)
    {
        if (qpcPosition > 0)
        {
            return AudioDevices.PacketSeconds(qpcPosition) - _originSeconds;
        }

        QpcMissing = true;
        return AudioDevices.NowSeconds() - _originSeconds - (frames / (double)AudioFormat.SampleRate);
    }

    /// <summary>Чи довелося бодай раз обходитися без таймстемпа від драйвера.</summary>
    public bool QpcMissing { get; private set; }
}
