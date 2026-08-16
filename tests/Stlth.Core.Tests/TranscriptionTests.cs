using Stlth.Core.Transcription;

namespace Stlth.Core.Tests;

public class ModelInstallerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory().FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void The_vad_model_is_required_not_optional()
    {
        // Whisper на вхідному вікні завжди щось декодує: дай йому тишу — отримаєш
        // правдоподібне речення, якого ніхто не казав. А зустріч здебільшого з тиші
        // й складається.
        Assert.Contains(ModelInstaller.Required, model => model.Name.Contains("silero"));
        Assert.Equal(2, ModelInstaller.Required.Count);
    }

    [Fact]
    public void The_total_size_is_stated_before_downloading()
    {
        // Це число показують людині до того, як почати качати гігабайт.
        Assert.True(ModelInstaller.TotalBytes > 1_000L * 1024 * 1024);
        Assert.True(ModelInstaller.TotalBytes < 1_200L * 1024 * 1024);
    }

    [Fact]
    public void The_full_model_is_used_not_the_distilled_one()
    {
        // Turbo вдвічі швидший, бо його декодер здистильований із 32 шарів до 4 —
        // і саме декодер тримає межі слів. На українській це чути прямо.
        Assert.Contains(ModelInstaller.Required, model => model.Name == "ggml-large-v3-q5_0.bin");
        Assert.DoesNotContain(ModelInstaller.Required, model => model.Name.Contains("turbo"));
    }

    [Fact]
    public void A_partial_file_is_never_mistaken_for_a_complete_model()
    {
        var model = ModelInstaller.Required[1];
        var path = Path.Combine(_dir, model.Name);
        File.WriteAllBytes(path, new byte[model.ApproxBytes / 2]);

        Assert.False(ModelInstaller.IsComplete(path, model));
    }

    [Fact]
    public void A_file_of_the_right_size_counts_as_installed()
    {
        var model = ModelInstaller.Required[1];
        var path = Path.Combine(_dir, model.Name);
        File.WriteAllBytes(path, new byte[model.ApproxBytes]);

        Assert.True(ModelInstaller.IsComplete(path, model));
    }

    [Fact]
    public void A_reuploaded_model_a_few_bytes_off_is_still_a_model()
    {
        // Причина, з якої точний розмір більше не є критерієм: зашита константа
        // розійшлася з реальним файлом на 405 байтів, після чого перша спроба
        // оголошувала завантаження неповним, а друга просила діапазон за межею
        // файлу і отримувала 416.
        var model = ModelInstaller.Required[0];
        var path = Path.Combine(_dir, model.Name);
        File.WriteAllBytes(path, new byte[model.ApproxBytes - 405]);

        Assert.True(ModelInstaller.IsComplete(path, model));
    }

    [Fact]
    public void The_declared_size_is_an_estimate_not_a_gate()
    {
        // Він потрібен рівно для двох речей: показати вагу до завантаження і
        // намалювати прогрес.
        Assert.All(ModelInstaller.Required, model => Assert.True(model.ApproxBytes > 0));
    }

    [Fact]
    public void A_missing_file_is_not_installed()
        => Assert.False(ModelInstaller.IsComplete(Path.Combine(_dir, "нема.bin"),
                                                  ModelInstaller.Required[0]));

    [Fact]
    public void Nothing_downloaded_means_nothing_installed()
        => Assert.False(new ModelInstaller(_dir).IsInstalled);
}

public class TranscriberTests
{
    private const string WhisperOutput = """
        [00:00:01.000 --> 00:00:04.120]   Добрий день, дякую що знайшли час.
        [00:00:04.120 --> 00:00:07.500]   Розкажіть, будь ласка, про ваш портфель.
        [00:00:09.000 --> 00:00:09.000]
        """;

