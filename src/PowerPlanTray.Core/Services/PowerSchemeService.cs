using System.Runtime.InteropServices;
using PowerPlanTray.Core.Models;

namespace PowerPlanTray.Core.Services;

/// <summary>
/// Provides access to the system's power schemes (power plans) via the
/// Windows Power Management API (PowrProf.dll).
/// </summary>
public class PowerSchemeService
{
    public IReadOnlyList<(Guid SubgroupGuid, string SubgroupName)> GetSubgroups(Guid schemeGuid) =>
        EnumerateGuids(schemeGuid, null, NativeMethods.ACCESS_SUBGROUP)
            .Select(guid => (guid, ReadFriendlyName(schemeGuid, guid, null))).ToArray();

    public IReadOnlyList<(Guid SettingGuid, string SettingName)> GetSettings(Guid schemeGuid, Guid subgroupGuid) =>
        EnumerateGuids(schemeGuid, subgroupGuid, NativeMethods.ACCESS_INDIVIDUAL_SETTING)
            .Select(guid => (guid, ReadFriendlyName(schemeGuid, subgroupGuid, guid))).ToArray();

    public string? GetSettingDescription(Guid subgroupGuid, Guid settingGuid)
    {
        return ReadString((IntPtr buffer, ref uint size) =>
            NativeMethods.PowerReadDescription(IntPtr.Zero, IntPtr.Zero, ref subgroupGuid, ref settingGuid, buffer, ref size));
    }

