using System.Globalization;
using Stlth.Core;
using Stlth.Core.Audio;
using Stlth.Core.Storage;

// Headless-стенд: те саме ядро, що й у застосунку, без жодного UI.
//
// Існує рівно для одного — щоб ціну головного ризику (синхронність двох потоків на
// спільній шкалі QPC) можна було дізнатися до того, як на нього покладено інтерфейс,
// інсталятор і бонусні шари.

Console.OutputEncoding = System.Text.Encoding.UTF8;

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

switch (args[0])
{
    case "record":
        return Record(args.Length > 1 ? args[1] : "30");

    case "devices":
        return Devices();

    case "probe":
        return Probe(args.Length > 1 ? args[1] : "5");

    case "models":
        return Models();

    case "transcribe":
        return Transcribe(args.Length > 1 ? args[1] : string.Empty);

    case "mix":
        return Mix(args.Length > 1 ? args[1] : string.Empty);

    case "drift":
        return Stlth.Cli.DriftBench.Run(args.Length > 1 ? args[1] : "3600",
                                        args.Length > 2 ? args[2] : "10");

    default:
        PrintUsage();
        return 1;
}

static void PrintUsage()
{
    Console.WriteLine("""
        STLTH Recorder — стенд

          stlth-cli record <секунди>   записати сесію і надрукувати звіт
          stlth-cli devices            показати пристрої за замовчуванням
          stlth-cli probe <секунди>    подивитися на сирі пакети і їхні таймстемпи
          stlth-cli drift <с> [крок]   зміряти розбіжність каналів регресією
          stlth-cli mix <тека>         зібрати session.m4a для сесії
          stlth-cli models             довантажити моделі транскрибації
          stlth-cli transcribe <тека>  розпізнати сесію
        """);
}

static int Devices()
{
    Describe("Вхід ", AudioDevices.DefaultCapture());
    Describe("Вихід", AudioDevices.DefaultRender());
    return 0;

    static void Describe(string label, NAudio.CoreAudioApi.MMDevice? device)
    {
        if (device is null)
        {
            Console.WriteLine($"{label}: —");
            return;
        }

        using (device)
        {
            var volume = device.AudioEndpointVolume;
            Console.WriteLine($"{label}: {device.FriendlyName}");
            Console.WriteLine($"        гучність {volume.MasterVolumeLevelScalar * 100:F0}%, " +
                              $"{(volume.Mute ? "ВИМКНЕНО" : "увімкнено")}, " +
                              $"формат {device.AudioClient.MixFormat}");
        }
    }
}

/// <summary>
/// Дивиться на сирі пакети обох потоків: чи взагалі йдуть, чи заповнений qpcPosition,
/// який інтервал між ними. Саме тут видно, що loopback мовчить, поки нічого не грає.
/// </summary>
static int Probe(string secondsArg)
{
    if (!double.TryParse(secondsArg, CultureInfo.InvariantCulture, out var seconds))
    {
        Console.Error.WriteLine("Не число: " + secondsArg);
        return 1;
    }

    var origin = AudioDevices.NowSeconds();
    var render = AudioDevices.DefaultRender();
    var capture = AudioDevices.DefaultCapture();

    if (render is null)
    {
        Console.Error.WriteLine("Немає пристрою відтворення.");
        return 1;
    }

    var streams = new List<(string Label, WasapiStream Stream)>
    {
        ("system", new WasapiStream(render, loopback: true, AudioFormat.SystemChannels)),
    };

    if (capture is not null)
    {
        streams.Add(("mic", new WasapiStream(capture, loopback: false, AudioFormat.MicChannels)));
    }

    var counts = new Dictionary<string, int>();
    var last = new Dictionary<string, double>();

    foreach (var (label, stream) in streams)
    {
        counts[label] = 0;
        var captured = label;
        stream.PacketCaptured += packet =>
        {
            var index = counts[captured]++;
            if (index < 5 || index % 200 == 0)
            {
                var gap = last.TryGetValue(captured, out var previous)
                    ? $"{(packet.TimestampSeconds - previous) * 1000:F2} мс"
                    : "—";
                Console.WriteLine($"[{captured,6}] #{index,-5} t={packet.TimestampSeconds,8:F4} с  " +
                                  $"кадрів={packet.FrameCount,-5} крок={gap,-10} " +
                                  $"{(packet.Silent ? "тиша" : string.Empty)}");
            }

            last[captured] = packet.TimestampSeconds;
        };
        stream.Start(origin);
    }

    Console.WriteLine($"Слухаю {seconds:F0} с. Увімкніть щось, щоб системний потік ожив.\n");
    Thread.Sleep(TimeSpan.FromSeconds(seconds));

    Console.WriteLine();
    foreach (var (label, stream) in streams)
    {
        Console.WriteLine($"{label,6}: пакетів {counts[label]}, пристрій «{stream.DeviceName}», " +
                          $"qpc {(stream.QpcMissing ? "ВІДСУТНІЙ у частині пакетів" : "від драйвера")}, " +
                          $"справжнє аудіо: {(stream.SawRealAudio ? "так" : "ні")}");
        stream.Dispose();
    }

    return 0;
}

