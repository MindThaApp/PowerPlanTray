namespace PowerPlanTray.Core.Models;

public enum AutomationTrigger
{
    Battery,
    AC,
    AppRunning,
    Timed,
    SystemCpuBelow,
    SystemCpuAbove,
    ProcessCpuBelow,
    ProcessCpuAbove,
}

public class AutoSwitchRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public AutomationTrigger Trigger { get; set; }
    public Guid TargetPlanGuid { get; set; }
    public string? AppExecutableName { get; set; }
    public double CpuThresholdPercent { get; set; } = 15;
    /// <summary>Ordering for CPU rules. Lower values have higher priority.</summary>
    public int Priority { get; set; }
    public string? Name { get; set; }
    public bool Enabled { get; set; } = true;
}
