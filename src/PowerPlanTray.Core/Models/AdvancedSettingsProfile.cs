namespace PowerPlanTray.Core.Models;

public sealed class AdvancedSettingsProfile
{
    public string Name { get; set; } = string.Empty;
    public DateTime SavedAt { get; set; }
    public List<AdvancedSettingSnapshot> Settings { get; set; } = new();
}

public sealed class AdvancedSettingSnapshot
{
    public Guid SubgroupGuid { get; set; }
    public Guid SettingGuid { get; set; }
    public bool Hidden { get; set; }
    public uint AcValue { get; set; }
    public uint DcValue { get; set; }
}
