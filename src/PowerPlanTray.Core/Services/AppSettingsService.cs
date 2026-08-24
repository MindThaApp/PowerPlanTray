using Windows.Storage;
using PowerPlanTray.Core.Models;
using System.Text.Json;

namespace PowerPlanTray.Core.Services;

public sealed class AppSettingsService
{
    private const string StartWithWindowsKey = nameof(StartWithWindows);
    private const string StartHiddenKey = nameof(StartHidden);
    private const string VisiblePlanGuidsKey = "VisiblePlanGuids";
    private const string AutoSwitchBatteryAcEnabledKey = nameof(AutoSwitchBatteryAcEnabled);
    private const string BatteryPlanGuidKey = nameof(BatteryPlanGuid);
    private const string AcPlanGuidKey = nameof(AcPlanGuid);
    private const string AutomationRulesKey = "AutomationRules";
    private const string AdvancedProfilesFileName = "advanced-settings-profiles.json";
    private const string ThemeKey = nameof(Theme);
    private const string PopupSizeKey = nameof(PopupSize);
    private const string PopupTextSizeKey = nameof(PopupTextSize);
    private const string SettingsWindowSizeKey = nameof(SettingsWindowSize);
    private const string NavigationPanePinnedKey = nameof(NavigationPanePinned);

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

    public AppTheme Theme
    {
        get => GetEnum(ThemeKey, AppTheme.FollowWindows);
        set => _localSettings.Values[ThemeKey] = value.ToString();
    }

    public UiSize PopupSize
    {
        get => GetEnum(PopupSizeKey, UiSize.Medium);
        set => _localSettings.Values[PopupSizeKey] = value.ToString();
    }

    public UiSize PopupTextSize
    {
        get => GetEnum(PopupTextSizeKey, UiSize.Medium);
        set => _localSettings.Values[PopupTextSizeKey] = value.ToString();
    }

    public UiSize SettingsWindowSize
    {
        get => GetEnum(SettingsWindowSizeKey, UiSize.Medium);
        set => _localSettings.Values[SettingsWindowSizeKey] = value.ToString();
    }

    public bool NavigationPanePinned
    {
        get => GetBoolean(NavigationPanePinnedKey, defaultValue: false);
        set => _localSettings.Values[NavigationPanePinnedKey] = value;
    }

    public bool AutoSwitchBatteryAcEnabled
    {
        get => GetBoolean(AutoSwitchBatteryAcEnabledKey, defaultValue: false);
        set => _localSettings.Values[AutoSwitchBatteryAcEnabledKey] = value;
    }

    public Guid? BatteryPlanGuid
    {
        get => GetNullableGuid(BatteryPlanGuidKey);
        set => SetNullableGuid(BatteryPlanGuidKey, value);
    }

    public Guid? AcPlanGuid
    {
        get => GetNullableGuid(AcPlanGuidKey);
        set => SetNullableGuid(AcPlanGuidKey, value);
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

    public List<AutoSwitchRule> GetAutomationRules()
    {
        if (!_localSettings.Values.TryGetValue(AutomationRulesKey, out object? value) ||
            value is not string json || string.IsNullOrWhiteSpace(json))
        {
            return new List<AutoSwitchRule>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<AutoSwitchRule>>(json) ?? new List<AutoSwitchRule>();
        }
        catch (JsonException)
        {
            return new List<AutoSwitchRule>();
        }
    }

    public void SetAutomationRules(List<AutoSwitchRule> rules) =>
        _localSettings.Values[AutomationRulesKey] = JsonSerializer.Serialize(rules);

    public async Task<List<AdvancedSettingsProfile>> GetAdvancedSettingsProfilesAsync()
    {
        try
        {
            StorageFile file = await ApplicationData.Current.LocalFolder.GetFileAsync(AdvancedProfilesFileName);
            return JsonSerializer.Deserialize<List<AdvancedSettingsProfile>>(await FileIO.ReadTextAsync(file)) ?? new();
        }
        catch (FileNotFoundException) { return new(); }
        catch (JsonException) { return new(); }
    }

    public async Task SetAdvancedSettingsProfilesAsync(IEnumerable<AdvancedSettingsProfile> profiles)
    {
        StorageFile file = await ApplicationData.Current.LocalFolder.CreateFileAsync(
            AdvancedProfilesFileName, CreationCollisionOption.ReplaceExisting);
        await FileIO.WriteTextAsync(file, JsonSerializer.Serialize(profiles, new JsonSerializerOptions { WriteIndented = true }));
    }

    private bool GetBoolean(string key, bool defaultValue) =>
        _localSettings.Values.TryGetValue(key, out object? value) && value is bool boolean
            ? boolean
            : defaultValue;

    private T GetEnum<T>(string key, T defaultValue) where T : struct, Enum =>
        _localSettings.Values.TryGetValue(key, out object? value) &&
        value is string serialized && Enum.TryParse(serialized, out T result)
            ? result
            : defaultValue;

    private Guid? GetNullableGuid(string key) =>
        _localSettings.Values.TryGetValue(key, out object? value) &&
        value is string serialized && Guid.TryParse(serialized, out Guid guid)
            ? guid
            : null;

    private void SetNullableGuid(string key, Guid? value)
    {
        if (value.HasValue)
        {
            _localSettings.Values[key] = value.Value.ToString("D");
        }
        else
        {
            _localSettings.Values.Remove(key);
        }
    }
}
