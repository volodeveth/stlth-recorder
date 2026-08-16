using System.Text;
using Stlth.Core.Storage;

namespace Stlth.Core.Tests;

public class WavWriterTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory().FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string At(string name) => Path.Combine(_dir, name);

    [Fact]
    public void Header_is_44_bytes_and_declares_the_format()
    {
        var path = At("a.wav");
        using (var _ = new WavWriter(path, AudioFormat.MicChannels)) { }

        var bytes = File.ReadAllBytes(path);

        Assert.Equal(44, bytes.Length);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(bytes, 8, 4));
        Assert.Equal("fmt ", Encoding.ASCII.GetString(bytes, 12, 4));
        Assert.Equal("data", Encoding.ASCII.GetString(bytes, 36, 4));
        Assert.Equal(1, BitConverter.ToInt16(bytes, 20));      // PCM
        Assert.Equal(1, BitConverter.ToInt16(bytes, 22));      // моно
        Assert.Equal(48000, BitConverter.ToInt32(bytes, 24));
        Assert.Equal(16, BitConverter.ToInt16(bytes, 34));
    }

    [Fact]
    public void Stereo_header_declares_two_channels_and_the_right_block_align()
    {
        var path = At("st.wav");
        using (var _ = new WavWriter(path, AudioFormat.SystemChannels)) { }

        var bytes = File.ReadAllBytes(path);

        Assert.Equal(2, BitConverter.ToInt16(bytes, 22));
        Assert.Equal(4, BitConverter.ToInt16(bytes, 32));            // block align
        Assert.Equal(48000 * 4, BitConverter.ToInt32(bytes, 28));    // byte rate
    }

    [Fact]
    public void Sizes_are_patched_on_close()
    {
        var path = At("b.wav");
        using (var writer = new WavWriter(path, AudioFormat.SystemChannels))
        {
            writer.WriteSilence(48000);                              // 1 с стерео
        }

        var bytes = File.ReadAllBytes(path);

        Assert.Equal(48000 * 4, BitConverter.ToInt32(bytes, 40));
        Assert.Equal(bytes.Length - 8, BitConverter.ToInt32(bytes, 4));
    }

    [Fact]
    public void Silence_is_actually_zeroes()
    {
        var path = At("c.wav");
        using (var writer = new WavWriter(path, AudioFormat.MicChannels))
        {
            writer.WriteSilence(100);
        }

        Assert.All(File.ReadAllBytes(path).Skip(44), b => Assert.Equal(0, b));
    }

    [Fact]
    public void Frames_written_counts_both_audio_and_silence()
    {
        using var writer = new WavWriter(At("d.wav"), AudioFormat.MicChannels);

        writer.Write(new byte[200]);                                 // 100 кадрів моно
        writer.WriteSilence(50);

        Assert.Equal(150, writer.FramesWritten);
    }

    [Fact]
    public void Long_silence_is_written_in_full()
    {
        // Десять хвилин mute — це 28 800 000 кадрів. Вони мають опинитися у файлі,
        // а не бути «оптимізовані» в нуль.
        var path = At("mute.wav");
        using (var writer = new WavWriter(path, AudioFormat.MicChannels))
        {
            writer.WriteSilence(48000 * 60);                         // 1 хвилина
        }

        Assert.Equal(48000 * 60, WavWriter.FramesInFile(path));
    }

    [Fact]
    public void Frames_in_file_reads_length_back()
    {
        var path = At("e.wav");
        using (var writer = new WavWriter(path, AudioFormat.SystemChannels))
        {
            writer.WriteSilence(4800);
        }

        Assert.Equal(4800, WavWriter.FramesInFile(path));
    }

    [Fact]
    public void Frames_in_file_falls_back_to_the_actual_length_when_the_header_lies()
    {
        // Так виглядає файл, який лишив по собі вбитий процес: аудіо на місці,
        // розмір у заголовку — нуль.
        var path = At("killed.wav");
        using (var writer = new WavWriter(path, AudioFormat.MicChannels))
        {
            writer.WriteSilence(1000);
        }

        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write))
        {
            stream.Seek(40, SeekOrigin.Begin);
            stream.Write(new byte[4]);
        }

        Assert.Equal(1000, WavWriter.FramesInFile(path));
    }

    [Fact]
    public void Frames_in_file_of_a_missing_file_is_zero()
        => Assert.Equal(0, WavWriter.FramesInFile(At("нема.wav")));
}
