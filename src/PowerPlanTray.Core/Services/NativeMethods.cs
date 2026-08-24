using System.Runtime.InteropServices;

namespace PowerPlanTray.Core.Services;

/// <summary>
/// P/Invoke declarations for the Windows Power Management functions
/// exported by PowrProf.dll. See:
/// https://learn.microsoft.com/windows/win32/api/powrprof/
/// </summary>
internal static class NativeMethods
{
    /// <summary>Win32 success code.</summary>
    internal const uint ERROR_SUCCESS = 0;

    /// <summary>Returned when the supplied buffer is too small; BufferSize is updated with the required size.</summary>
    internal const uint ERROR_MORE_DATA = 234;

    /// <summary>Returned by PowerEnumerate when Index is past the last available item (end of enumeration).</summary>
    internal const uint ERROR_NO_MORE_ITEMS = 259;

    /// <summary>AccessFlags value used to enumerate top-level power schemes.</summary>
    internal const uint ACCESS_SCHEME = 16;

    /// <summary>
    /// Enumerates power schemes (when SchemeGuid/SubGroupOfPowerSettingsGuid are IntPtr.Zero and
    /// AccessFlags == ACCESS_SCHEME), subgroups, or individual settings, depending on AccessFlags.
    /// </summary>
    [DllImport("PowrProf.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    internal static extern uint PowerEnumerate(
        IntPtr RootPowerKey,
        IntPtr SchemeGuid,
        IntPtr SubGroupOfPowerSettingsGuid,
        uint AccessFlags,
        uint Index,
        IntPtr Buffer,
        ref uint BufferSize);

    /// <summary>
    /// Retrieves the friendly (display) name for a power scheme, subgroup, or individual setting.
    /// </summary>
    [DllImport("PowrProf.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    internal static extern uint PowerReadFriendlyName(
        IntPtr RootPowerKey,
        ref Guid SchemeGuid,
        IntPtr SubGroupOfPowerSettingsGuid,
        IntPtr PowerSettingGuid,
        IntPtr Buffer,
        ref uint BufferSize);

    /// <summary>
    /// Retrieves the currently active power scheme's GUID. The out pointer must be freed with LocalFree.
    /// </summary>
    [DllImport("PowrProf.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    internal static extern uint PowerGetActiveScheme(
        IntPtr UserRootPowerKey,
        out IntPtr ActivePolicyGuid);

    /// <summary>
    /// Sets the currently active power scheme.
    /// </summary>
    [DllImport("PowrProf.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    internal static extern uint PowerSetActiveScheme(
        IntPtr UserRootPowerKey,
        ref Guid SchemeGuid);

    [DllImport("kernel32.dll")]
    internal static extern IntPtr LocalFree(IntPtr hMem);
}
