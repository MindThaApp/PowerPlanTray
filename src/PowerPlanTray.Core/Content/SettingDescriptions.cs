namespace PowerPlanTray.Core.Content;

// Layman descriptions are authored separately (per-setting research) and will populate this
// dictionary in a follow-up change. Until then, GetLaymanDescription returns null and callers
// must fall back to just the Windows-provided description.
public static class SettingDescriptions
{
    private static readonly Dictionary<Guid, string> ByGuid = new();

    public static string? GetLaymanDescription(Guid settingGuid) =>
        ByGuid.TryGetValue(settingGuid, out var text) ? text : null;
}
