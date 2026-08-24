namespace PowerPlanTray.Core.Content;

// Plain-language explanations for the most commonly-useful Windows power settings, keyed by
// their setting GUID (stable across Windows versions since Vista/7). Settings without an entry
// here simply fall back to the Windows-provided description in the UI — this list intentionally
// doesn't try to cover every exotic/OEM-specific setting, only the ones a typical user is likely
// to actually want to understand and change.
public static class SettingDescriptions
{
    private static readonly Guid Processor = new("54533251-82be-4824-96c1-47b60b740d00");
    private static readonly Guid Disk = new("0012ee47-9041-4b5d-9b77-535fba8b1442");
    private static readonly Guid Sleep = new("238c9fa8-0aad-41ed-83f4-97be242c8f20");
    private static readonly Guid PciExpress = new("501a4d13-42af-4429-9fd1-a8218c268e20");
    private static readonly Guid Usb = new("2a737441-1930-4402-8d77-b2bebba308a3");
    private static readonly Guid Display = new("7516b95f-f776-4464-8c53-06167f40cc99");
    private static readonly Guid Buttons = new("4f971e89-eebd-4455-a8de-9e59040e7347");
    private static readonly Guid EnergySaver = new("de830923-a562-41af-a086-e3a2c6bad2da");

    private static readonly Dictionary<string, string> ByGuid = new(StringComparer.OrdinalIgnoreCase)
    {
        // SUB_PROCESSOR (54533251-82be-4824-96c1-47b60b740d00)
        ["893dee8e-2bef-41e0-89c6-b55d0929964c"] =
            "The lowest speed your CPU is allowed to idle down to, as a percentage of its full speed. Lower values save power and reduce heat/fan noise when the computer is mostly idle, but can make the CPU feel slightly slower to 'wake up' for sudden bursts of work. Most people can leave this low (5-10%); raising it trades battery life / cooler running for slightly snappier response.",
        ["bc5038f7-23e0-4960-96da-33abaf5935ec"] =
            "The highest speed your CPU is allowed to run at, as a percentage of its full rated speed. Set to 100% for full performance. Lowering it (e.g. to 99% or below) is a well-known trick to disable 'Turbo Boost'/'Precision Boost' — the CPU's automatic short-term speed-up above its base clock — which reduces peak performance but also reduces peak heat, fan noise, and power draw. This is exactly what this app's 'Disable CPU Boost' checkbox controls.",
        ["be337238-0d82-4146-a960-4f3749d470c7"] =
            "Controls whether and how aggressively the CPU is allowed to briefly run above its normal top speed (Turbo Boost/Precision Boost) to handle short bursts of demanding work. 'Disabled' turns boosting off entirely (cooler, quieter, less peak performance); 'Enabled' is the normal balanced behavior; 'Aggressive' and the 'Efficient' variants push harder toward performance at the cost of more heat and power, using slightly different strategies for how eagerly the CPU boosts.",
        ["45bcc044-d885-43e2-8605-ee0ec6e96b59"] =
            "Works together with the boost mode setting to decide how much of the time the CPU is allowed to spend boosted versus running at its normal base speed. Higher settings mean the CPU boosts more often and for longer, which increases performance but also power use and heat.",
        ["94d3a615-a899-4ac5-ae2b-e4d8f634367f"] =
            "Decides how Windows manages CPU cooling. 'Passive' slows the CPU down first and only spins fans up if that isn't enough (quieter, prioritizes battery life). 'Active' spins fans up first to keep the CPU running fast (louder, prioritizes performance). Laptops usually default to Passive on battery and Active on AC power.",

        // SUB_DISK (0012ee47-9041-4b5d-9b77-535fba8b1442)
        ["6738e2c4-e8a5-4a42-b16a-e040e769756e"] =
            "How many minutes of inactivity before Windows spins down/parks your hard disk to save power. Doesn't affect solid-state drives (SSDs) in any meaningful way since they have no motor to spin down, but can add a slight delay the next time you access a spun-down mechanical hard disk. Set to 'Never' (0) to disable.",

        // SUB_SLEEP (238c9fa8-0aad-41ed-83f4-97be242c8f20)
        ["29f6c1db-86da-48c5-9fdb-f2b67b1f44da"] =
            "How many minutes of inactivity before the whole computer goes to sleep (low-power standby, resumes almost instantly when you touch a key or move the mouse). Set to 'Never' to disable automatic sleep entirely.",
        ["9d7815a6-7ee4-497e-8888-515a05f02364"] =
            "How many minutes of inactivity before the computer hibernates — saving everything to disk and powering off completely (uses no power at all, but takes longer to resume than sleep). On many modern PCs this happens after a period of sleep rather than directly from being active.",
        ["94ac6d29-73ce-41a6-809f-6363ba21b47e"] =
            "Hybrid sleep combines sleep and hibernate: the computer appears to sleep (fast resume) but Windows also saves your session to disk in the background, so you don't lose work if the battery runs out completely while 'asleep'. Mostly relevant to desktops without a battery backup; most laptops don't need this since they have a battery to protect the RAM-based sleep state anyway.",
        ["bd3b718a-0680-4d9d-8ab2-e1d2b4ac806d"] =
            "Whether scheduled tasks and devices (like a Wake-on-LAN network card or a scheduled backup) are allowed to wake the computer up from sleep on their own. Disabling this stops unexpected wake-ups but also stops legitimate scheduled wake events from working.",

        // SUB_PCIEXPRESS (501a4d13-42af-4429-9fd1-a8218c268e20)
        ["ee12f906-d277-404b-b6da-e5fa1a576df5"] =
            "Lets PCI Express devices (Wi-Fi cards, SSDs, graphics cards, etc.) drop into a lower-power link state when they're not actively transferring data. 'Moderate' or 'Maximum power savings' can meaningfully improve battery life, but on some hardware/driver combinations can cause stutters, dropped Wi-Fi connections, or SSD hiccups — if you notice odd hardware behavior after changing this, try setting it back to 'Off'.",

        // SUB_USB (2a737441-1930-4402-8d77-b2bebba308a3)
        ["48e6b7a6-50f5-4782-a5d4-53bb8f07e226"] =
            "Allows Windows to power down idle USB ports/devices to save energy. Occasionally causes USB mice, keyboards, or other peripherals to briefly 'lag' or need to be moved/clicked to wake back up. If you experience that, try disabling this setting.",

        // SUB_DISPLAY (7516b95f-f776-4464-8c53-06167f40cc99)
        ["3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e"] =
            "How many minutes of inactivity before the screen turns off to save power (the computer itself stays on and running — this is just the display). Set to 'Never' to keep the screen on indefinitely.",
        ["fbd9aa66-9553-4097-ba44-ed6e9d65eab8"] =
            "Lets Windows automatically adjust screen brightness based on an ambient light sensor (if your device has one) to save power in bright rooms and improve comfort in dark ones. Has no effect on devices without a built-in light sensor.",

        // SUB_BUTTONS (4f971e89-eebd-4455-a8de-9e59040e7347)
        ["5ca83367-6e45-459f-a27b-476b1d01c936"] =
            "What happens when you close a laptop's lid: typically 'Sleep', 'Hibernate', 'Shut down', or 'Do nothing'. 'Do nothing' is useful if you want the laptop to keep running (e.g. as a server or while playing music) with the lid closed, but note this can cause overheating if airflow is blocked by a closed lid.",
        ["7648efa3-dd9c-4e3e-b566-50f929386280"] =
            "What happens when you press the physical power button: typically 'Sleep', 'Hibernate', 'Shut down', or 'Do nothing' (nothing means only a long-press hard shutdown will work). Changing this to 'Sleep' is a common way to make the power button behave like a quick standby button instead of a shutdown button.",
        ["96996bc0-ad50-47ec-923b-6f41874dd9eb"] =
            "What happens when you press a dedicated 'sleep' button, where the keyboard/device has one: typically 'Sleep', 'Hibernate', or 'Do nothing'.",

        // SUB_ENERGYSAVER (de830923-a562-41af-a086-e3a2c6bad2da)
        ["e69653ca-cf7f-4f05-aa73-cb833fa90ad4"] =
            "The battery percentage at which Windows automatically turns on Battery Saver mode (which dims the screen, throttles background activity, and reduces performance a bit to stretch remaining battery life). Lowering this delays Battery Saver kicking in; raising it triggers it earlier.",
    };

