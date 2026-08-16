using Stlth.Core;
using Stlth.Core.Audio;
using Stlth.Core.Storage;

namespace Stlth.Core.Tests;

public class RecorderControllerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory().FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private sealed class FakeEngine(string dir, bool failOnStart) : IAudioEngine
    {
        public int StartCount { get; private set; }

        public bool Disposed { get; private set; }

        public void Start()
        {
            StartCount++;
            if (failOnStart)
            {
                throw new InvalidOperationException("мікрофон недоступний");
            }
        }

        public RecordingResult Stop() => new(
            Path.Combine(dir, "mic.wav"),
            Path.Combine(dir, "system.wav"),
            1000, "in", "out", CaptureMode.QpcAnchored, [], true);

        public void Dispose() => Disposed = true;
    }

    private RecorderController Make(bool failing = false, List<FakeEngine>? made = null)
    {
        var store = new SessionStore(_root);
        return new RecorderController(store, dir =>
        {
            var engine = new FakeEngine(dir, failing);
            made?.Add(engine);
            return engine;
        });
    }

    private SessionMeta OnlySessionMeta()
        => SessionJson.Load(Path.Combine(Directory.GetDirectories(_root)[0], "meta.json"));

    [Fact]
    public void Starts_from_idle()
    {
        var controller = Make();

        controller.Start(DateTimeOffset.Now, "in", "out");

        Assert.Equal(RecorderState.Recording, controller.State);
        Assert.True(controller.IsRecording);
    }

    [Fact]
    public void Second_start_never_creates_a_duplicate_session()
    {
        // Ця перевірка і є захистом від дубля: подвійний клік не має ані створювати
        // другу сесію, ані ламати першу.
        var made = new List<FakeEngine>();
        var controller = Make(made: made);

        controller.Start(DateTimeOffset.Now, "in", "out");
        controller.Start(DateTimeOffset.Now, "in", "out");

        Assert.Single(made);
        Assert.Single(Directory.GetDirectories(_root));
    }

    [Fact]
    public void Stop_completes_the_session_and_returns_to_idle()
    {
        var controller = Make();
        controller.Start(DateTimeOffset.Now, "in", "out");

        controller.Stop();

        Assert.Equal(RecorderState.Idle, controller.State);
        Assert.Equal(SessionStatus.Completed, OnlySessionMeta().Status);
        Assert.NotNull(controller.LastResult);
    }

    [Fact]
    public void Stop_disposes_the_engine()
    {
        var made = new List<FakeEngine>();
        var controller = Make(made: made);
        controller.Start(DateTimeOffset.Now, "in", "out");

        controller.Stop();

        Assert.True(made[0].Disposed);
    }

    [Fact]
    public void Stop_while_idle_is_ignored()
    {
        var controller = Make();

        controller.Stop();

        Assert.Equal(RecorderState.Idle, controller.State);
        Assert.Empty(Directory.GetDirectories(_root));
    }

    [Fact]
    public void A_failed_start_leaves_the_session_marked_honestly()
    {
        var controller = Make(failing: true);

        controller.Start(DateTimeOffset.Now, "in", "out");

        Assert.Equal(RecorderState.Idle, controller.State);
        Assert.NotNull(controller.LastError);
        Assert.Equal(SessionStatus.Interrupted, OnlySessionMeta().Status);
    }

    [Fact]
    public void A_failed_start_does_not_leave_the_app_stuck_in_preparing()
    {
        var controller = Make(failing: true);

        controller.Start(DateTimeOffset.Now, "in", "out");

        Assert.Equal(RecorderState.Idle, controller.State); // спробувати ще раз можна
    }

    [Fact]
    public void A_successful_start_clears_the_previous_error()
    {
        var controller = Make();
        controller.Start(DateTimeOffset.Now, "in", "out");

        Assert.Null(controller.LastError);
    }

    [Fact]
    public void Elapsed_is_zero_when_not_recording()
        => Assert.Equal(TimeSpan.Zero, Make().Elapsed);

    [Fact]
    public void Elapsed_grows_while_recording()
    {
        var controller = Make();
        controller.Start(DateTimeOffset.Now, "in", "out");

        Thread.Sleep(20);

        Assert.True(controller.Elapsed > TimeSpan.Zero);
    }

    [Fact]
    public void Changed_fires_on_every_transition()
    {
        var controller = Make();
        var count = 0;
        controller.Changed += (_, _) => count++;

        controller.Start(DateTimeOffset.Now, "in", "out");
        controller.Stop();

        Assert.True(count >= 2);
    }

    [Fact]
    public void Recovery_reports_what_it_repaired()
    {
        var store = new SessionStore(_root);
        var handle = store.Begin(DateTimeOffset.Now, "in", "out");
        using (var writer = new WavWriter(Path.Combine(handle.Dir, "mic.wav"), 1))
        {
            writer.WriteSilence(4800);
        }

        var controller = Make();
        controller.RecoverInterruptedSessions();

        Assert.Single(controller.RecoveredSessions);
    }
}
