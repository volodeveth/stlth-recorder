using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Stlth.Core.Audio;

namespace Stlth.Core.Permissions;

public enum MicPermission
{
    Unknown,
    Granted,
    Denied,

    /// <summary>Мікрофона в системі немає взагалі — це не відмова в дозволі.</summary>
    NoDevice,
}

/// <summary>
/// Стан доступу до мікрофона.
///
/// Визначається <b>фактичною спробою</b> підняти потік, а не читанням приватних API:
/// приватний виклик — це ризик на кожному оновленні системи заради відповіді, яку
/// однаково доводиться перевіряти дією.
///
/// Системний звук окремого дозволу не потребує взагалі. Це властивість платформи, а
/// не заслуга застосунку, і подавати її як перевагу продукту було б нечесно.
/// </summary>
public static class MicrophonePermission
{
    /// <summary>HRESULT для «доступ заборонено».</summary>
    private const int AccessDenied = unchecked((int)0x80070005);

    public static MicPermission Probe()
    {
        var device = AudioDevices.DefaultCapture();
        if (device is null)
        {
            return MicPermission.NoDevice;
        }

        using (device)
        {
            try
            {
                var client = device.AudioClient;
                client.Initialize(
                    AudioClientShareMode.Shared,
                    AudioClientStreamFlags.AutoConvertPcm | AudioClientStreamFlags.SrcDefaultQuality,
                    1_000_000,
                    0,
                    new WaveFormat(AudioFormat.SampleRate, AudioFormat.BitsPerSample, AudioFormat.MicChannels),
                    Guid.Empty);
                client.Dispose();
                return MicPermission.Granted;
            }
            catch (COMException e) when (e.HResult == AccessDenied)
            {
                return MicPermission.Denied;
            }
            catch (UnauthorizedAccessException)
            {
                return MicPermission.Denied;
            }
            catch (Exception e) when (e is COMException or InvalidOperationException)
            {
                // Пристрій зайнятий в ексклюзивному режимі або відповів помилкою, не
                // пов'язаною з доступом. Це не «заборонено» — і казати користувачеві
                // «видайте дозвіл» тут було б неправдою.
                return MicPermission.Unknown;
            }
        }
    }

    /// <summary>Відкрити сторінку приватності мікрофона в налаштуваннях Windows.</summary>
    public static void OpenSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:privacy-microphone")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Немає чим відкрити — користувач знайде налаштування сам, а падати
            // через це застосунок не має.
        }
    }

    public static string Describe(MicPermission permission) => permission switch
    {
        MicPermission.Granted => "Мікрофон: доступ є",
        MicPermission.Denied => "Мікрофон: доступ заборонено",
        MicPermission.NoDevice => "Мікрофон: пристрою немає",
        _ => "Мікрофон: стан невідомий",
    };
}
