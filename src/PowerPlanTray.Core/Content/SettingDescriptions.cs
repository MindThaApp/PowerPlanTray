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
    private static readonly Guid SubNone = new("fea3413e-7e05-4911-9a71-700331f1c294");
    private static readonly Guid WirelessAdapter = new("19cbb8fa-5279-450e-9fac-8a3d5fedd0c1");
    private static readonly Guid Multimedia = new("9596fb26-9850-41fd-ac3e-f7c3c00afd4b");
    private static readonly Guid Battery = new("e73a048d-bf27-4f12-9731-8b2076e8891f");
    private static readonly Guid IdleResiliency = new("2e601130-5351-4d9d-8e04-252966bad054");
    private static readonly Guid InterruptSteering = new("48672f38-7a9a-4bb2-8bf8-3d85be19de4e");
    private static readonly Guid DesktopBackground = new("0d7dbae2-4294-402a-ba8e-26777e8488cd");
    private static readonly Guid Graphics = new("5fb4938d-1ee8-4b0f-9a3c-5036b0ab995c");

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
        ["06cadf0e-64ed-448a-8927-ce7bf90eb35d"] =
            "How busy the CPU has to be (as a percentage) before Windows considers ramping its speed up further. Lower values make the CPU react to load more eagerly (snappier, but more power draw and heat); higher values make it wait for heavier load before speeding up, trading a bit of responsiveness for battery life.",
        ["12a0ab44-fe28-4fa9-b3bd-4b64f44960a6"] =
            "How idle the CPU has to be (as a percentage) before Windows considers slowing it back down. Lower values make the CPU throttle down more readily (better battery life), higher values keep it running fast for longer after a burst of work, at the cost of extra power draw.",
        ["984cf492-3bed-4488-a8f9-4286c97bf5aa"] =
            "How many consecutive sampling intervals of high CPU load are required before Windows actually speeds the processor up. Higher values make speed increases more deliberate/delayed (smoother, less jittery power draw); lower values make the CPU respond to bursts of work almost instantly.",
        ["d8edeb9b-95cf-4f95-a73c-b061973693c8"] =
            "How many consecutive sampling intervals of low CPU load are required before Windows actually slows the processor back down. Higher values keep the CPU at a higher speed for longer after work finishes (feels snappier for bursty tasks); lower values drop speed sooner to save power.",
        ["0cc5b647-c1df-4637-891a-dec35c318583"] =
            "The minimum percentage of your CPU cores that Windows must always keep unparked (active and available), even when the system is mostly idle. Raising this keeps more cores ready for sudden multi-threaded work at the cost of some idle power savings; most people can leave this low.",
        ["ea062031-0e34-4ff1-9b6d-eb1059334028"] =
            "The maximum percentage of your CPU cores that Windows is allowed to keep unparked (active) at once. Lowering this below 100% forces some cores to stay parked even under heavy load, which can reduce peak multi-threaded performance and heat/power draw — mainly useful for capping power on many-core CPUs.",
        ["2430ab6f-a520-44a2-9601-f7f23b5134b1"] =
            "How much of the CPU's capacity needs to be in use before Windows unparks another core to share the load, versus just running the already-active cores harder. Lower values spread work across more cores sooner (can improve responsiveness on multi-threaded tasks); higher values favor keeping fewer cores busy longer to save power.",
        ["3b04d4fd-1cc7-4f23-ab1c-d1337819c4bb"] =
            "Whether the CPU is allowed to use its deepest throttle/T-states to cut power when running below its minimum performance state. 'Automatic' (the default) lets Windows and the hardware decide; forcing this off can occasionally help with certain CPU/chipset compatibility issues, at the cost of slightly higher idle power draw.",
        ["5d76a2ca-e8c0-402f-a133-2158492d58ad"] =
            "Whether the CPU is allowed to enter its idle/sleep states (C-states) between bursts of work at all. Leaving idle enabled saves significant power and reduces heat; disabling it keeps the CPU 'awake' at all times, which can help with certain latency-sensitive workloads or troubleshoot rare hardware timing issues, but noticeably increases power draw and heat.",
        ["7b224883-b3cc-4d79-819f-8374152cbe7c"] =
            "How busy a CPU core has to get before Windows 'promotes' it to a less power-efficient but faster idle state so it can respond to work more quickly next time. Lower values promote more eagerly (snappier response to bursts of activity, more power use); higher values wait longer, favoring deeper power savings.",
        ["4b92d758-5a24-4851-a470-815d78aee119"] =
            "How idle a CPU core has to stay before Windows 'demotes' it to a deeper, more power-saving idle state. Lower values drop into deep idle sooner (better battery life); higher values keep cores in a lighter idle state longer so they can wake up faster, at a small power cost.",
        ["8baa4a8a-14c6-4451-8e8b-14bdbd197537"] =
            "Enables 'autonomous mode' (hardware-controlled performance states, also known as HWP on modern Intel CPUs), letting the CPU itself decide its clock speed in real time instead of Windows' software governor doing it. Usually improves both performance and efficiency on CPUs that support it; only disable this if you suspect it's causing instability on unusual hardware.",
        ["93b8b6dc-0698-4d1c-9ee4-0644e900c85d"] =
            "On CPUs with a mix of fast 'performance' and efficient 'efficiency' cores (most modern Intel and some AMD hybrid chips), this controls which type of core Windows prefers scheduling threads onto: performant cores for speed, efficient cores for battery life, or 'Automatic' to let Windows decide per-workload. Has no effect on CPUs with only one core type.",
        ["619b7505-003b-4e82-b7a6-4dd29c300971"] =
            "Part of the 'latency sensitivity hint' feature: when an app asks Windows for extra responsiveness (e.g. during audio/video playback or gaming), this sets how high a minimum performance level the CPU should jump to in response. Higher values react more aggressively to such hints at the cost of extra power draw.",
        ["616cdaa5-695e-4545-97ad-97dc2d1bdd88"] =
            "Part of the 'latency sensitivity hint' feature: sets the minimum percentage of CPU cores that must stay unparked (ready) when an app requests low-latency responsiveness. Higher values keep more cores on standby for instant response, using more power; lower values save power but may react slightly slower to sudden latency-sensitive requests.",
        ["4b70f900-cdd9-4e66-aa26-ae8417f98173"] =
            "Part of the 'latency sensitivity hint' feature: sets the energy-performance preference (how aggressively the CPU favors speed over efficiency) while responding to an app's request for low-latency behavior. Higher values favor speed more strongly during those moments, at a power-draw cost.",

        // SUB_DISK (0012ee47-9041-4b5d-9b77-535fba8b1442)
        ["6738e2c4-e8a5-4a42-b16a-e040e769756e"] =
            "How many minutes of inactivity before Windows spins down/parks your hard disk to save power. Doesn't affect solid-state drives (SSDs) in any meaningful way since they have no motor to spin down, but can add a slight delay the next time you access a spun-down mechanical hard disk. Set to 'Never' (0) to disable.",
        ["0b2d69d7-a2a1-449c-9680-f91c70521c60"] =
            "Controls how aggressively SATA/AHCI disks are allowed to drop their link into a low-power state between transfers (HIPM/DIPM). More aggressive settings save power but can add tiny latency spikes on some drives/controllers; if you notice odd storage stutters after changing this, try a less aggressive setting or 'Active'.",
        ["51dea550-bb38-4bc4-991b-eacf37be5ec8"] =
            "Caps how much of a disk's maximum power budget it's allowed to use, as a percentage. Lowering this can reduce power draw and heat on some drives, but may also reduce peak read/write performance; most people should leave this at 100%.",
        ["d639518a-e56d-4345-8af2-b9f32fb26109"] =
            "How many milliseconds an NVMe SSD sits idle before Windows lets it drop into a lower-power state. Shorter timeouts save more power but mean the drive enters/exits low-power states more often, which can very slightly affect responsiveness on latency-sensitive workloads; most people can leave the default.",
        ["fc7372b6-ab2d-43ee-8797-15e9841f2cca"] =
            "Enables a feature (NOPPME) that lets an NVMe SSD notify the system when it's ready to accept more commands after waking from a low-power state, generally making the wake-up handshake faster and slightly more power-efficient. Only relevant to NVMe drives/controllers that support it.",

        // SUB_SLEEP (238c9fa8-0aad-41ed-83f4-97be242c8f20)
        ["29f6c1db-86da-48c5-9fdb-f2b67b1f44da"] =
            "How many minutes of inactivity before the whole computer goes to sleep (low-power standby, resumes almost instantly when you touch a key or move the mouse). Set to 'Never' to disable automatic sleep entirely.",
        ["9d7815a6-7ee4-497e-8888-515a05f02364"] =
            "How many minutes of inactivity before the computer hibernates — saving everything to disk and powering off completely (uses no power at all, but takes longer to resume than sleep). On many modern PCs this happens after a period of sleep rather than directly from being active.",
        ["94ac6d29-73ce-41a6-809f-6363ba21b47e"] =
            "Hybrid sleep combines sleep and hibernate: the computer appears to sleep (fast resume) but Windows also saves your session to disk in the background, so you don't lose work if the battery runs out completely while 'asleep'. Mostly relevant to desktops without a battery backup; most laptops don't need this since they have a battery to protect the RAM-based sleep state anyway.",
        ["bd3b718a-0680-4d9d-8ab2-e1d2b4ac806d"] =
            "Whether scheduled tasks and devices (like a Wake-on-LAN network card or a scheduled backup) are allowed to wake the computer up from sleep on their own. Disabling this stops unexpected wake-ups but also stops legitimate scheduled wake events from working.",
        ["25dfa149-5dd1-4736-b5ab-e8a37b5b8187"] =
            "Whether apps are allowed to use 'Away Mode' to keep the computer running (screen off, looks asleep to the user) instead of actually sleeping, typically so a media-sharing or recording task can finish in the background. Disabling this forces the computer to sleep normally instead, which can interrupt background media tasks that rely on Away Mode.",
        ["abfc2519-3608-4c2a-94ea-171b0ed546ab"] =
            "The master switch for whether the computer is allowed to enter low-power standby states at all (S1-S3/Modern Standby). Turning this off prevents the system from sleeping under any circumstances — closing the lid, the sleep timer, and the Start menu Sleep option will all fail to actually put it to sleep — useful only for troubleshooting sleep-related problems.",
        ["a4b195f5-8225-47d8-8012-9d41369786e2"] =
            "Whether apps and drivers are allowed to use a 'system required' request to temporarily keep the computer awake and prevent it from auto-sleeping while they're doing something important (e.g. burning a disc or installing an update). Disabling this can cause such operations to be interrupted by sleep.",
        ["d4c1d4c8-d5cc-43d3-b83e-fc51215cb04d"] =
            "Whether the computer is still allowed to go to sleep while other computers on the network have open file handles to files shared from it. Disabling this keeps the computer awake to avoid dropping those network connections, at the cost of never auto-sleeping while anyone is browsing its shared files.",
        ["7bc4a2f9-d8fc-4469-b07b-33eb785aaca0"] =
            "A safety-net sleep timeout used specifically during unattended scenarios (like Windows Update installing updates or a scheduled task running) so the machine doesn't stay awake indefinitely if something goes wrong. Rarely needs to be changed from its default.",
        ["1a34bdc3-7e6b-442e-a9d0-64b6ef378e84"] =
            "Enables workarounds for a known class of hardware real-time-clock (RTC) wake timer bugs on some older/lower-quality motherboards that can otherwise cause unreliable wake-from-sleep behavior. Leave this enabled unless a hardware vendor specifically advises otherwise.",

        // SUB_PCIEXPRESS (501a4d13-42af-4429-9fd1-a8218c268e20)
        ["ee12f906-d277-404b-b6da-e5fa1a576df5"] =
            "Lets PCI Express devices (Wi-Fi cards, SSDs, graphics cards, etc.) drop into a lower-power link state when they're not actively transferring data. 'Moderate' or 'Maximum power savings' can meaningfully improve battery life, but on some hardware/driver combinations can cause stutters, dropped Wi-Fi connections, or SSD hiccups — if you notice odd hardware behavior after changing this, try setting it back to 'Off'.",

        // SUB_USB (2a737441-1930-4402-8d77-b2bebba308a3)
        ["48e6b7a6-50f5-4782-a5d4-53bb8f07e226"] =
            "Allows Windows to power down idle USB ports/devices to save energy. Occasionally causes USB mice, keyboards, or other peripherals to briefly 'lag' or need to be moved/clicked to wake back up. If you experience that, try disabling this setting.",
        ["0853a681-27c8-4100-a2fd-82013e970683"] =
            "How many milliseconds a USB hub must sit idle before Windows suspends it to save power (used together with USB selective suspend). Shorter timeouts save a bit more power; longer timeouts can reduce the odd wake-up lag some USB peripherals show after being idle.",
        ["d4e98f31-5ffe-4ce1-be31-1b38b384c009"] =
            "How aggressively USB 3.x ports/controllers are allowed to enter their low-power link states (U1/U2) when idle. More aggressive power savings reduce battery drain, but on some USB 3 controllers or external drives/docks can cause disconnects, audio glitches, or slow device wake-up — if you see that, try a lower savings level or 'Off'.",

        // SUB_DISPLAY (7516b95f-f776-4464-8c53-06167f40cc99)
        ["3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e"] =
            "How many minutes of inactivity before the screen turns off to save power (the computer itself stays on and running — this is just the display). Set to 'Never' to keep the screen on indefinitely.",
        ["fbd9aa66-9553-4097-ba44-ed6e9d65eab8"] =
            "Lets Windows automatically adjust screen brightness based on an ambient light sensor (if your device has one) to save power in bright rooms and improve comfort in dark ones. Has no effect on devices without a built-in light sensor.",
        ["aded5e82-b909-4619-9949-f5d71dac0bcb"] =
            "The normal screen brightness level, as a percentage, used while the display is active and you're not idle. This is the same brightness slider you can adjust from the taskbar/Action Center — changing it here changes the plan's default rather than a one-off adjustment.",
        ["f1fbfde2-a960-4165-9f88-50667911ce96"] =
            "The reduced brightness level the screen dims to shortly before it would turn off completely (a warning dim, mainly on battery), giving you a visual cue that the screen is about to go dark. Only takes effect if 'Dim display after' is configured and reached before the 'Turn off display after' timeout.",
        ["17aaa29b-8b43-4b94-aafe-35f64daaf1ee"] =
            "How many minutes of inactivity before the screen dims (not fully off) as an early power-saving/warning step, ahead of it turning off completely. Set shorter than 'Turn off display after' for it to have any visible effect; set to 'Never' to skip dimming and go straight to off.",
        ["684c3e69-a4f7-4014-8754-d45179a56167"] =
            "On displays with HDR/Advanced Color enabled, this trades off color/brightness accuracy versus power draw for the extra processing HDR requires. 'Power saving bias' extends battery life at a slight cost to HDR visual fidelity; 'Visual quality bias' prioritizes the best HDR picture. Only matters on Advanced Color-capable displays with HDR turned on.",
        ["8ec4b3a5-6868-48c2-be75-4f3044be88a7"] =
            "A separate, usually shorter, display-off timeout that applies specifically when the machine is locked (e.g. at the sign-in/lock screen). Lets you turn the screen off faster while locked than while you're actively signed in and working, without affecting your normal 'Turn off display after' timeout.",
        ["90959d22-d6a1-49b9-af93-bce885ad335b"] =
            "Lets Windows dynamically adjust display refresh rate and other display parameters based on content and usage to save power on displays that support it (e.g. variable refresh rate panels). Has no effect on displays/GPUs that don't support adaptive refresh.",
        ["a9ceb8da-cd46-44fb-a98b-02af69de4623"] =
            "Whether apps are allowed to request that the display stay on and required (e.g. during a video call or while watching a video), overriding the normal 'Turn off display after' timeout for as long as the request is active. Disabling this can cause the screen to turn off mid-video or mid-call even while you're actively watching.",

        // SUB_BUTTONS (4f971e89-eebd-4455-a8de-9e59040e7347)
        ["5ca83367-6e45-459f-a27b-476b1d01c936"] =
            "What happens when you close a laptop's lid: typically 'Sleep', 'Hibernate', 'Shut down', or 'Do nothing'. 'Do nothing' is useful if you want the laptop to keep running (e.g. as a server or while playing music) with the lid closed, but note this can cause overheating if airflow is blocked by a closed lid.",
        ["7648efa3-dd9c-4e3e-b566-50f929386280"] =
            "What happens when you press the physical power button: typically 'Sleep', 'Hibernate', 'Shut down', or 'Do nothing' (nothing means only a long-press hard shutdown will work). Changing this to 'Sleep' is a common way to make the power button behave like a quick standby button instead of a shutdown button.",
        ["96996bc0-ad50-47ec-923b-6f41874dd9eb"] =
            "What happens when you press a dedicated 'sleep' button, where the keyboard/device has one: typically 'Sleep', 'Hibernate', or 'Do nothing'.",
        ["833a6b62-dfa4-46d1-82f8-e09e34d029d6"] =
            "Allows a forced shutdown by holding the power button (or closing the lid) for several seconds even if Windows or an app would normally intercept that and do something else (like sleep). Useful as a guaranteed 'hard reset' escape hatch if the system ever becomes unresponsive to normal power actions.",
        ["99ff10e7-23b1-4c07-a9d1-5c3206d741b4"] =
            "What happens when you open a closed laptop lid: normally 'Turn on the display' so the screen wakes up immediately. There's little reason to change this from the default on most laptops.",
        ["a7066653-8d6c-40a8-910e-a1f54b84c7e5"] =
            "What action the power button inside the Windows Start menu performs: 'Sleep', 'Hibernate', or 'Shut down'. This is separate from the physical power button setting above and only affects the on-screen Start menu power button.",

        // SUB_ENERGYSAVER (de830923-a562-41af-a086-e3a2c6bad2da)
        ["e69653ca-cf7f-4f05-aa73-cb833fa90ad4"] =
            "The battery percentage at which Windows automatically turns on Battery Saver mode (which dims the screen, throttles background activity, and reduces performance a bit to stretch remaining battery life). Lowering this delays Battery Saver kicking in; raising it triggers it earlier.",
        ["13d09884-f74e-474a-a852-b6bde8ad03a8"] =
            "How much Battery Saver is allowed to dim the screen when it kicks in, as a percentage weight. A higher value lets Battery Saver reduce brightness more aggressively (saving more battery, but a noticeably dimmer screen); a lower value limits how far it's allowed to dim things.",
        ["5c5bb349-ad29-4ee2-9d0b-2b25270f7a81"] =
            "Chooses how aggressively Battery Saver throttles background activity and visual effects once it's on: 'User' uses the standard, moderate behavior, while 'Aggressive' cuts background app activity and effects harder to squeeze out extra battery life at the cost of some background app responsiveness (e.g. delayed notifications).",

        // SUB_NONE (fea3413e-7e05-4911-9a71-700331f1c294) — settings that don't belong to any subgroup
        ["0e796bdb-100d-47d6-a2d5-f7d2daa51f51"] =
            "Whether Windows requires you to enter your password when the computer wakes up from sleep. Turning this off means anyone who wakes your PC from sleep can get straight to your desktop without signing in — a meaningful security/privacy tradeoff, mainly considered on trusted home desktops.",
        ["245d8541-3943-4422-b025-13a784f679b7"] =
            "An internal marker Windows uses to classify a plan as 'Power saver', 'Balanced', or 'High performance' for its own UI and defaults. Not meant to be changed by hand — editing it doesn't change the plan's actual settings, just how Windows labels/categorizes it internally.",
        ["4faab71a-92e5-4726-b531-224559672d19"] =
            "A broad policy hint that tells connected devices and drivers whether to generally favor performance or power savings when they have their own internal power-management choices to make. 'Power savings' extends battery life slightly across many small devices; 'Performance' favors responsiveness.",
        ["68afb2d9-ee95-47a8-8f50-4115088073b1"] =
            "How aggressively the system is allowed to cut power to components while in Modern Standby but disconnected from any network. 'Aggressive' saves more battery while sitting disconnected in your bag; 'Normal' is more conservative and can resume slightly faster.",
        ["f15576e8-98b7-4186-b944-eafa664402d9"] =
            "Whether the network stays connected while the computer is in Modern Standby, so things like emails, calendar syncs, and Find My Device can keep working while the lid is closed. 'Managed by Windows' lets the OS decide based on conditions like battery level; forcing it 'Enable' keeps connectivity active in standby at some extra battery cost, and 'Disable' saves the most battery but background updates stop until you wake the PC.",

        // SUB_WIRELESSADAPTER (19cbb8fa-5279-450e-9fac-8a3d5fedd0c1)
        ["12bbebe6-58d6-4636-95bb-3217ef867c1a"] =
            "How aggressively Wi-Fi/wireless adapters are allowed to power down between transmissions to save battery. Higher power-saving levels stretch battery life but can slightly increase latency or reduce throughput/range on some adapters; if you notice flaky Wi-Fi after changing this, try 'Maximum Performance'.",

        // SUB_MULTIMEDIA (9596fb26-9850-41fd-ac3e-f7c3c00afd4b)
        ["03680956-93bc-4294-bba6-4e0f09bb717f"] =
            "What the computer is allowed to do when it's actively sharing media (e.g. streaming to another device via Media Player/DLNA): sleep normally, stay awake ('Prevent idling to sleep'), or use the lower-power 'Away Mode' to look asleep while still serving media. Choose 'Prevent idling to sleep' or 'Away Mode' if shared streams keep cutting out because the PC falls asleep mid-stream.",
        ["10778347-1370-4ee0-8bbd-33bdacaade49"] =
            "Trades off video decoding quality versus power draw during video playback in supporting apps. 'Video playback performance bias' favors smoother, higher-quality playback at more power cost; 'power-saving bias' favors battery life, which can matter for long video playback on battery.",
        ["34c7b99f-9a6d-4b3c-8dc7-b6693b78cef4"] =
            "Chooses how video playback balances picture quality against battery life: 'Optimize video quality' favors the best picture, 'Optimize power savings' favors longer battery life during video playback (may reduce quality/frame smoothness on some content), and 'Balanced' sits in between.",

        // SUB_BATTERY (e73a048d-bf27-4f12-9731-8b2076e8891f)
        ["5dbb7c9f-38e9-40d2-9749-4f8a0e9f640f"] =
            "Whether Windows shows a notification when the battery reaches the 'Critical battery level' threshold. Turning this off means you won't get a warning before the critical-battery action (like forced hibernate or shutdown) kicks in — generally best left on.",
        ["637ea02f-bbcb-4015-8e2c-a1c7b9c0b546"] =
            "What the computer does automatically once the battery drops to the 'Critical battery level' percentage: typically hibernate or shut down to avoid an uncontrolled power-off and possible data loss. 'Do nothing' risks the machine simply dying mid-task once the battery is fully depleted.",
        ["8183ba9a-e910-48da-8769-14ae6dc1170a"] =
            "The battery percentage at which Windows considers the battery 'low' and triggers the low-battery notification/action (separate from, and higher than, the critical level). Raising this gives you an earlier heads-up to plug in; lowering it delays the warning.",
        ["9a66d8d7-4ff7-4ef9-b5a2-5a326ca2a469"] =
            "The battery percentage at which Windows considers the battery 'critical' and triggers the critical-battery action (typically hibernate or shutdown) to protect your work before the battery fully dies. Should generally stay low enough to leave time for the critical action to actually complete.",
        ["bcded951-187b-4d05-bccc-f7e51960c258"] =
            "Whether Windows shows a notification when the battery reaches the 'Low battery level' threshold. Turning this off means you lose the early low-battery heads-up, though the critical-battery notification/action (if enabled) will still occur later.",
        ["d8742dcb-3e6a-4b3c-b3fe-374623cdcf06"] =
            "What the computer does automatically once the battery drops to the 'Low battery level' percentage: options range from 'Do nothing' (just show the notification) up to sleep/hibernate/shut down. Most people leave this at 'Do nothing' and let the critical-level action do the actual protective response later.",
        ["f3c5027d-cd16-4930-aa6b-90db844a8f00"] =
            "A percentage of battery capacity that Windows keeps in reserve and won't report as usable, acting as a safety buffer so the reported '0%' still has a small real cushion left to allow a clean shutdown/hibernate. Not typically something you need to change.",

        // SUB_IR (2e601130-5351-4d9d-8e04-252966bad054) — Idle Resiliency
        ["3166bc41-7e98-4e03-b34e-ec0f5f2b218e"] =
            "How long Windows waits before ending an app's 'execution required' power request (which keeps the system from idling/sleeping) if the app doesn't renew it. Mainly a safety timeout so a misbehaving app can't keep the system awake forever; rarely needs adjustment.",
        ["c36f0eb4-2988-4a70-8eee-0884fc2c2433"] =
            "How long Windows waits to batch up ('coalesce') timers and I/O events from background apps before processing them together, instead of handling each one immediately. Longer coalescing windows let the CPU stay idle longer between wake-ups (better battery life); shorter windows make background timers more precise/responsive.",
        ["c42b79aa-aa3a-484b-a98f-2cf32aa90a28"] =
            "How precisely (in milliseconds) idle-related timers are allowed to be batched together while the processor is idle. Works alongside the I/O coalescing timeout to reduce how often the CPU wakes from a low-power state purely to service small background timers, improving battery life at a very slight cost to timer precision.",
        ["d502f7ee-1dc7-4efd-a55d-f04b6f5c0545"] =
            "Whether the system is allowed to use its deepest available idle/'Deep Sleep' state while otherwise appearing active (part of Modern Standby-style idle power management). Disabling this can help diagnose hardware/driver issues that only show up in the deepest idle states, at the cost of noticeably higher idle power draw.",

        // SUB_INTSTEER (48672f38-7a9a-4bb2-8bf8-3d85be19de4e) — Interrupt Steering Settings
        ["2bfc24f9-5ea2-4801-8213-3dbae01aa39d"] =
            "Controls which CPU core(s) handle hardware interrupts (e.g. from network or storage controllers) as system load changes. The default lets Windows route interrupts intelligently to balance responsiveness and power use; pinning interrupts to specific processors is an advanced troubleshooting/tuning option rarely needed outside of server or latency-sensitive scenarios.",

        // SUB_DESKTOP_BACKGROUND (0d7dbae2-4294-402a-ba8e-26777e8488cd)
        ["309dce9b-bef4-4119-9921-a851fb12f0f4"] =
            "Whether a desktop background slideshow keeps advancing to the next picture on a timer, or pauses. Pausing the slideshow on battery saves a small amount of power and avoids unnecessary disk/CPU activity from repeatedly loading new wallpaper images.",

        // SUB_GRAPHICS (5fb4938d-1ee8-4b0f-9a3c-5036b0ab995c) — switchable/hybrid graphics
        ["dd848b2a-8a5d-4451-9ae2-39cd41658f6c"] =
            "On laptops with both an integrated and a discrete/dedicated GPU (hybrid graphics), this sets the system-wide default preference for which GPU apps run on: 'None' lets Windows/the app decide, 'Low Power' prefers the integrated GPU for better battery life. Individual apps can still override this in Windows Settings > Display > Graphics. Has no effect on systems with only one GPU.",
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