    // This is also the source of truth for the Advanced page's curated view.
    public static IReadOnlyList<PowerPlanTray.Core.Models.CommonPowerSetting> CommonSettings { get; } =
    [
        new(Processor, new("893dee8e-2bef-41e0-89c6-b55d0929964c")),
        new(Processor, new("bc5038f7-23e0-4960-96da-33abaf5935ec")),
        new(Processor, new("be337238-0d82-4146-a960-4f3749d470c7")),
        new(Processor, new("45bcc044-d885-43e2-8605-ee0ec6e96b59")),
        new(Processor, new("94d3a615-a899-4ac5-ae2b-e4d8f634367f")),
        new(Disk, new("6738e2c4-e8a5-4a42-b16a-e040e769756e")),
        new(Sleep, new("29f6c1db-86da-48c5-9fdb-f2b67b1f44da")),
        new(Sleep, new("9d7815a6-7ee4-497e-8888-515a05f02364")),
        new(Sleep, new("94ac6d29-73ce-41a6-809f-6363ba21b47e")),
        new(Sleep, new("bd3b718a-0680-4d9d-8ab2-e1d2b4ac806d")),
        new(PciExpress, new("ee12f906-d277-404b-b6da-e5fa1a576df5")),
        new(Usb, new("48e6b7a6-50f5-4782-a5d4-53bb8f07e226")),
        new(Display, new("3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e")),
        new(Display, new("fbd9aa66-9553-4097-ba44-ed6e9d65eab8")),
        new(Buttons, new("5ca83367-6e45-459f-a27b-476b1d01c936")),
        new(Buttons, new("7648efa3-dd9c-4e3e-b566-50f929386280")),
        new(Buttons, new("96996bc0-ad50-47ec-923b-6f41874dd9eb")),
        new(EnergySaver, new("e69653ca-cf7f-4f05-aa73-cb833fa90ad4")),
    ];

    public static string? GetLaymanDescription(Guid settingGuid) =>
        ByGuid.TryGetValue(settingGuid.ToString(), out var text) ? text : null;
}
