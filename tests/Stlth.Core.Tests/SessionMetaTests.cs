using Stlth.Core.Storage;

namespace Stlth.Core.Tests;

public class SessionMetaTests
{
    private static SessionMeta Sample() => new()
    {
        SessionId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        StartedAt = new DateTimeOffset(2026, 8, 16, 14, 0, 3, 412, TimeSpan.FromHours(3)),
        DurationMs = 3127000,
        Status = SessionStatus.Completed,
        Consent = new Consent(true, new DateTimeOffset(2026, 8, 16, 14, 0, 1, 907, TimeSpan.FromHours(3))),
        Tracks = [Track.Mic, Track.System],
        Devices = new Devices("Мікрофон (Realtek)", "Динаміки (Realtek)"),
        DeviceChanges = [],
        CaptureMode = CaptureMode.QpcAnchored,
        AppVersion = "1.0.0",
        OsVersion = "10.0.26200",
    };

    [Fact]
    public void Round_trips_without_loss()
    {
        var restored = SessionJson.Deserialize(SessionJson.Serialize(Sample()));

        Assert.Equal(Sample().SessionId, restored.SessionId);
        Assert.Equal(Sample().StartedAt, restored.StartedAt);
        Assert.Equal(SessionStatus.Completed, restored.Status);
        Assert.Equal(CaptureMode.QpcAnchored, restored.CaptureMode);
        Assert.Equal(3127000, restored.DurationMs);
        Assert.True(restored.Consent.Confirmed);
    }

    [Fact]
    public void Dates_carry_offset_and_fractional_seconds()
    {
        // Без дробових секунд дві сесії, стартовані в одну секунду, сортуються
        // недетерміновано, а відновлена тривалість втрачає до секунди.
        Assert.Contains("\"2026-08-16T14:00:03.412+03:00\"", SessionJson.Serialize(Sample()));
    }

    [Fact]
    public void Enums_serialise_as_readable_strings()
    {
        var json = SessionJson.Serialize(Sample());
        Assert.Contains("\"status\": \"completed\"", json);
        Assert.Contains("\"captureMode\": \"qpc-anchored\"", json);
    }

    [Fact]
    public void Speakers_are_neutral()
    {
        Assert.Equal("me", Track.Mic.Speaker);
        Assert.Equal("peer", Track.System.Speaker);
        Assert.Equal("mic.wav", Track.Mic.File);
        Assert.Equal("system.wav", Track.System.File);
        Assert.Equal(1, Track.Mic.Channels);
        Assert.Equal(2, Track.System.Channels);
        Assert.Equal(48000, Track.Mic.SampleRate);
    }

    [Fact]
    public void Device_names_stay_readable_rather_than_escaped()
    {
        // Кирилиця в назві пристрою мусить лишатися кирилицею: meta.json читають
        // очима не рідше, ніж кодом.
        Assert.Contains("Мікрофон (Realtek)", SessionJson.Serialize(Sample()));
    }

    [Fact]
    public void Missing_optional_fields_still_decode()
    {
        // Сесії, записані до появи зведення, мусять читатися без mixFile і captureMode.
        var json = SessionJson.Serialize(Sample())
            .Replace("\"captureMode\": \"qpc-anchored\",", string.Empty);

        var restored = SessionJson.Deserialize(json);

        Assert.Null(restored.CaptureMode);
        Assert.Null(restored.MixFile);
    }

    [Fact]
    public void Unknown_fields_do_not_break_decoding()
    {
        var json = SessionJson.Serialize(Sample())
            .Replace("\"durationMs\"", "\"somethingNewer\": 1,\n  \"durationMs\"");

        Assert.Equal(3127000, SessionJson.Deserialize(json).DurationMs);
    }

    [Fact]
    public void Write_is_atomic_and_readable()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "meta.json");

        SessionJson.WriteAtomic(Sample(), path);

        Assert.Equal(Sample().SessionId, SessionJson.Load(path).SessionId);
        Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
        Directory.Delete(dir, true);
    }

    [Fact]
    public void Overwriting_an_existing_file_keeps_it_valid()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "meta.json");

        SessionJson.WriteAtomic(Sample(), path);
        var updated = Sample();
        updated.Status = SessionStatus.Interrupted;
        SessionJson.WriteAtomic(updated, path);

        Assert.Equal(SessionStatus.Interrupted, SessionJson.Load(path).Status);
        Directory.Delete(dir, true);
    }

    [Fact]
    public void Device_changes_survive_the_round_trip()
    {
        var meta = Sample();
        meta.DeviceChanges =
        [
            new DeviceChange(new DateTimeOffset(2026, 8, 16, 14, 20, 11, 3, TimeSpan.FromHours(3)),
                             "Навушники", null),
        ];

        var restored = SessionJson.Deserialize(SessionJson.Serialize(meta));

        Assert.Single(restored.DeviceChanges);
        Assert.Equal("Навушники", restored.DeviceChanges[0].Input);
        Assert.Null(restored.DeviceChanges[0].Output);
    }
}
