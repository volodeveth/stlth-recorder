using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using Stlth.Core.Audio;

namespace Stlth.Core.Meetings;

/// <summary>
/// Хто зараз тримає мікрофон.
///
/// Джерело — сесії захоплення на мікрофонному ендпойнті: Windows веде їх сама, і
/// активна сесія означає, що процес читає з пристрою просто зараз. Ані вміст вікон,
/// ані вкладки браузера тут не читаються — лише те, хто відкрив пристрій.
/// </summary>
internal static class MicrophoneHolders
{
    /// <summary>Перший відомий застосунок для дзвінків, що тримає мікрофон.</summary>
    public static Meeting? Current()
    {
        var device = AudioDevices.DefaultCapture();
        if (device is null)
        {
            return null;
        }

        using (device)
        {
            try
            {
                var sessions = device.AudioSessionManager.Sessions;
                for (var i = 0; i < sessions.Count; i++)
                {
                    var session = sessions[i];
                    if (session.State != AudioSessionState.AudioSessionStateActive)
                    {
                        continue;
                    }

                    var processId = (int)session.GetProcessID;
                    if (processId <= 0)
                    {
                        continue;
                    }

                    var name = ProcessName(processId);
                    if (name is null || !MeetingDetector.IsMeetingProcess(name))
                    {
                        continue;
                    }

                    return new Meeting(name, Friendly(name));
                }
            }
            catch (Exception e) when (e is System.Runtime.InteropServices.COMException
                                           or InvalidOperationException)
            {
                // Ендпойнт зник між отриманням і опитуванням — наступна проба
                // за дві секунди розбереться.
            }
        }

        return null;
    }

    private static string? ProcessName(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            // Процес завершився між опитуванням сесії і цим викликом.
            return null;
        }
    }

    /// <summary>Назва, яку не соромно показати в нагадуванні.</summary>
    private static string Friendly(string processName) => processName.ToLowerInvariant() switch
    {
        "zoom" => "Zoom",
        "ms-teams" or "teams" => "Microsoft Teams",
        "chrome" => "Chrome",
        "msedge" => "Edge",
        "firefox" => "Firefox",
        "slack" => "Slack",
        "webex" or "webexmta" => "Webex",
        "discord" => "Discord",
        "skype" => "Skype",
        "whatsapp" => "WhatsApp",
        "telegram" => "Telegram",
        _ => processName,
    };
}
