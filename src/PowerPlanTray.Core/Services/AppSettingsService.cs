using Windows.Storage;

namespace PowerPlanTray.Core.Services;

public sealed class AppSettingsService
{
    private const string StartWithWindowsKey = nameof(StartWithWindows);
    private const string StartHiddenKey = nameof(StartHidden);
    private const string VisiblePlanGuidsKey = "VisiblePlanGuids";

    private readonly ApplicationDataContainer _localSettings =
        ApplicationData.Current.LocalSettings;

    public bool StartWithWindows
    {
        get => GetBoolean(StartWithWindowsKey, defaultValue: false);
        set => _localSettings.Values[StartWithWindowsKey] = value;
    }

    public bool StartHidden
    {
        get => GetBoolean(StartHiddenKey, defaultValue: false);
        set => _localSettings.Values[StartHiddenKey] = value;
    }

    /// <summary>
    /// Gets the configured tray-visible plans. An empty set means that no filter
    /// is configured and callers should show every currently installed plan.
    /// </summary>
    public IReadOnlySet<Guid> GetVisiblePlanGuids()
    {
        if (!_localSettings.Values.TryGetValue(VisiblePlanGuidsKey, out object? value) ||
            value is not string serialized || string.IsNullOrWhiteSpace(serialized))
        {
            return new HashSet<Guid>();
        }

        return serialized.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => Guid.TryParse(value, out Guid guid) ? guid : Guid.Empty)
            .Where(guid => guid != Guid.Empty)
            .ToHashSet();
    }

    public void SetVisiblePlanGuids(IEnumerable<Guid> guids) =>
        _localSettings.Values[VisiblePlanGuidsKey] =
            string.Join(',', guids.Distinct().Select(guid => guid.ToString("D")));

    private bool GetBoolean(string key, bool defaultValue) =>
        _localSettings.Values.TryGetValue(key, out object? value) && value is bool boolean
            ? boolean
            : defaultValue;
}