/// <summary>Розпізнати сесію тим самим кодом, яким це робить застосунок.</summary>
static int Transcribe(string dir)
{
    if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
    {
        Console.Error.WriteLine("Вкажіть теку сесії.");
        return 1;
    }

    // Застосунок шукає whisper поруч із собою; стенд запускається з іншої теки, тож
    // шлях до встановленої копії передається явно.
    var installed = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "STLTH Recorder", "whisper", "whisper-cli.exe");

    var executable = File.Exists(installed)
        ? installed
        : Stlth.Core.Transcription.Transcriber.DefaultExecutable;

    var transcriber = new Stlth.Core.Transcription.Transcriber(executable);

    if (transcriber.UnavailableReason is { } reason)
    {
        Console.Error.WriteLine(reason);
        return 1;
    }

    try
    {
        var started = DateTimeOffset.Now;
        var progress = new Progress<string>(Console.WriteLine);
        var path = transcriber.TranscribeAsync(dir, progress).GetAwaiter().GetResult();
        var elapsed = (DateTimeOffset.Now - started).TotalSeconds;

        Console.WriteLine($"\n{path}  ({elapsed:F1} с)\n");
        Console.WriteLine(File.ReadAllText(path));
        return 0;
    }
    catch (Exception e)
    {
        Console.Error.WriteLine($"{e.GetType().Name}: {e.Message}");
        return 1;
    }
}

/// <summary>
/// Довантажити моделі транскрибації з консолі — і побачити помилку повністю, а не
/// одним рядком у діалозі.
/// </summary>
static int Models()
{
    var installer = new Stlth.Core.Transcription.ModelInstaller();

    Console.WriteLine($"Тека:  {installer.Directory}");
    Console.WriteLine($"Разом: ≈ {Stlth.Core.Transcription.ModelInstaller.TotalBytes / 1_048_576} МБ");

    foreach (var model in Stlth.Core.Transcription.ModelInstaller.Required)
    {
        var path = installer.PathOf(model);
        var state = Stlth.Core.Transcription.ModelInstaller.IsComplete(path, model) ? "є" : "немає";
        Console.WriteLine($"  {model.Name,-34} {state}");
    }

    if (installer.IsInstalled)
    {
        Console.WriteLine("\nУсі моделі на місці.");
        return 0;
    }

    Console.WriteLine();

    var lastShown = -1;
    var progress = new Progress<double>(value =>
    {
        var percent = (int)(value * 100);
        if (percent == lastShown)
        {
            return;
        }

        lastShown = percent;
        Console.Write($"\rЗавантажено {percent,3}%");
    });

    try
    {
        installer.InstallAsync(progress).GetAwaiter().GetResult();
        Console.WriteLine("\nГотово.");
        return 0;
    }
    catch (Exception e)
    {
        Console.Error.WriteLine($"\n{e.GetType().Name}: {e.Message}");
        return 1;
    }
}

/// <summary>
/// Зібрати зведення руками. У застосунку це робиться у фоні і помилку ковтає —
/// файл похідний і сесії не псує. Тут навпаки: причина показується повністю, бо
/// саме для розбору полiтів команда й потрібна.
/// </summary>
static int Mix(string dir)
{
    if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
    {
        Console.Error.WriteLine("Вкажіть теку сесії.");
        return 1;
    }

    try
    {
        var started = DateTimeOffset.Now;
        var path = Stlth.Core.Mixdown.SessionMixer.Mix(dir, force: true);
        var elapsed = (DateTimeOffset.Now - started).TotalSeconds;

        Console.WriteLine($"{path}");
        Console.WriteLine($"{new FileInfo(path).Length / 1024.0 / 1024.0:F1} МБ за {elapsed:F1} с");
        return 0;
    }
    catch (Exception e)
    {
        Console.Error.WriteLine($"{e.GetType().Name}: {e.Message}");
        if (e.StackTrace is { } trace)
        {
            Console.Error.WriteLine(trace);
        }

        return 1;
    }
}

static int Record(string secondsArg)
{
    if (!double.TryParse(secondsArg, CultureInfo.InvariantCulture, out var seconds) || seconds <= 0)
    {
        Console.Error.WriteLine("Не число: " + secondsArg);
        return 1;
    }

    var store = new SessionStore();
    var controller = new RecorderController(store, dir => new AudioEngine(dir));

    Console.WriteLine($"Вхід:  {DeviceMonitor.CurrentInputName}");
    Console.WriteLine($"Вихід: {DeviceMonitor.CurrentOutputName}");
    Console.WriteLine($"Пишу {seconds:F0} с…\n");

    controller.Start(DateTimeOffset.Now, DeviceMonitor.CurrentInputName, DeviceMonitor.CurrentOutputName);

    if (controller.LastError is { } error)
    {
        Console.Error.WriteLine(error);
        return 1;
    }

    Thread.Sleep(TimeSpan.FromSeconds(seconds));
    controller.Stop();

    if (controller.LastResult is not { } result)
    {
        Console.Error.WriteLine(controller.LastError ?? "Сесія не завершилася.");
        return 1;
    }

    var micFrames = WavWriter.FramesInFile(result.MicPath);
    var systemFrames = WavWriter.FramesInFile(result.SystemPath);
    var difference = micFrames - systemFrames;

    Console.WriteLine($"Сесія:      {Path.GetFileName(controller.LastSessionDir)}");
    Console.WriteLine($"Тека:       {controller.LastSessionDir}");
    Console.WriteLine($"Тривалість: {result.DurationMs / 1000.0:F3} с");
    Console.WriteLine($"mic.wav:    {micFrames:N0} кадрів");
    Console.WriteLine($"system.wav: {systemFrames:N0} кадрів");
    Console.WriteLine($"Різниця:    {difference:N0} кадрів " +
                      $"({difference * 1000.0 / AudioFormat.SampleRate:F1} мс)");
    Console.WriteLine($"Режим:      {result.Mode}");
    Console.WriteLine($"Звук співрозмовника: {(result.SystemAudioDetected ? "є" : "НЕ ЗАФІКСОВАНО")}");

    return difference == 0 ? 0 : 2;
}
