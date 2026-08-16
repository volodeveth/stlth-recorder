using NAudio.Wave;

namespace Stlth.Core.Audio;

/// <summary>
/// Зводить те, що справді віддав пристрій, до формату доріжки: 48 кГц, 16 біт,
/// потрібна кількість каналів.
///
/// У більшості випадків нічого робити не доводиться — конвертацію бере на себе
/// системний мікшер, якщо потік ініціалізовано з <c>AutoConvertPcm</c>. Але не кожен
/// драйвер це приймає, і тоді єдина чесна альтернатива — узяти рідний формат пристрою
/// і перетворити самому. Тихо писати аудіо в чужому форматі не можна: файл виглядав би
/// нормальним і звучав би як прискорений шум.
///
/// Частота тут не змінюється: обидва потоки ініціалізуються на 48 кГц, і якщо
/// пристрій цього не приймає навіть із конвертацією, потік не піднімається з
/// явною помилкою, а не з тихою деградацією.
/// </summary>
internal static class PcmConverter
{
    /// <summary>Чи потрібне перетворення взагалі.</summary>
    public static bool IsPassthrough(WaveFormat source, int targetChannels) =>
        source.Encoding == WaveFormatEncoding.Pcm &&
        source.BitsPerSample == AudioFormat.BitsPerSample &&
        source.Channels == targetChannels &&
        source.SampleRate == AudioFormat.SampleRate;

    /// <summary>
    /// Перетворити <paramref name="frames"/> кадрів із <paramref name="source"/> у
    /// 16-бітний PCM на <paramref name="targetChannels"/> каналів.
    /// </summary>
    public static byte[] Convert(ReadOnlySpan<byte> input, int frames, WaveFormat source, int targetChannels)
    {
        var output = new byte[frames * AudioFormat.BytesPerFrame(targetChannels)];
        var sourceChannels = source.Channels;
        var span = output.AsSpan();

        for (var frame = 0; frame < frames; frame++)
        {
            // Один кадр згортається в моно-значення, а далі роздається на цільові
            // канали. Для мікрофона це «звести стерео в моно», для системного —
            // «роздати моно на два», і обидва випадки — та сама арифметика.
            float mixed = 0;
            for (var channel = 0; channel < sourceChannels; channel++)
            {
                mixed += ReadSample(input, source, (frame * sourceChannels) + channel);
            }

            mixed /= sourceChannels;

            var value = (short)Math.Clamp(mixed * short.MaxValue, short.MinValue, short.MaxValue);
            for (var channel = 0; channel < targetChannels; channel++)
            {
                var offset = ((frame * targetChannels) + channel) * 2;
                BitConverter.TryWriteBytes(span[offset..], value);
            }
        }

        return output;
    }

    /// <summary>Одне значення в діапазоні [-1, 1], незалежно від того, як воно лежить.</summary>
    private static float ReadSample(ReadOnlySpan<byte> input, WaveFormat format, int index)
    {
        var bytesPerSample = format.BitsPerSample / 8;
        var offset = index * bytesPerSample;
        if (offset + bytesPerSample > input.Length)
        {
            return 0;
        }

        return format.Encoding switch
        {
            WaveFormatEncoding.IeeeFloat when bytesPerSample == 4 => BitConverter.ToSingle(input[offset..]),
            WaveFormatEncoding.Pcm when bytesPerSample == 2 => BitConverter.ToInt16(input[offset..]) / 32768f,
            WaveFormatEncoding.Pcm when bytesPerSample == 4 => BitConverter.ToInt32(input[offset..]) / 2147483648f,
            WaveFormatEncoding.Pcm when bytesPerSample == 3 =>
                ((input[offset + 2] << 16) | (input[offset + 1] << 8) | input[offset]) switch
                {
                    var raw => (raw >= 0x800000 ? raw - 0x1000000 : raw) / 8388608f,
                },
            WaveFormatEncoding.Extensible when bytesPerSample == 4 => BitConverter.ToSingle(input[offset..]),
            _ => 0,
        };
    }
}
