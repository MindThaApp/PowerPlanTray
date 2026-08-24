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
    internal const uint ACCESS_SUBGROUP = 17;
    internal const uint ACCESS_INDIVIDUAL_SETTING = 18;
    internal const uint POWER_ATTRIBUTE_HIDE = 1;

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

    [DllImport("PowrProf.dll", CharSet = CharSet.Unicode)]
    internal static extern uint PowerReadDescription(IntPtr rootPowerKey, IntPtr schemeGuid,
        ref Guid subgroupGuid, ref Guid settingGuid, IntPtr buffer, ref uint bufferSize);

    [DllImport("PowrProf.dll")]
    internal static extern uint PowerReadACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid,
        ref Guid subgroupGuid, ref Guid settingGuid, out uint valueIndex);

    [DllImport("PowrProf.dll")]
    internal static extern uint PowerReadDCValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid,
        ref Guid subgroupGuid, ref Guid settingGuid, out uint valueIndex);

    [DllImport("PowrProf.dll")]
    internal static extern uint PowerWriteACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid,
        ref Guid subgroupGuid, ref Guid settingGuid, uint valueIndex);

    [DllImport("PowrProf.dll")]
    internal static extern uint PowerWriteDCValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid,
        ref Guid subgroupGuid, ref Guid settingGuid, uint valueIndex);

    [DllImport("PowrProf.dll")]
    internal static extern uint PowerReadValueMin(IntPtr rootPowerKey, ref Guid subgroupGuid,
        ref Guid settingGuid, out uint valueMinimum);

    [DllImport("PowrProf.dll")]
    internal static extern uint PowerReadValueMax(IntPtr rootPowerKey, ref Guid subgroupGuid,
        ref Guid settingGuid, out uint valueMaximum);

    [DllImport("PowrProf.dll")]
    internal static extern uint PowerReadValueIncrement(IntPtr rootPowerKey, ref Guid subgroupGuid,
        ref Guid settingGuid, out uint valueIncrement);

    [DllImport("PowrProf.dll")]
    internal static extern uint PowerReadValueUnitsSpecifier(IntPtr rootPowerKey, ref Guid subgroupGuid,
        ref Guid settingGuid, IntPtr buffer, ref uint bufferSize);

    [DllImport("PowrProf.dll")]
    internal static extern uint PowerReadPossibleValue(IntPtr rootPowerKey, ref Guid subgroupGuid,
        ref Guid settingGuid, uint possibleSettingIndex, out uint type, IntPtr buffer, ref uint bufferSize);

    [DllImport("PowrProf.dll", CharSet = CharSet.Unicode)]
    internal static extern uint PowerReadPossibleFriendlyName(IntPtr rootPowerKey, ref Guid subgroupGuid,
        ref Guid settingGuid, uint possibleSettingIndex, IntPtr buffer, ref uint bufferSize);

    [DllImport("PowrProf.dll")]
    internal static extern uint PowerReadSettingAttributes(ref Guid subgroupGuid, ref Guid settingGuid);

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
