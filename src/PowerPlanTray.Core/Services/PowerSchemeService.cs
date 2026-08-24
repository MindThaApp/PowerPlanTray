using System.Runtime.InteropServices;
using PowerPlanTray.Core.Models;

namespace PowerPlanTray.Core.Services;

/// <summary>
/// Provides access to the system's power schemes (power plans) via the
/// Windows Power Management API (PowrProf.dll).
/// </summary>
public class PowerSchemeService
{
    /// <summary>
    /// Enumerates all power schemes defined on the system, resolving each
    /// scheme's friendly display name.
    /// </summary>
    public IReadOnlyList<PowerScheme> GetAllSchemes()
    {
        var schemes = new List<PowerScheme>();

        uint index = 0;
        while (true)
        {
            Guid? guid = TryEnumerateSchemeGuid(index);
            if (guid is null)
            {
                break;
            }

            string name = ReadFriendlyName(guid.Value);
            schemes.Add(new PowerScheme(guid.Value, name));
            index++;
        }

        return schemes;
    }

    /// <summary>
    /// Gets the GUID of the currently active power scheme.
    /// </summary>
    public Guid GetActiveSchemeGuid()
    {
        uint result = NativeMethods.PowerGetActiveScheme(IntPtr.Zero, out IntPtr activeGuidPtr);
        if (result != NativeMethods.ERROR_SUCCESS || activeGuidPtr == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"PowerGetActiveScheme failed with error code {result}.");
        }

        try
        {
            return Marshal.PtrToStructure<Guid>(activeGuidPtr);
        }
        finally
        {
            NativeMethods.LocalFree(activeGuidPtr);
        }
    }

    /// <summary>
    /// Sets the active power scheme to the scheme identified by <paramref name="schemeGuid"/>.
    /// </summary>
    public void SetActiveScheme(Guid schemeGuid)
    {
        uint result = NativeMethods.PowerSetActiveScheme(IntPtr.Zero, ref schemeGuid);
        if (result != NativeMethods.ERROR_SUCCESS)
        {
            throw new InvalidOperationException(
                $"PowerSetActiveScheme failed with error code {result}.");
        }
    }

    /// <summary>
    /// Attempts to enumerate the scheme GUID at the given index. Returns null when
    /// there are no more schemes (end of enumeration).
    /// </summary>
    private static Guid? TryEnumerateSchemeGuid(uint index)
    {
        uint bufferSize = (uint)Marshal.SizeOf<Guid>();
        IntPtr buffer = Marshal.AllocHGlobal((int)bufferSize);
        try
        {
            while (true)
            {
                uint size = bufferSize;
                uint result = NativeMethods.PowerEnumerate(
                    RootPowerKey: IntPtr.Zero,
                    SchemeGuid: IntPtr.Zero,
                    SubGroupOfPowerSettingsGuid: IntPtr.Zero,
                    AccessFlags: NativeMethods.ACCESS_SCHEME,
                    Index: index,
                    Buffer: buffer,
                    BufferSize: ref size);

                if (result == NativeMethods.ERROR_SUCCESS)
                {
                    return Marshal.PtrToStructure<Guid>(buffer);
                }

                if (result == NativeMethods.ERROR_NO_MORE_ITEMS)
                {
                    return null;
                }

                if (result == NativeMethods.ERROR_MORE_DATA)
                {
                    // Grow the buffer to the required size and retry.
                    Marshal.FreeHGlobal(buffer);
                    bufferSize = size;
                    buffer = Marshal.AllocHGlobal((int)bufferSize);
                    continue;
                }

                throw new InvalidOperationException(
                    $"PowerEnumerate failed with error code {result}.");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// Reads the friendly (display) name of the given power scheme, growing the
    /// buffer as needed until it is large enough to hold the UTF-16 string.
    /// </summary>
    private static string ReadFriendlyName(Guid schemeGuid)
    {
        uint bufferSize = 256;
        IntPtr buffer = Marshal.AllocHGlobal((int)bufferSize);
        try
        {
            while (true)
            {
                uint size = bufferSize;
                Guid guidCopy = schemeGuid;
                uint result = NativeMethods.PowerReadFriendlyName(
                    RootPowerKey: IntPtr.Zero,
                    SchemeGuid: ref guidCopy,
                    SubGroupOfPowerSettingsGuid: IntPtr.Zero,
                    PowerSettingGuid: IntPtr.Zero,
                    Buffer: buffer,
                    BufferSize: ref size);

                if (result == NativeMethods.ERROR_SUCCESS)
                {
                    string name = Marshal.PtrToStringUni(buffer) ?? string.Empty;
                    return name.TrimEnd('\0');
                }

                if (result == NativeMethods.ERROR_MORE_DATA)
                {
                    Marshal.FreeHGlobal(buffer);
                    bufferSize = size;
                    buffer = Marshal.AllocHGlobal((int)bufferSize);
                    continue;
                }

                throw new InvalidOperationException(
                    $"PowerReadFriendlyName failed with error code {result}.");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
