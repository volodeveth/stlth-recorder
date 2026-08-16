using Stlth.Core.Storage;

namespace Stlth.Core.Tests;

/// <summary>
/// Єдине місце в продукті, де він видаляє записи людини. Тому правило перевіряється
/// з обох боків: і що воно спрацьовує, і — головне — коли воно не має спрацювати.
/// </summary>
public class AudioRemovalTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory().FullName;
    private readonly SessionStore _store;

    public AudioRemovalTests() => _store = new SessionStore(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private SessionHandle SessionWithAudio(long frames = 48000)
    {
        var handle = _store.Begin(DateTimeOffset.Now, "in", "out");

        using (var mic = new WavWriter(Path.Combine(handle.Dir, Track.Mic.File), AudioFormat.MicChannels))
        {
            mic.WriteSilence(frames);
        }

        using (var system = new WavWriter(Path.Combine(handle.Dir, Track.System.File), AudioFormat.SystemChannels))
        {
            system.WriteSilence(frames);
        }

        return handle;
    }

    [Fact]
    public void Nothing_is_deleted_while_the_option_is_off()
        => Assert.False(SessionStore.MayRemoveAudio(enabled: false, transcriptHasSpeech: true));

    [Fact]
    public void Nothing_is_deleted_when_the_transcript_found_no_speech()
    {
        // Найгірший можливий результат: запис зник, а натомість лишився файл із
        // написом «мовлення не розпізнано». Порожній транскрипт означає, що
        // розпізнати не вдалося — це привід зберегти аудіо, а не позбутися його.
        Assert.False(SessionStore.MayRemoveAudio(enabled: true, transcriptHasSpeech: false));
    }

    [Fact]
    public void Deletion_needs_both_the_option_and_actual_speech()
        => Assert.True(SessionStore.MayRemoveAudio(enabled: true, transcriptHasSpeech: true));

    [Fact]
    public void Removing_audio_frees_the_tracks_and_keeps_everything_else()
    {
        var handle = SessionWithAudio();
        File.WriteAllText(Path.Combine(handle.Dir, "transcript.md"), "# текст");
        File.WriteAllText(Path.Combine(handle.Dir, "session.m4a"), "звук");

        var freed = _store.RemoveAudio(handle.Dir);

        Assert.True(freed > 0);
        Assert.False(File.Exists(Path.Combine(handle.Dir, Track.Mic.File)));
        Assert.False(File.Exists(Path.Combine(handle.Dir, Track.System.File)));
        Assert.True(File.Exists(Path.Combine(handle.Dir, "transcript.md")));
        Assert.True(File.Exists(Path.Combine(handle.Dir, "session.m4a")));
        Assert.True(File.Exists(Path.Combine(handle.Dir, "meta.json")));
    }

    [Fact]
    public void The_removal_is_written_into_the_metadata()
    {
        // Різниця між «файли прибрали навмисно» і «файли зникли» має бути записана,
        // а не відновлюватися здогадками через півроку.
        var handle = SessionWithAudio();

        _store.RemoveAudio(handle.Dir);

        var meta = SessionJson.Load(Path.Combine(handle.Dir, "meta.json"));
        Assert.NotNull(meta.AudioRemovedAt);
    }

    [Fact]
    public void A_session_without_audio_still_lists_and_reads()
    {
        var handle = SessionWithAudio();
        _store.RemoveAudio(handle.Dir);

        var listed = _store.List();

        Assert.Single(listed);
        Assert.Equal(handle.Id, listed[0].SessionId);
    }

    [Fact]
    public void Removing_twice_frees_nothing_the_second_time()
    {
        var handle = SessionWithAudio();

        Assert.True(_store.RemoveAudio(handle.Dir) > 0);
        Assert.Equal(0, _store.RemoveAudio(handle.Dir));
    }

    [Fact]
    public void A_session_that_never_had_audio_is_not_marked_as_stripped()
    {
        // Порожня тека — це не «аудіо видалили», і позначати її так було б брехнею.
        var handle = _store.Begin(DateTimeOffset.Now, "in", "out");

        _store.RemoveAudio(handle.Dir);

        Assert.Null(SessionJson.Load(Path.Combine(handle.Dir, "meta.json")).AudioRemovedAt);
    }

    [Fact]
    public void Sessions_without_the_field_still_decode()
    {
        // Записи, зроблені до появи цієї опції, читаються без жодних змін.
        var handle = SessionWithAudio();
        var meta = SessionJson.Load(Path.Combine(handle.Dir, "meta.json"));

        Assert.Null(meta.AudioRemovedAt);
    }
}
