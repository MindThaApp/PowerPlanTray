namespace PowerPlanTray.Core.Models;

public enum AutomationTrigger
{
    Battery,
    AC,
    AppRunning,
    Timed,
}

public class AutoSwitchRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public AutomationTrigger Trigger { get; set; }
    public Guid TargetPlanGuid { get; set; }
    public string? AppExecutableName { get; set; }
    public string? Name { get; set; }
    public bool Enabled { get; set; } = true;
}
