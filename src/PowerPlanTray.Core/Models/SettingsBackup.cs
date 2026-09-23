using System.Text.Json;

namespace PowerPlanTray.Core.Models;

public sealed class SettingsBackup
{
    public int Version { get; set; } = 1;
    // Preserve all persisted keys, including legacy StartHidden, FirstLaunchUtc,
    // serialized rules and visible-plan GUIDs. Absent keys retain their defaults.
    public Dictionary<string, JsonElement> LocalSettings { get; set; } = new();
    public List<AdvancedSettingsProfile>? AdvancedSettingsProfiles { get; set; }
    public List<AdvancedSettingVisibility>? AdvancedVisibilityBaseline { get; set; }
}
