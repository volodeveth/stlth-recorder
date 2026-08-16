using System.Text;

namespace Stlth.Core.Storage;

/// <summary>
/// Лікує WAV-файли, які лишив по собі крах.
///
/// Поки файл пишеться, поля розміру в заголовку — нулі; справжні значення
/// дописуються при закритті. Аварійне завершення процесу цей крок пропускає, і
/// далі виходить асиметрія: одні читачі беруть розмір із фактичної довжини файлу,
/// інші вірять заголовку і бачать порожню доріжку.
///
/// «Файли пишуться потоково» — це ще не «файли переживають крах». Різницю видно
/// лише тоді, коли справді вбиваєш процес.
///
/// Операція ідемпотентна і <b>не торкається жодного байта аудіо</b>: переписуються
/// рівно два 32-бітні поля.
/// </summary>
public static class WavRepair
{
    private const int RiffSizeOffset = 4;
    private const int DataSizeOffset = 40;

    /// <returns><c>true</c>, якщо файл було змінено.</returns>
    public static bool RepairIfNeeded(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < WavWriter.HeaderSize)
            {
                return false;
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            var header = new byte[WavWriter.HeaderSize];
            if (stream.Read(header, 0, header.Length) != header.Length)
            {
                return false;
            }

            if (Encoding.ASCII.GetString(header, 0, 4) != "RIFF" ||
                Encoding.ASCII.GetString(header, 8, 4) != "WAVE")
            {
                return false;
            }

            var expectedData = (uint)(info.Length - WavWriter.HeaderSize);
            var expectedRiff = (uint)(info.Length - 8);
            var declaredData = BitConverter.ToUInt32(header, DataSizeOffset);
            var declaredRiff = BitConverter.ToUInt32(header, RiffSizeOffset);

            if (declaredData == expectedData && declaredRiff == expectedRiff)
            {
                return false;
            }

            stream.Seek(RiffSizeOffset, SeekOrigin.Begin);
            stream.Write(BitConverter.GetBytes(expectedRiff));
            stream.Seek(DataSizeOffset, SeekOrigin.Begin);
            stream.Write(BitConverter.GetBytes(expectedData));
            stream.Flush(flushToDisk: true);
            return true;
        }
        catch (IOException)
        {
            // Лікування ніколи не має валити відновлення сесії: краще лишити файл
            // як є, ніж втратити решту сесій через один недоступний.
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
