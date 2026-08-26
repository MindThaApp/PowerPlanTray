namespace PowerPlanTray.Core.Models;

public sealed class AdvancedSettingVisibility
{
    public Guid SubgroupGuid { get; set; }
    public Guid SettingGuid { get; set; }
    public bool Hidden { get; set; }
}
