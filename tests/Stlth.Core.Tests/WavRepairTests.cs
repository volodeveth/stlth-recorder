using Stlth.Core.Storage;

namespace Stlth.Core.Tests;

public class WavRepairTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory().FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>
    /// Файл рівно такий, яким його лишає вбитий процес: аудіо на місці, поля
    /// розміру в заголовку — нулі, бо їх дописують лише при коректному закритті.
    /// </summary>
    private string Killed(long frames, int channels)
    {
        var path = Path.Combine(_dir, Guid.NewGuid() + ".wav");
        using (var writer = new WavWriter(path, channels))
        {
            writer.WriteSilence(frames);
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Write);
        stream.Seek(4, SeekOrigin.Begin);
        stream.Write(new byte[4]);
        stream.Seek(40, SeekOrigin.Begin);
        stream.Write(new byte[4]);
        return path;
    }

    [Fact]
    public void Patches_sizes_from_the_actual_file_length()
    {
        var path = Killed(48000, 2);

        Assert.True(WavRepair.RepairIfNeeded(path));

        var bytes = File.ReadAllBytes(path);
        Assert.Equal(48000 * 4, BitConverter.ToInt32(bytes, 40));
        Assert.Equal(bytes.Length - 8, BitConverter.ToInt32(bytes, 4));
    }

    [Fact]
    public void Audio_bytes_are_never_touched()
    {
        var path = Killed(1000, 1);
        var before = File.ReadAllBytes(path).Skip(44).ToArray();

        WavRepair.RepairIfNeeded(path);

        Assert.Equal(before, File.ReadAllBytes(path).Skip(44).ToArray());
    }

    [Fact]
    public void Is_idempotent()
    {
        var path = Killed(1000, 1);

        Assert.True(WavRepair.RepairIfNeeded(path));
        Assert.False(WavRepair.RepairIfNeeded(path));
    }

    [Fact]
    public void Heals_a_file_left_by_an_older_version_too()
    {
        // Ідемпотентність потрібна саме для цього: відновлення прогоняється по всіх
        // сесіях, і вже полікована не має ламатися вдруге.
        var path = Killed(500, 1);
        WavRepair.RepairIfNeeded(path);

        Assert.Equal(500, WavWriter.FramesInFile(path));
    }

    [Fact]
    public void Healthy_file_is_left_alone()
    {
        var path = Path.Combine(_dir, "ok.wav");
        using (var writer = new WavWriter(path, 1))
        {
            writer.WriteSilence(500);
        }

        Assert.False(WavRepair.RepairIfNeeded(path));
    }

    [Fact]
    public void Header_only_file_is_not_mistaken_for_damage()
    {
        // Сесія, вбита до першого пакета: даних немає, і це не поломка.
        var path = Path.Combine(_dir, "empty.wav");
        using (var _ = new WavWriter(path, 1)) { }

        Assert.False(WavRepair.RepairIfNeeded(path));
    }

    [Fact]
    public void Missing_file_returns_false_instead_of_throwing()
        => Assert.False(WavRepair.RepairIfNeeded(Path.Combine(_dir, "нема.wav")));

    [Fact]
    public void A_file_that_is_not_a_wav_is_left_alone()
    {
        var path = Path.Combine(_dir, "notes.txt");
        File.WriteAllText(path, new string('x', 200));

        Assert.False(WavRepair.RepairIfNeeded(path));
    }
}
