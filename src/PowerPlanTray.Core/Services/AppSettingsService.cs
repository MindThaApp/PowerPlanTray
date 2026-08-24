using Windows.Storage;

namespace PowerPlanTray.Core.Services;

public sealed class AppSettingsService
{
    private const string StartWithWindowsKey = nameof(StartWithWindows);
    private const string StartHiddenKey = nameof(StartHidden);

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

    private bool GetBoolean(string key, bool defaultValue) =>
        _localSettings.Values.TryGetValue(key, out object? value) && value is bool boolean
            ? boolean
            : defaultValue;
}
