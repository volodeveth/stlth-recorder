using System.Runtime.InteropServices;

namespace Stlth.Core.Audio;

/// <summary>Serializes stream restarts and provides a synchronous shutdown boundary.</summary>
public sealed class RestartGate
{
    private readonly object _sync = new();
    private bool _closed;
    private bool _running;

    /// <summary>Runs a restart exclusively, unless the gate has been closed.</summary>
    public bool TryRun(Action restart)
    {
        ArgumentNullException.ThrowIfNull(restart);

        lock (_sync)
        {
            while (_running && !_closed)
            {
                Monitor.Wait(_sync);
            }

            if (_closed)
            {
                return false;
            }

            _running = true;
        }

        try
        {
            restart();
            return true;
        }
        finally
        {
            lock (_sync)
            {
                _running = false;
                Monitor.PulseAll(_sync);
            }
        }
    }

    /// <summary>Closes the gate and waits for a restart already in progress to finish.</summary>
    public void Close()
    {
        lock (_sync)
        {
            _closed = true;
            Monitor.PulseAll(_sync);

            while (_running)
            {
                Monitor.Wait(_sync);
            }
        }
    }
}

/// <summary>Classifies expected audio-device failures during stream restart.</summary>
public static class RestartFault
{
    public static bool IsDeviceFault(Exception e) =>
        e is COMException
            or InvalidOperationException
            or UnauthorizedAccessException
            or InvalidComObjectException;
}
