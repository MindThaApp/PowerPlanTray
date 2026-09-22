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
    /// <summary>
    /// How many seconds a condition must hold continuously before the plan switches.
    /// For CPU-threshold rules (SystemCpuBelow/Above, ProcessCpuBelow/Above) this is the CPU debounce
    /// duration; a value &lt;= 0 falls back to the app's original fixed ~9-second debounce so rules
    /// persisted before this field existed keep their old behavior unchanged.
    /// For AppRunning rules this is the minimum time the app must have been running before switching
    /// (0 = switch immediately, matching pre-existing behavior), or, when <see cref="AppCpuGateEnabled"/>
    /// is set, how long the app's own CPU usage must stay above <see cref="CpuThresholdPercent"/> first.
    /// The default of 0 (rather than the legacy CPU-rule fallback of 9) is deliberate: it's what makes
    /// old AppRunning rules that predate this field keep switching immediately.
    /// </summary>
    public double SustainedSeconds { get; set; }
    /// <summary>
    /// Opt-in flag for AppRunning rules only: when true, the rule also requires the app's own CPU usage
    /// to stay above <see cref="CpuThresholdPercent"/> for <see cref="SustainedSeconds"/> before switching
    /// (evaluated the same way ProcessCpuAbove rules are). Kept as an explicit flag rather than inferring
    /// from CpuThresholdPercent alone because AppRunning rules persisted before this feature already carry
    /// a leftover CpuThresholdPercent value (historically unused, defaulted to 15) that must not be
    /// mistaken for an intentionally-configured CPU gate.
    /// </summary>
    public bool AppCpuGateEnabled { get; set; }
}
