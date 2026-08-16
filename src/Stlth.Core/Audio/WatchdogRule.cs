namespace Stlth.Core.Audio;

/// <summary>
/// Коли мовчання потоку означає збій, а коли — просто тишу.
///
/// Наївне правило «немає даних N секунд → перезапустити» тут хибне: WASAPI loopback
/// законно не віддає нічого, поки жоден процес не грає, тож такий watchdog смикав би
/// запис на кожній паузі в розмові.
///
/// Справжній збій має характерний підпис — <b>пару</b> умов: потік уже стартував
/// <i>і</i> пристрій звітує, що працює, <i>а</i> даних немає. Голий лічильник пакетів
/// такої різниці не бачить.
/// </summary>
public static class WatchdogRule
{
    /// <summary>Скільки мовчання при працюючому пристрої вважати збоєм.</summary>
    public static readonly TimeSpan Silence = TimeSpan.FromSeconds(3);

    public static bool ShouldRestart(bool streamStarted, bool deviceRunning, TimeSpan sinceLastPacket)
        => streamStarted && deviceRunning && sinceLastPacket > Silence;
}
