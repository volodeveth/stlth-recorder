using Stlth.Core.Audio;
using Stlth.Core.Storage;

namespace Stlth.Core;

public enum RecorderState
{
    Idle,
    Preparing,
    Recording,
    Stopping,
}

/// <summary>
/// Єдина state machine застосунку: <c>Idle → Preparing → Recording → Stopping → Idle</c>.
///
/// Переходи охоронювані, і саме це виконує вимогу, щоб повторне натискання «Почати
/// запис» ніколи не створювало другої сесії і не ламало першу.
///
/// UI лише читає стан і слухає <see cref="Changed"/> — жодної власної логіки про те,
/// коли можна писати, у ньому немає.
/// </summary>
public sealed class RecorderController
{
    private readonly SessionStore _store;
    private readonly Func<string, IAudioEngine> _engineFactory;

    private IAudioEngine? _engine;
    private SessionHandle? _handle;

    public RecorderController(SessionStore store, Func<string, IAudioEngine> engineFactory)
    {
        _store = store;
        _engineFactory = engineFactory;
    }

    public SessionStore Store => _store;

    public RecorderState State { get; private set; } = RecorderState.Idle;

    public DateTimeOffset? StartedAt { get; private set; }

    /// <summary>Остання помилка, українською, готова до показу в меню.</summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// Підсумок сесії, що щойно завершилася — з нього UI дізнається, чи взагалі
    /// прийшов системний звук.
    /// </summary>
    public RecordingResult? LastResult { get; private set; }

    /// <summary>Тека сесії, що щойно завершилася — з неї будується зведення.</summary>
    public string? LastSessionDir { get; private set; }

    /// <summary>Теки, полікувані при старті, — щоб було з чого перебудувати похідне.</summary>
    public IReadOnlyList<string> RecoveredSessions { get; private set; } = [];

    public event EventHandler? Changed;

    public bool IsRecording => State == RecorderState.Recording;

    public TimeSpan Elapsed =>
        State == RecorderState.Recording && StartedAt is { } started
            ? DateTimeOffset.Now - started
            : TimeSpan.Zero;

    /// <summary>
    /// Почати сесію. Ігнорується, якщо стан не <see cref="RecorderState.Idle"/> —
    /// ця перевірка і є захистом від дубля.
    /// </summary>
    /// <param name="consentAt">Коли підтверджено згоду співрозмовника.</param>
    public void Start(DateTimeOffset consentAt, string inputDevice, string outputDevice)
    {
        if (State != RecorderState.Idle)
        {
            return;
        }

        State = RecorderState.Preparing;
        LastError = null;
        Changed?.Invoke(this, EventArgs.Empty);

        SessionHandle? started = null;
        try
        {
            var handle = _store.Begin(consentAt, inputDevice, outputDevice);
            started = handle;

            var engine = _engineFactory(handle.Dir);
            engine.Start();

            _handle = handle;
            _engine = engine;
            StartedAt = DateTimeOffset.Now;
            State = RecorderState.Recording;
        }
        catch (Exception e)
        {
            // Те, що вже лягло на диск, лишається на диску і позначається чесно.
            if (started is not null)
            {
                TryInterrupt(started);
            }

            _handle = null;
            _engine = null;
            StartedAt = null;
            LastError = $"Не вдалося почати запис: {e.Message}";
            State = RecorderState.Idle;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Зупинити і завершити сесію. Ігнорується, якщо запис не триває.</summary>
    public void Stop()
    {
        if (State != RecorderState.Recording || _engine is null || _handle is null)
        {
            return;
        }

        State = RecorderState.Stopping;
        Changed?.Invoke(this, EventArgs.Empty);

        var engine = _engine;
        var handle = _handle;

        try
        {
            var result = engine.Stop();
            LastResult = result;
            LastSessionDir = handle.Dir;

            try
            {
                _store.Complete(handle, result);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                LastError = $"Запис зупинено, але не вдалося оновити meta.json: {e.Message}";
            }
        }
        catch (Exception e)
        {
            // Движок помер на зупинці — аудіо на диску, і сесія має це відображати.
            LastError = $"Запис зупинено з помилкою: {e.Message}";
            TryInterrupt(handle);
        }
        finally
        {
            engine.Dispose();
            _engine = null;
            _handle = null;
            StartedAt = null;
            State = RecorderState.Idle;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Відновити сесії, які лишив по собі крах — викликається при старті застосунку.</summary>
    public void RecoverInterruptedSessions()
    {
        RecoveredSessions = _store.RecoverInterrupted();
        if (RecoveredSessions.Count > 0)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void TryInterrupt(SessionHandle handle)
    {
        try
        {
            _store.Interrupt(handle);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Позначити не вдалося — відновлення при наступному старті це полагодить.
        }
    }
}
