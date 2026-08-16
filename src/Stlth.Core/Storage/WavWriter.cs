using System.Text;

namespace Stlth.Core.Storage;

/// <summary>
/// Пише доріжку потоково: заголовок наперед, аудіо — по ходу, розміри — на закритті.
///
/// Потоковий запис означає, що аварійне завершення процесу не забирає з собою
/// записане аудіо. Ціною того, що поля розміру в заголовку лишаються нульовими —
/// це лікує <see cref="WavRepair"/>.
/// </summary>
public sealed class WavWriter : IDisposable
{
    /// <summary>Канонічний RIFF-заголовок: 12 байтів RIFF/WAVE + 24 fmt + 8 data.</summary>
    public const int HeaderSize = 44;

    private const int DataSizeOffset = 40;
    private const int RiffSizeOffset = 4;

    /// <summary>Порція, якою заливається тиша — щоб не алокувати мегабайти на паузу.</summary>
    private const int SilenceChunkBytes = 32 * 1024;

    private readonly FileStream _stream;
    private readonly int _bytesPerFrame;
    private bool _disposed;

    public WavWriter(string path, int channels)
    {
        _bytesPerFrame = AudioFormat.BytesPerFrame(channels);
        _stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read,
                                 bufferSize: 64 * 1024, FileOptions.SequentialScan);
        WriteHeader(channels);
    }

    /// <summary>Кадрів у файлі — записане аудіо разом із вставленою тишею.</summary>
    public long FramesWritten { get; private set; }

    public void Write(ReadOnlySpan<byte> pcm)
    {
        _stream.Write(pcm);
        FramesWritten += pcm.Length / _bytesPerFrame;
    }

    /// <summary>
    /// Дописати <paramref name="frames"/> кадрів тиші.
    ///
    /// Тиша пишеться як тиша і ніколи не вирізається: інакше десять хвилин mute
    /// зробили б цю доріжку рівно на десять хвилин коротшою за сусідню.
    /// </summary>
    public void WriteSilence(long frames)
    {
        if (frames <= 0)
        {
            return;
        }

        var remaining = frames * _bytesPerFrame;
        var chunk = new byte[(int)Math.Min(remaining, SilenceChunkBytes)];

        while (remaining > 0)
        {
            var take = (int)Math.Min(remaining, chunk.Length);
            _stream.Write(chunk, 0, take);
            remaining -= take;
        }

        FramesWritten += frames;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        var length = _stream.Length;
        _stream.Seek(RiffSizeOffset, SeekOrigin.Begin);
        _stream.Write(BitConverter.GetBytes((uint)(length - 8)));
        _stream.Seek(DataSizeOffset, SeekOrigin.Begin);
        _stream.Write(BitConverter.GetBytes((uint)(length - HeaderSize)));
        _stream.Flush(flushToDisk: true);
        _stream.Dispose();
    }

    /// <summary>
    /// Скільки кадрів насправді лежить у файлі.
    ///
    /// Заголовку довіряємо лише тоді, коли він узгоджений із фактичною довжиною:
    /// у файлі, який лишив по собі вбитий процес, розмір нульовий, а аудіо на місці.
    /// </summary>
    public static long FramesInFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length <= HeaderSize)
            {
                return 0;
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var header = new byte[HeaderSize];
            if (stream.Read(header, 0, HeaderSize) != HeaderSize)
            {
                return 0;
            }

            var channels = BitConverter.ToInt16(header, 22);
            var bits = BitConverter.ToInt16(header, 34);
            var bytesPerFrame = channels * (bits / 8);
            if (bytesPerFrame <= 0)
            {
                return 0;
            }

            var declared = BitConverter.ToUInt32(header, DataSizeOffset);
            var actual = info.Length - HeaderSize;
            var dataBytes = declared > 0 && declared <= actual ? declared : (ulong)actual;

            return (long)(dataBytes / (ulong)bytesPerFrame);
        }
        catch (IOException)
        {
            return 0;
        }
    }

    private void WriteHeader(int channels)
    {
        var byteRate = AudioFormat.SampleRate * _bytesPerFrame;

        _stream.Write(Encoding.ASCII.GetBytes("RIFF"));
        _stream.Write(BitConverter.GetBytes(0u));                       // розмір — на закритті
        _stream.Write(Encoding.ASCII.GetBytes("WAVE"));
        _stream.Write(Encoding.ASCII.GetBytes("fmt "));
        _stream.Write(BitConverter.GetBytes(16u));                      // розмір fmt-чанка
        _stream.Write(BitConverter.GetBytes((ushort)1));                // PCM
        _stream.Write(BitConverter.GetBytes((ushort)channels));
        _stream.Write(BitConverter.GetBytes((uint)AudioFormat.SampleRate));
        _stream.Write(BitConverter.GetBytes((uint)byteRate));
        _stream.Write(BitConverter.GetBytes((ushort)_bytesPerFrame));   // block align
        _stream.Write(BitConverter.GetBytes((ushort)AudioFormat.BitsPerSample));
        _stream.Write(Encoding.ASCII.GetBytes("data"));
        _stream.Write(BitConverter.GetBytes(0u));                       // розмір — на закритті
    }
}
