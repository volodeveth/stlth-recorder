namespace Stlth.Core.Audio;

/// <summary>Owns the active stream for one track and keeps failed replacements retryable.</summary>
public sealed class StreamSlot<T> where T : class, IDisposable
{
    public T? Current { get; private set; }

    public bool NeedsRetry { get; private set; }

    public void Replace(Func<T> create, Action<T> start)
    {
        ArgumentNullException.ThrowIfNull(create);
        ArgumentNullException.ThrowIfNull(start);

        var previous = Current;
        Current = null;
        NeedsRetry = true;
        previous?.Dispose();

        T? replacement = null;
        try
        {
            replacement = create();
            if (replacement is null)
            {
                throw new InvalidOperationException("The stream factory returned null.");
            }

            start(replacement);
            Current = replacement;
            replacement = null;
            NeedsRetry = false;
        }
        catch
        {
            try
            {
                replacement?.Dispose();
            }
            catch
            {
                // Preserve the exception that caused the replacement to fail.
            }

            Current = null;
            NeedsRetry = true;
            throw;
        }
    }

    public void Clear()
    {
        var current = Current;
        Current = null;
        NeedsRetry = false;
        current?.Dispose();
    }
}

public sealed record DeviceSwitchResult(string? InputName, string? OutputName);

public static class DeviceSwitch
{
    public static DeviceSwitchResult Apply(
        string? input,
        string? output,
        Func<bool, bool> restart)
    {
        ArgumentNullException.ThrowIfNull(restart);

        string? inputName = null;
        string? outputName = null;

        if (input is not null)
        {
            try
            {
                if (restart(false))
                {
                    inputName = input;
                }
            }
            catch (Exception e) when (RestartFault.IsDeviceFault(e))
            {
            }
        }

        if (output is not null)
        {
            try
            {
                if (restart(true))
                {
                    outputName = output;
                }
            }
            catch (Exception e) when (RestartFault.IsDeviceFault(e))
            {
            }
        }

        return new DeviceSwitchResult(inputName, outputName);
    }
}
