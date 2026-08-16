using NAudio.Wave;
using Stlth.Core.Audio;

namespace Stlth.Core.Tests;

/// <summary>
/// Резервний шлях, який виконується лише тоді, коли драйвер відмовився віддавати
/// потрібний формат. Саме тому він і потребує тестів: на стенді його не відтворити,
/// а тихо записане у чужому форматі аудіо звучить як прискорений шум і виглядає при
/// цьому як нормальний файл.
/// </summary>
public class PcmConverterTests
{
    private static WaveFormat Float(int channels) => WaveFormat.CreateIeeeFloatWaveFormat(48000, channels);

    private static WaveFormat Pcm16(int channels) => new(48000, 16, channels);

    private static byte[] FloatBytes(params float[] values)
    {
        var bytes = new byte[values.Length * 4];
        for (var i = 0; i < values.Length; i++)
        {
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 4), values[i]);
        }

        return bytes;
    }

    private static short[] Shorts(byte[] bytes)
    {
        var values = new short[bytes.Length / 2];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = BitConverter.ToInt16(bytes, i * 2);
        }

        return values;
    }

    [Fact]
    public void A_matching_format_needs_no_conversion()
    {
        Assert.True(PcmConverter.IsPassthrough(Pcm16(1), 1));
        Assert.True(PcmConverter.IsPassthrough(Pcm16(2), 2));
    }

    [Fact]
    public void A_different_channel_count_is_not_passthrough()
        => Assert.False(PcmConverter.IsPassthrough(Pcm16(2), 1));

    [Fact]
    public void Float_input_is_not_passthrough()
        => Assert.False(PcmConverter.IsPassthrough(Float(2), 2));

    [Fact]
    public void Float_is_converted_to_sixteen_bit()
    {
        var input = FloatBytes(0f, 0.5f, -0.5f, 1f);

        var output = Shorts(PcmConverter.Convert(input, frames: 4, Float(1), targetChannels: 1));

        Assert.Equal(0, output[0]);
        Assert.Equal(16383, output[1], tolerance: 2);
        Assert.Equal(-16383, output[2], tolerance: 2);
        Assert.Equal(short.MaxValue, output[3]);
    }

    [Fact]
    public void Full_scale_input_does_not_wrap_around()
    {
        // Кліпінг мусить впертися у стелю, а не перевернутися в найтихіший семпл —
        // саме так виглядає найгірший різновид «тихої» помилки в аудіо.
        var output = Shorts(PcmConverter.Convert(FloatBytes(1.5f, -1.5f), 2, Float(1), 1));

        Assert.Equal(short.MaxValue, output[0]);
        Assert.Equal(short.MinValue, output[1]);
    }

    [Fact]
    public void A_multichannel_microphone_array_is_folded_to_mono()
    {
        // Реальний випадок цього стенда: мікрофон-масив віддає чотири канали
        // 32-бітного float, а доріжка мусить бути моно.
        var input = FloatBytes(1f, 0f, 1f, 0f);

        var output = Shorts(PcmConverter.Convert(input, frames: 1, Float(4), targetChannels: 1));

        Assert.Single(output);
        Assert.Equal(short.MaxValue / 2, output[0], tolerance: 2);
    }

    [Fact]
    public void Mono_is_duplicated_across_a_stereo_track()
    {
        var output = Shorts(PcmConverter.Convert(FloatBytes(0.5f, -0.25f), 2, Float(1), targetChannels: 2));

        Assert.Equal(4, output.Length);
        Assert.Equal(output[0], output[1]);
        Assert.Equal(output[2], output[3]);
    }

    [Fact]
    public void Sixteen_bit_stereo_is_folded_to_mono_by_averaging()
    {
        var input = new byte[4];
        BitConverter.TryWriteBytes(input.AsSpan(0), (short)10000);
        BitConverter.TryWriteBytes(input.AsSpan(2), (short)0);

        var output = Shorts(PcmConverter.Convert(input, frames: 1, Pcm16(2), targetChannels: 1));

        Assert.Equal(5000, output[0], tolerance: 2);
    }

    [Fact]
    public void The_output_is_exactly_as_long_as_the_frame_count_promises()
    {
        // Довжина — це те, на що спирається інваріант таймлайна: кадр, загублений
        // у конверторі, зсунув би всю доріжку.
        var output = PcmConverter.Convert(FloatBytes(new float[100]), frames: 50, Float(2), targetChannels: 2);

        Assert.Equal(50 * AudioFormat.BytesPerFrame(2), output.Length);
    }

    [Fact]
    public void A_truncated_buffer_yields_silence_rather_than_reading_past_the_end()
    {
        var output = Shorts(PcmConverter.Convert(FloatBytes(1f), frames: 4, Float(1), targetChannels: 1));

        Assert.Equal(4, output.Length);
        Assert.Equal(short.MaxValue, output[0]);
        Assert.Equal(0, output[3]);
    }
}
