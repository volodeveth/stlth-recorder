namespace Stlth.Core.Audio;

/// <summary>Runs a periodic check and provides a synchronous stop boundary.</summary>
public sealed class WatchdogTimer : IDisposable
{
    [ThreadStatic]
    private static HashSet<WatchdogTimer>? _runningOnThisThread;

    private readonly object _gate = new();
    private readonly Action _check;
    private Timer? _timer;
    private bool _stopped;
    private bool _checkRunning;

    public WatchdogTimer(Action check, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(check);
        if (period <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(period), "The period must be positive.");
        }

        _check = check;
        _timer = new Timer(OnTimer, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _timer.Change(period, period);
    }

    /// <summary>Stops future checks and waits for a check on another thread to finish.</summary>
    public void Stop()
    {
        lock (_gate)
        {
            if (!_stopped)
            {
                _stopped = true;
                _timer?.Dispose();
                _timer = null;
            }

            // A check cannot wait for itself. It has already disabled future checks;
            // any concurrent Stop caller still waits for this check to leave OnTimer.
            if (_runningOnThisThread?.Contains(this) == true)
            {
                return;
            }

            while (_checkRunning)
            {
                Monitor.Wait(_gate);
            }
        }
    }

    public void Dispose() => Stop();

    private void OnTimer(object? state)
    {
        lock (_gate)
        {
            if (_stopped || _checkRunning)
            {
                return;
            }

            _checkRunning = true;
        }

        HashSet<WatchdogTimer>? running = null;
        var registered = false;
        try
        {
            running = _runningOnThisThread ??= [];
            running.Add(this);
            registered = true;
            _check();
        }
        finally
        {
            if (registered)
            {
                running!.Remove(this);
                if (running.Count == 0)
                {
                    _runningOnThisThread = null;
                }
            }

            lock (_gate)
            {
                _checkRunning = false;
                Monitor.PulseAll(_gate);
            }
        }
    }
}
