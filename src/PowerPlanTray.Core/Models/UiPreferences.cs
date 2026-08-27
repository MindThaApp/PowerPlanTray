namespace PowerPlanTray.Core.Models;

public enum AppTheme
{
    FollowWindows,
    Light,
    Dark,
    OledBlack,
}

public enum UiSize
{
    Small,
    Medium,
    Large,
}

public enum TrayIconMode
{
    Static,
    CpuPercentText,
    CpuBarChart,
    PowerPlanAbbreviation,
    Gauge,
}

/// <summary>A Task Manager Performance-tab-style system metric, each expressed as a 0-100 percentage.</summary>
public enum TrayGaugeMetric
{
    Cpu,
    Memory,
    Disk,
    Network,
    Gpu,
}
