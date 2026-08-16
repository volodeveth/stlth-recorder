namespace Stlth.Core.Meetings;

/// <summary>
/// Стежить за зустрічами і повідомляє про їхній початок та кінець.
///
/// <b>Ніколи не вмикає і не вимикає запис сам.</b> Рішення лишається за людиною:
/// автостоп виглядав би зручним рівно до першої зустрічі, яку він обірве посеред
/// розмови.
/// </summary>
public sealed class MeetingWatcher : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly Func<Meeting?> _probe;
    private readonly object _gate = new();

    private Timer? _timer;
    private Meeting? _current;
    private DateTimeOffset? _heldSince;
    private DateTimeOffset? _freeSince;

    /// <summary>Чи показали нагадування про початок цієї зустрічі.</summary>
    private bool _startAnnounced;

    /// <summary>
    /// Чи була зустріч насправді — тобто чи тримав хтось мікрофон достатньо довго.
    ///
    /// Окремо від <see cref="_startAnnounced"/> навмисно. Один прапорець на два
    /// питання вже дав дефект: приглушення нагадування про старт (коли людина сама
    /// почала запис) виглядало для коду як «зустріч оголошено», і через хвилину
    /// застосунок питав, чи зупинити запис «зустрічі», якої не було.
    /// </summary>
    private bool _meetingConfirmed;

    public MeetingWatcher(Func<Meeting?>? probe = null)
        => _probe = probe ?? MicrophoneHolders.Current;

    /// <summary>Зустріч почалася — варто спитати, чи вмикати запис.</summary>
    public event Action<Meeting>? Started;

    /// <summary>Зустріч завершилася. Важливо, коли запис усе ще триває.</summary>
    public event Action? Ended;

    public void Start()
    {
        lock (_gate)
        {
            _timer ??= new Timer(_ => Poll(DateTimeOffset.Now), null, PollInterval, PollInterval);
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
            Reset();
        }
    }

    public void Dispose() => Stop();

    /// <summary>
    /// Не нагадувати про зустріч, яку вже пишуть.
    ///
    /// Викликається після старту запису: людина щойно ухвалила рішення, і питати її
    /// про те саме вдруге — це шум.
    /// </summary>
    public void SuppressForCurrentMeeting()
    {
        lock (_gate)
        {
            // Тільки нагадування про старт. Чи була зустріч насправді — вирішує
            // мікрофон, а не той факт, що людина натиснула «Почати запис».
            _startAnnounced = true;
        }
    }

    /// <summary>Один крок опитування. Відкритий для тестів — час передається ззовні.</summary>
    internal void Poll(DateTimeOffset now)
    {
        Meeting? announce = null;
        var ended = false;

        lock (_gate)
        {
            var candidate = _probe();

            if (candidate is null)
            {
                _freeSince ??= now;

                if (MeetingDetector.HasEnded(_freeSince, now))
                {
                    // Кінець оголошуємо лише для зустрічі, яка справді була, а не
                    // для будь-якого запису, що триває.
                    ended = _meetingConfirmed;
                    Reset();
                }
            }
            else
            {
                _freeSince = null;

                if (_current?.ProcessName != candidate.Value.ProcessName)
                {
                    // Мікрофон перехопив інший застосунок — це вже інша зустріч.
                    _startAnnounced = false;
                    _meetingConfirmed = false;
                    _current = candidate;
                    _heldSince = now;
                }

                // Зустріч підтверджена, щойно мікрофон протримали достатньо довго —
                // незалежно від того, чи показували нагадування. Саме це відрізняє
                // «була зустріч» від «ми про неї сказали».
                if (_heldSince is { } since && now - since >= MeetingDetector.ConfirmationDelay)
                {
                    _meetingConfirmed = true;
                }
            }

            var decision = MeetingDetector.Decide(_current, _heldSince, _startAnnounced, now);
            if (decision is { } meeting)
            {
                _startAnnounced = true;
                announce = meeting;
            }
        }

        // Події кидаються поза замком: підписник із UI може відкрити вікно, і
        // тримати на цей час блокування опитування нема жодної причини.
        if (ended)
        {
            Ended?.Invoke();
        }

        if (announce is { } started)
        {
            Started?.Invoke(started);
        }
    }

    private void Reset()
    {
        _startAnnounced = false;
        _meetingConfirmed = false;
        _current = null;
        _heldSince = null;
        _freeSince = null;
    }
}
