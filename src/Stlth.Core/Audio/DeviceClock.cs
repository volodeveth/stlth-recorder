using System.Runtime.InteropServices;

namespace Stlth.Core.Audio;

/// <summary>Reads a device clock, treating a vanished or released device as unavailable.</summary>
public static class DeviceClock
{
    public static ulong ReadOrZero(Func<ulong> read)
    {
        ArgumentNullException.ThrowIfNull(read);

        try
        {
            return read();
        }
        catch (Exception e) when (e is InvalidComObjectException
                                       or COMException
                                       or InvalidOperationException
                                       or NullReferenceException)
        {
            return 0;
        }
    }
}