    public PowerSettingMetadata GetSettingMetadata(Guid subgroupGuid, Guid settingGuid)
    {
        uint? min = NativeMethods.PowerReadValueMin(IntPtr.Zero, ref subgroupGuid, ref settingGuid, out uint minValue) == 0 ? minValue : null;
        uint? max = NativeMethods.PowerReadValueMax(IntPtr.Zero, ref subgroupGuid, ref settingGuid, out uint maxValue) == 0 ? maxValue : null;
        uint? increment = NativeMethods.PowerReadValueIncrement(IntPtr.Zero, ref subgroupGuid, ref settingGuid, out uint incrementValue) == 0 ? incrementValue : null;
        string? units = ReadString((IntPtr buffer, ref uint size) =>
            NativeMethods.PowerReadValueUnitsSpecifier(IntPtr.Zero, ref subgroupGuid, ref settingGuid, buffer, ref size));

        var choices = new List<PowerSettingChoice>();
        for (uint index = 0; index < 256; index++)
        {
            uint size = sizeof(uint);
            IntPtr buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                uint result = NativeMethods.PowerReadPossibleValue(IntPtr.Zero, ref subgroupGuid, ref settingGuid,
                    out _, index, buffer, ref size);
                if (result != NativeMethods.ERROR_SUCCESS || size < sizeof(uint)) break;
                uint value = unchecked((uint)Marshal.ReadInt32(buffer));
                string? name = ReadString((IntPtr nameBuffer, ref uint nameSize) =>
                    NativeMethods.PowerReadPossibleFriendlyName(IntPtr.Zero, ref subgroupGuid, ref settingGuid,
                        index, nameBuffer, ref nameSize));
                choices.Add(new PowerSettingChoice(value, string.IsNullOrWhiteSpace(name) ? value.ToString() : name));
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        return new PowerSettingMetadata(min, max, increment, units, choices);
    }

    public uint GetACValue(Guid schemeGuid, Guid subgroupGuid, Guid settingGuid) =>
        ReadValue(NativeMethods.PowerReadACValueIndex, schemeGuid, subgroupGuid, settingGuid);

    public uint GetDCValue(Guid schemeGuid, Guid subgroupGuid, Guid settingGuid) =>
        ReadValue(NativeMethods.PowerReadDCValueIndex, schemeGuid, subgroupGuid, settingGuid);

    public void SetACValue(Guid schemeGuid, Guid subgroupGuid, Guid settingGuid, uint value) =>
        WriteValue(NativeMethods.PowerWriteACValueIndex, schemeGuid, subgroupGuid, settingGuid, value);

    public void SetDCValue(Guid schemeGuid, Guid subgroupGuid, Guid settingGuid, uint value) =>
        WriteValue(NativeMethods.PowerWriteDCValueIndex, schemeGuid, subgroupGuid, settingGuid, value);

    public bool IsSettingHidden(Guid subgroupGuid, Guid settingGuid) =>
        (NativeMethods.PowerReadSettingAttributes(ref subgroupGuid, ref settingGuid) & NativeMethods.POWER_ATTRIBUTE_HIDE) != 0;
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

    private static IReadOnlyList<Guid> EnumerateGuids(Guid schemeGuid, Guid? subgroupGuid, uint accessFlags)
    {
        var results = new List<Guid>();
        IntPtr schemePtr = Marshal.AllocHGlobal(Marshal.SizeOf<Guid>());
        IntPtr subgroupPtr = IntPtr.Zero;
        IntPtr buffer = Marshal.AllocHGlobal(Marshal.SizeOf<Guid>());
        try
        {
            Marshal.StructureToPtr(schemeGuid, schemePtr, false);
            if (subgroupGuid is Guid subgroup)
            {
                subgroupPtr = Marshal.AllocHGlobal(Marshal.SizeOf<Guid>());
                Marshal.StructureToPtr(subgroup, subgroupPtr, false);
            }
            for (uint index = 0; ; index++)
            {
                uint size = (uint)Marshal.SizeOf<Guid>();
                uint result = NativeMethods.PowerEnumerate(IntPtr.Zero, schemePtr, subgroupPtr, accessFlags, index, buffer, ref size);
                if (result == NativeMethods.ERROR_NO_MORE_ITEMS) break;
                if (result != NativeMethods.ERROR_SUCCESS)
                    throw new InvalidOperationException($"PowerEnumerate failed with error code {result}.");
                results.Add(Marshal.PtrToStructure<Guid>(buffer));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
            if (subgroupPtr != IntPtr.Zero) Marshal.FreeHGlobal(subgroupPtr);
            Marshal.FreeHGlobal(schemePtr);
        }
        return results;
    }

    private delegate uint StringReader(IntPtr buffer, ref uint size);
    private static string? ReadString(StringReader reader)
    {
        uint size = 0;
        uint result = reader(IntPtr.Zero, ref size);
        if (result != NativeMethods.ERROR_SUCCESS && result != NativeMethods.ERROR_MORE_DATA) return null;
        if (size == 0) return null;
        IntPtr buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            result = reader(buffer, ref size);
            return result == NativeMethods.ERROR_SUCCESS ? Marshal.PtrToStringUni(buffer)?.TrimEnd('\0') : null;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static string ReadFriendlyName(Guid schemeGuid, Guid? subgroupGuid, Guid? settingGuid)
    {
        IntPtr subgroupPtr = IntPtr.Zero;
        IntPtr settingPtr = IntPtr.Zero;
        try
        {
            if (subgroupGuid is Guid subgroup)
            {
                subgroupPtr = Marshal.AllocHGlobal(Marshal.SizeOf<Guid>());
                Marshal.StructureToPtr(subgroup, subgroupPtr, false);
            }
            if (settingGuid is Guid setting)
            {
                settingPtr = Marshal.AllocHGlobal(Marshal.SizeOf<Guid>());
                Marshal.StructureToPtr(setting, settingPtr, false);
            }
            return ReadString((IntPtr buffer, ref uint size) => NativeMethods.PowerReadFriendlyName(
                IntPtr.Zero, ref schemeGuid, subgroupPtr, settingPtr, buffer, ref size)) ?? string.Empty;
        }
        finally
        {
            if (settingPtr != IntPtr.Zero) Marshal.FreeHGlobal(settingPtr);
            if (subgroupPtr != IntPtr.Zero) Marshal.FreeHGlobal(subgroupPtr);
        }
    }

    private delegate uint ValueReader(IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, out uint value);
    private static uint ReadValue(ValueReader reader, Guid scheme, Guid subgroup, Guid setting)
    {
        uint result = reader(IntPtr.Zero, ref scheme, ref subgroup, ref setting, out uint value);
        if (result != 0) throw new InvalidOperationException($"Reading the power setting failed with error code {result}.");
        return value;
    }

    private delegate uint ValueWriter(IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, uint value);
    private static void WriteValue(ValueWriter writer, Guid scheme, Guid subgroup, Guid setting, uint value)
    {
        uint result = writer(IntPtr.Zero, ref scheme, ref subgroup, ref setting, value);
        if (result != 0) throw new InvalidOperationException($"Writing the power setting failed with error code {result}.");
        result = NativeMethods.PowerSetActiveScheme(IntPtr.Zero, ref scheme);
        if (result != 0) throw new InvalidOperationException($"Applying the power scheme failed with error code {result}.");
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
