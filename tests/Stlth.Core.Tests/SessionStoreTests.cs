using Stlth.Core.Storage;

namespace Stlth.Core.Tests;

public class SessionStoreTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory().FullName;
    private readonly SessionStore _store;

    public SessionStoreTests() => _store = new SessionStore(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static RecordingResult Result(string dir, int durationMs = 12345) => new(
        Path.Combine(dir, "mic.wav"),
        Path.Combine(dir, "system.wav"),
        durationMs,
        InputDeviceName: "Мікрофон",
        OutputDeviceName: "Динаміки",
        Mode: CaptureMode.QpcAnchored,
        DeviceChanges: [],
        SystemAudioDetected: true);

    private static SessionMeta MetaOf(string dir) => SessionJson.Load(Path.Combine(dir, "meta.json"));

    private static void WriteTrack(string dir, string name, int channels, long frames)
    {
        using var writer = new WavWriter(Path.Combine(dir, name), channels);
        writer.WriteSilence(frames);
    }

    [Fact]
    public void Begin_records_consent_before_any_audio()
    {
        var consentAt = DateTimeOffset.Now.AddSeconds(-2);

        var handle = _store.Begin(consentAt, "Мікрофон", "Динаміки");

        var meta = MetaOf(handle.Dir);
        Assert.Equal(SessionStatus.Recording, meta.Status);
        Assert.True(meta.Consent.Confirmed);
        Assert.Equal(consentAt.ToUnixTimeMilliseconds(), meta.Consent.At.ToUnixTimeMilliseconds());
    }

    [Fact]
    public void Begin_records_devices_up_front()
    {
        // Сесія, яку крах лишає перерваною, — саме та, де знати пристрої важливо.
        // Записувати їх лише на завершенні означало б не мати їх там, де вони
        // найпотрібніші.
        var handle = _store.Begin(DateTimeOffset.Now, "Мікрофон", "Динаміки");

        Assert.Equal("Мікрофон", MetaOf(handle.Dir).Devices.Input);
        Assert.Equal("Динаміки", MetaOf(handle.Dir).Devices.Output);
    }

    [Fact]
    public void Begin_creates_a_directory_named_after_the_session()
    {
        var handle = _store.Begin(DateTimeOffset.Now, "in", "out");

        Assert.True(Directory.Exists(handle.Dir));
        Assert.Equal(handle.Id.ToString(), Path.GetFileName(handle.Dir));
    }

    [Fact]
    public void Complete_writes_duration_status_and_mode()
    {
        var handle = _store.Begin(DateTimeOffset.Now, "in", "out");

        _store.Complete(handle, Result(handle.Dir));

        var meta = MetaOf(handle.Dir);
        Assert.Equal(SessionStatus.Completed, meta.Status);
        Assert.Equal(12345, meta.DurationMs);
        Assert.Equal(CaptureMode.QpcAnchored, meta.CaptureMode);
        Assert.Equal(2, meta.Tracks.Count);
    }

    [Fact]
    public void Interrupt_marks_the_session_honestly()
    {
        var handle = _store.Begin(DateTimeOffset.Now, "in", "out");

        _store.Interrupt(handle);

        Assert.Equal(SessionStatus.Interrupted, MetaOf(handle.Dir).Status);
    }

    [Fact]
    public void Recovery_marks_a_killed_session_interrupted_with_the_real_duration()
    {
        var handle = _store.Begin(DateTimeOffset.Now, "in", "out");
        WriteTrack(handle.Dir, "mic.wav", 1, 48000);
        WriteTrack(handle.Dir, "system.wav", 2, 48000);

        var recovered = _store.RecoverInterrupted();

        Assert.Single(recovered);
        var meta = MetaOf(handle.Dir);
        Assert.Equal(SessionStatus.Interrupted, meta.Status);
        Assert.Equal(1000, meta.DurationMs);
    }

    [Fact]
    public void Recovery_takes_the_shorter_track_as_the_duration()
    {
        // Далі цієї точки обох каналів уже немає, а сесія без одного каналу —
        // не сесія.
        var handle = _store.Begin(DateTimeOffset.Now, "in", "out");
        WriteTrack(handle.Dir, "mic.wav", 1, 96000);
        WriteTrack(handle.Dir, "system.wav", 2, 48000);

        _store.RecoverInterrupted();

        Assert.Equal(1000, MetaOf(handle.Dir).DurationMs);
    }

    [Fact]
    public void Recovery_repairs_the_headers_of_the_tracks()
    {
        var handle = _store.Begin(DateTimeOffset.Now, "in", "out");
        WriteTrack(handle.Dir, "mic.wav", 1, 4800);
        WriteTrack(handle.Dir, "system.wav", 2, 4800);

        // Обнулити розміри так, як це робить убитий процес.
        foreach (var name in new[] { "mic.wav", "system.wav" })
        {
            using var stream = new FileStream(Path.Combine(handle.Dir, name), FileMode.Open, FileAccess.Write);
            stream.Seek(40, SeekOrigin.Begin);
            stream.Write(new byte[4]);
        }

        _store.RecoverInterrupted();

        var bytes = File.ReadAllBytes(Path.Combine(handle.Dir, "mic.wav"));
        Assert.Equal(4800 * 2, BitConverter.ToInt32(bytes, 40));
    }

    [Fact]
    public void Completed_sessions_are_left_alone_by_recovery()
    {
        var handle = _store.Begin(DateTimeOffset.Now, "in", "out");
        WriteTrack(handle.Dir, "mic.wav", 1, 4800);
        WriteTrack(handle.Dir, "system.wav", 2, 4800);
        _store.Complete(handle, Result(handle.Dir));

        Assert.Empty(_store.RecoverInterrupted());
        Assert.Equal(SessionStatus.Completed, MetaOf(handle.Dir).Status);
    }

    [Fact]
    public void Recovery_is_idempotent()
    {
        var handle = _store.Begin(DateTimeOffset.Now, "in", "out");
        WriteTrack(handle.Dir, "mic.wav", 1, 4800);
        WriteTrack(handle.Dir, "system.wav", 2, 4800);

        _store.RecoverInterrupted();

        // Другий запуск застосунку не має «відновлювати» вже відновлене.
        Assert.Empty(_store.RecoverInterrupted());
    }

    [Fact]
    public void One_broken_session_does_not_hide_the_others()
    {
        var good = _store.Begin(DateTimeOffset.Now, "in", "out");
        _store.Complete(good, Result(good.Dir));
        var broken = Path.Combine(_root, Guid.NewGuid().ToString());
        Directory.CreateDirectory(broken);
        File.WriteAllText(Path.Combine(broken, "meta.json"), "{ це не json");

        Assert.Single(_store.List());
    }

    [Fact]
    public void Sessions_are_listed_newest_first()
    {
        var older = _store.Begin(DateTimeOffset.Now.AddHours(-1), "in", "out");
        var newer = _store.Begin(DateTimeOffset.Now, "in", "out");
        _store.Complete(older, Result(older.Dir));
        _store.Complete(newer, Result(newer.Dir));

        Assert.Equal(newer.Id, _store.List()[0].SessionId);
    }

    [Fact]
    public void Listing_a_root_that_does_not_exist_yet_is_empty_not_an_error()
    {
        var store = new SessionStore(Path.Combine(_root, "ще-немає"));

        Assert.Empty(store.List());
    }

    [Fact]
    public void Device_changes_accumulate_in_order()
    {
        var handle = _store.Begin(DateTimeOffset.Now, "in", "out");

        _store.AppendDeviceChange(handle, DateTimeOffset.Now, "Навушники", null);
        _store.AppendDeviceChange(handle, DateTimeOffset.Now, "Мікрофон", null);

        var changes = MetaOf(handle.Dir).DeviceChanges;
        Assert.Equal(2, changes.Count);
        Assert.Equal("Навушники", changes[0].Input);
        Assert.Equal("Мікрофон", changes[1].Input);
    }

    [Fact]
    public void Delete_removes_the_whole_directory()
    {
        var handle = _store.Begin(DateTimeOffset.Now, "in", "out");

        _store.Delete(handle.Id);

        Assert.False(Directory.Exists(handle.Dir));
    }

    [Fact]
    public void Deleting_a_session_that_is_gone_is_not_an_error()
        => _store.Delete(Guid.NewGuid());

    [Fact]
    public void Note_mix_records_the_derived_file()
    {
        var handle = _store.Begin(DateTimeOffset.Now, "in", "out");
        _store.Complete(handle, Result(handle.Dir));

        _store.NoteMix(handle.Dir, "session.m4a");

        Assert.Equal("session.m4a", MetaOf(handle.Dir).MixFile);
    }
}
