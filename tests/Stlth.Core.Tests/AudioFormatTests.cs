namespace Stlth.Core.Tests;

public class AudioFormatTests
{
    [Fact]
    public void Mono_frame_is_two_bytes()
        => Assert.Equal(2, AudioFormat.BytesPerFrame(AudioFormat.MicChannels));

    [Fact]
    public void Stereo_frame_is_four_bytes()
        => Assert.Equal(4, AudioFormat.BytesPerFrame(AudioFormat.SystemChannels));

    [Fact]
    public void Sample_rate_is_48k()
        => Assert.Equal(48000, AudioFormat.SampleRate);

    [Fact]
    public void One_second_of_both_tracks_is_six_bytes_per_frame_position()
        => Assert.Equal(6, AudioFormat.BytesPerFrame(AudioFormat.MicChannels)
                          + AudioFormat.BytesPerFrame(AudioFormat.SystemChannels));
}