    [Fact]
    public void Timestamped_lines_are_parsed()
    {
        var lines = Transcriber.Parse(WhisperOutput, "Я");

        Assert.Equal(2, lines.Count);
        Assert.Equal(TimeSpan.FromSeconds(1), lines[0].At);
        Assert.Equal("Добрий день, дякую що знайшли час.", lines[0].Text);
        Assert.Equal("Я", lines[0].Speaker);
    }

    [Fact]
    public void Empty_segments_are_dropped()
    {
        // Порожній сегмент — це тиша, яку VAD вирізав. Рядок «нічого не сказано» у
        // транскрипті гірший за його відсутність.
        Assert.DoesNotContain(Transcriber.Parse(WhisperOutput, "Я"), line => line.Text.Length == 0);
    }

    [Fact]
    public void Output_without_timestamps_yields_nothing_rather_than_garbage()
        => Assert.Empty(Transcriber.Parse("whisper: loading model...\nready\n", "Я"));

    [Fact]
    public void Both_speakers_are_merged_in_chronological_order()
    {
        var mine = Transcriber.Parse(WhisperOutput, "Я");
        var theirs = Transcriber.Parse(
            "[00:00:02.000 --> 00:00:03.000]   Добрий день!", "Співрозмовник");

        var all = mine.Concat(theirs).OrderBy(line => line.At).ToList();
        var rendered = Transcriber.Render(@"C:\sessions\abc", all);

        var mineIndex = rendered.IndexOf("дякую що знайшли час", StringComparison.Ordinal);
        var theirsIndex = rendered.IndexOf("Добрий день!", StringComparison.Ordinal);

        Assert.True(mineIndex >= 0 && theirsIndex >= 0);
        Assert.True(mineIndex < theirsIndex);   // 00:01 раніше за 00:02
    }

    [Fact]
    public void Speaker_attribution_comes_from_the_track_not_from_a_model()
    {
        var rendered = Transcriber.Render(@"C:\sessions\abc",
        [
            new Transcriber.Line(TimeSpan.FromSeconds(1), "Я", "перше"),
            new Transcriber.Line(TimeSpan.FromSeconds(2), "Співрозмовник", "друге"),
        ]);

        Assert.Contains("**Я**", rendered);
        Assert.Contains("**Співрозмовник**", rendered);
    }

    [Fact]
    public void A_speaker_speaking_twice_is_not_labelled_twice()
    {
        var rendered = Transcriber.Render(@"C:\sessions\abc",
        [
            new Transcriber.Line(TimeSpan.FromSeconds(1), "Я", "перше"),
            new Transcriber.Line(TimeSpan.FromSeconds(2), "Я", "друге"),
        ]);

        Assert.Equal(1, rendered.Split("**Я**").Length - 1);
    }

    [Fact]
    public void An_empty_transcript_says_so_plainly()
        => Assert.Contains("не розпізнано", Transcriber.Render(@"C:\sessions\abc", []));

    [Fact]
    public void A_file_whisper_could_not_read_is_not_mistaken_for_silence()
    {
        // whisper-cli виходить із кодом 0 навіть тоді, коли не прочитав аудіо, і
        // каже про це лише в stderr. Без цієї перевірки нечитабельна доріжка
        // виглядала б у транскрипті так само, як мовчазна.
        Assert.True(Transcriber.FailedToRead(
            "read_audio_data: failed to read audio data\nerror: failed to read audio file 'mic.wav'"));
    }

    [Fact]
    public void Ordinary_whisper_chatter_is_not_treated_as_a_failure()
    {
        // Порожній результат сам по собі нормальний: у сесії справді могло не бути
        // мовлення.
        Assert.False(Transcriber.FailedToRead(
            "load_backend: loaded CPU backend from ggml-cpu-alderlake.dll\n" +
            "read_audio_data: trying to decode with miniaudio"));
    }

    [Fact]
    public void Unavailable_transcription_explains_itself()
    {
        var transcriber = new Transcriber(executable: @"C:\немає\whisper-cli.exe");

        Assert.False(transcriber.IsAvailable);
        Assert.NotNull(transcriber.UnavailableReason);
    }
}
