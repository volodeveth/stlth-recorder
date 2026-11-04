using System.Diagnostics;
using Stlth.Core.Transcription;

namespace Stlth.Core.Tests;

/// <summary>
/// Розпізнавання не має забирати ноутбук у людини, яка щойно поклала слухавку,
/// але й не має сидіти на чотирьох потоках із вісімнадцяти.
/// </summary>
public class TranscriptionLoadTests
{
    [Theory]
    [InlineData(18, 4)]
    [InlineData(16, 4)]
    [InlineData(12, 3)]
    [InlineData(8, 2)]
    [InlineData(4, 1)]
    [InlineData(2, 1)]
    [InlineData(1, 1)]
    public void Each_track_gets_a_quarter_of_the_logical_processors(int logical, int expected)
        => Assert.Equal(expected, TranscriptionLoad.ThreadsPerTrack(logical));

    [Theory]
    [InlineData(18)]
    [InlineData(16)]
    [InlineData(8)]
    [InlineData(4)]
    public void Both_tracks_together_never_take_more_than_half_the_machine(int logical)
        => Assert.True(2 * TranscriptionLoad.ThreadsPerTrack(logical) <= logical / 2);

    [Fact]
    public void Whisper_yields_to_whatever_the_person_is_doing()
        => Assert.Equal(ProcessPriorityClass.BelowNormal, TranscriptionLoad.Priority);

    [Fact]
    public void The_thread_count_is_passed_to_whisper_explicitly()
    {
        // Без `-t` whisper-cli бере типові 4 потоки незалежно від машини.
        var arguments = Transcriber.BuildArguments(
            model: @"C:\m\large.bin", audio: @"C:\s\mic.wav", language: "uk",
            vadModel: @"C:\m\vad.bin", threads: 4);

        var at = arguments.IndexOf("-t");
        Assert.True(at >= 0, "немає -t");
        Assert.Equal("4", arguments[at + 1]);
    }

    [Fact]
    public void The_recognition_arguments_stay_as_they_were()
    {
        var arguments = Transcriber.BuildArguments(
            model: @"C:\m\large.bin", audio: @"C:\s\mic.wav", language: "uk",
            vadModel: @"C:\m\vad.bin", threads: 4);

        Assert.Equal(@"C:\m\large.bin", arguments[arguments.IndexOf("-m") + 1]);
        Assert.Equal(@"C:\s\mic.wav", arguments[arguments.IndexOf("-f") + 1]);
        Assert.Equal("uk", arguments[arguments.IndexOf("-l") + 1]);
        Assert.Equal(@"C:\m\vad.bin", arguments[arguments.IndexOf("--vad-model") + 1]);
        Assert.Equal("0.5", arguments[arguments.IndexOf("--vad-threshold") + 1]);
        Assert.Contains("--vad", arguments);
        Assert.Contains("--no-prints", arguments);
        Assert.Contains("--suppress-nst", arguments);
    }
}
