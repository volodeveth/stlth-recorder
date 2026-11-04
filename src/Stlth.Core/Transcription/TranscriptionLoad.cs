using System.Diagnostics;

namespace Stlth.Core.Transcription;

/// <summary>
/// Скільки машини забирає розпізнавання.
///
/// Без явного <c>-t</c> whisper-cli бере 4 потоки на будь-якій машині: на 18 логічних
/// процесорах 40-хвилинна розмова розпізнавалася близько 100 хвилин. Але й забрати весь
/// процесор не можна — розпізнавання йде одразу після дзвінка, коли людина далі працює.
/// Тому обидві доріжки разом беруть не більше половини логічних процесорів і поступаються
/// пріоритетом усьому, що людина робить у цей час.
/// </summary>
public static class TranscriptionLoad
{
    /// <summary>Чверть логічних процесорів на доріжку: дві доріжки паралельно — половина.</summary>
    public static int ThreadsPerTrack(int logicalProcessors) => Math.Max(1, logicalProcessors / 4);

    public static ProcessPriorityClass Priority => ProcessPriorityClass.BelowNormal;
}
