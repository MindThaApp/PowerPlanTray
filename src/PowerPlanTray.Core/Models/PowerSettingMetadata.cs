namespace PowerPlanTray.Core.Models;

public sealed record PowerSettingChoice(uint Value, string Name);

public sealed record PowerSettingMetadata(
    uint? Minimum,
    uint? Maximum,
    uint? Increment,
    string? Units,
    IReadOnlyList<PowerSettingChoice> Choices);

public sealed record CommonPowerSetting(Guid SubgroupGuid, Guid SettingGuid);
