using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace Stlth.Core.Audio;

/// <summary>
/// Стежить, коли Windows перемикає пристрій за замовчуванням — навушники підключили
/// посеред розмови, гарнітура від'єдналася, віртуальний пристрій зник.
///
/// Сам нічого не перебудовує: лише повідомляє. Рішення, що з цим робити, належить
/// движку, який знає, чи триває запис.
/// </summary>
public sealed class DeviceMonitor : IDisposable, IMMNotificationClient
{
    private MMDeviceEnumerator? _enumerator;

    /// <summary>Новий пристрій вводу і/або виводу за замовчуванням.</summary>
    public event Action<string?, string?>? DefaultDeviceChanged;

    public static string CurrentInputName
    {
        get
        {
            using var device = AudioDevices.DefaultCapture();
            return AudioDevices.NameOf(device);
        }
    }

    public static string CurrentOutputName
    {
        get
        {
            using var device = AudioDevices.DefaultRender();
            return AudioDevices.NameOf(device);
        }
    }

    public void Start()
    {
        if (_enumerator is not null)
        {
            return;
        }

        _enumerator = new MMDeviceEnumerator();
        _enumerator.RegisterEndpointNotificationCallback(this);
    }

    public void Dispose()
    {
        if (_enumerator is null)
        {
            return;
        }

        try
        {
            _enumerator.UnregisterEndpointNotificationCallback(this);
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            // Енумератор уже пішов разом із сесією аудіо — знімати нічого.
        }

        _enumerator.Dispose();
        _enumerator = null;
    }

    void IMMNotificationClient.OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        // Console — та роль, якою користуються застосунки для дзвінків; решта
        // (Multimedia, Communications) на тому самому пристрої лише дублювала б подію.
        if (role != Role.Console)
        {
            return;
        }

        DefaultDeviceChanged?.Invoke(
            flow == DataFlow.Capture ? CurrentInputName : null,
            flow == DataFlow.Render ? CurrentOutputName : null);
    }

    void IMMNotificationClient.OnDeviceStateChanged(string deviceId, DeviceState newState)
    {
    }

    void IMMNotificationClient.OnDeviceAdded(string pwstrDeviceId)
    {
    }

    void IMMNotificationClient.OnDeviceRemoved(string deviceId)
    {
    }

    void IMMNotificationClient.OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
    {
    }
}
