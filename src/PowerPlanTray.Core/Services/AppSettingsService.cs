using Windows.Storage;
using PowerPlanTray.Core.Models;
using System.Text.Json;

namespace PowerPlanTray.Core.Services;

public sealed class AppSettingsService
{
    private const string StartWithWindowsKey = nameof(StartWithWindows);
    private const string StartHiddenKey = nameof(StartHidden);
    private const string StartHiddenOnStartupKey = nameof(StartHiddenOnStartup);
    private const string StartHiddenOnManualLaunchKey = nameof(StartHiddenOnManualLaunch);
    private const string VisiblePlanGuidsKey = "VisiblePlanGuids";
    private const string AutoSwitchBatteryAcEnabledKey = nameof(AutoSwitchBatteryAcEnabled);
    private const string BatteryPlanGuidKey = nameof(BatteryPlanGuid);
    private const string AcPlanGuidKey = nameof(AcPlanGuid);
    private const string AutomationRulesKey = "AutomationRules";
    private const string AdvancedProfilesFileName = "advanced-settings-profiles.json";
    private const string AdvancedVisibilityBaselineFileName = "advanced-settings-visibility-baseline.json";
    private const string ThemeKey = nameof(Theme);
    private const string PopupSizeKey = nameof(PopupSize);
    private const string PopupTextSizeKey = nameof(PopupTextSize);
    private const string SettingsWindowSizeKey = nameof(SettingsWindowSize);
    private const string NavigationPanePinnedKey = nameof(NavigationPanePinned);
    private const string KeepWindowOnTopKey = nameof(KeepWindowOnTop);
    private const string TrayIconModeKey = nameof(TrayIconMode);
    private const string TrayIconGaugeMetricKey = nameof(TrayIconGaugeMetric);
    private const string TrayIconGaugeColorKey = nameof(TrayIconGaugeColor);
    private const string DefaultTrayIconGaugeColor = "#7A3FD4";
    private const string AdvancedWarningAcknowledgedKey = nameof(AdvancedWarningAcknowledged);
    private const string FirstLaunchUtcKey = nameof(FirstLaunchUtc);

    private readonly ApplicationDataContainer _localSettings =
        ApplicationData.Current.LocalSettings;

    private bool _backupReady;

    public DateTimeOffset FirstLaunchUtc
    {
        get
        {
            if (_localSettings.Values.TryGetValue(FirstLaunchUtcKey, out object? value) &&
                value is string serialized &&
                DateTimeOffset.TryParseExact(serialized, "O", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out DateTimeOffset firstLaunchUtc))
            {
                return firstLaunchUtc.ToUniversalTime();
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            _localSettings.Values[FirstLaunchUtcKey] = now.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
            WriteBackupSafe();
            return now;
        }
    }

    public bool StartWithWindows
    {
        get => GetBoolean(StartWithWindowsKey, defaultValue: false);
        set { _localSettings.Values[StartWithWindowsKey] = value; WriteBackupSafe(); }
    }

    public bool StartHidden
    {
        get => GetBoolean(StartHiddenKey, defaultValue: false);
        set { _localSettings.Values[StartHiddenKey] = value; WriteBackupSafe(); }
    }

    public bool StartHiddenOnStartup
    {
        get => GetBoolean(StartHiddenOnStartupKey, StartHidden);
        set { _localSettings.Values[StartHiddenOnStartupKey] = value; WriteBackupSafe(); }
    }

    public bool StartHiddenOnManualLaunch
    {
        get => GetBoolean(StartHiddenOnManualLaunchKey, StartHidden);
        set { _localSettings.Values[StartHiddenOnManualLaunchKey] = value; WriteBackupSafe(); }
    }

    public AppTheme Theme
    {
        get => GetEnum(ThemeKey, AppTheme.FollowWindows);
        set { _localSettings.Values[ThemeKey] = value.ToString(); WriteBackupSafe(); }
    }

    public UiSize PopupSize
    {
        get => GetEnum(PopupSizeKey, UiSize.Medium);
        set { _localSettings.Values[PopupSizeKey] = value.ToString(); WriteBackupSafe(); }
    }

    public UiSize PopupTextSize
    {
        get => GetEnum(PopupTextSizeKey, UiSize.Medium);
        set { _localSettings.Values[PopupTextSizeKey] = value.ToString(); WriteBackupSafe(); }
    }

    public UiSize SettingsWindowSize
    {
        get => GetEnum(SettingsWindowSizeKey, UiSize.Medium);
        set { _localSettings.Values[SettingsWindowSizeKey] = value.ToString(); WriteBackupSafe(); }
    }

    public bool NavigationPanePinned
    {
        get => GetBoolean(NavigationPanePinnedKey, defaultValue: false);
        set { _localSettings.Values[NavigationPanePinnedKey] = value; WriteBackupSafe(); }
    }

    public bool KeepWindowOnTop
    {
        get => GetBoolean(KeepWindowOnTopKey, defaultValue: false);
        set { _localSettings.Values[KeepWindowOnTopKey] = value; WriteBackupSafe(); }
    }

    public bool AdvancedWarningAcknowledged
    {
        get => GetBoolean(AdvancedWarningAcknowledgedKey, defaultValue: false);
        set { _localSettings.Values[AdvancedWarningAcknowledgedKey] = value; WriteBackupSafe(); }
    }

    public TrayIconMode TrayIconMode
    {
        get => GetEnum(TrayIconModeKey, Models.TrayIconMode.Static);
        set { _localSettings.Values[TrayIconModeKey] = value.ToString(); WriteBackupSafe(); }
    }

    public TrayGaugeMetric TrayIconGaugeMetric
    {
        get => GetEnum(TrayIconGaugeMetricKey, TrayGaugeMetric.Cpu);
        set { _localSettings.Values[TrayIconGaugeMetricKey] = value.ToString(); WriteBackupSafe(); }
    }

    /// <summary>Hex ARGB/RGB string (e.g. "#7A3FD4") for the Gauge tray icon's accent color.</summary>
    public string TrayIconGaugeColor
    {
        get => GetString(TrayIconGaugeColorKey, DefaultTrayIconGaugeColor);
        set { _localSettings.Values[TrayIconGaugeColorKey] = value; WriteBackupSafe(); }
    }

    public bool AutoSwitchBatteryAcEnabled
    {
        get => GetBoolean(AutoSwitchBatteryAcEnabledKey, defaultValue: false);
        set { _localSettings.Values[AutoSwitchBatteryAcEnabledKey] = value; WriteBackupSafe(); }
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

    public void SetVisiblePlanGuids(IEnumerable<Guid> guids)
    {
        _localSettings.Values[VisiblePlanGuidsKey] =
            string.Join(',', guids.Distinct().Select(guid => guid.ToString("D")));
        WriteBackupSafe();
    }

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

    public void SetAutomationRules(List<AutoSwitchRule> rules)
    {
        _localSettings.Values[AutomationRulesKey] = JsonSerializer.Serialize(rules);
        WriteBackupSafe();
    }

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
        WriteBackupSafe();
    }

    public async Task<List<AdvancedSettingVisibility>?> GetAdvancedVisibilityBaselineAsync()
    {
        try
        {
            StorageFile file = await ApplicationData.Current.LocalFolder.GetFileAsync(AdvancedVisibilityBaselineFileName);
            return JsonSerializer.Deserialize<List<AdvancedSettingVisibility>>(await FileIO.ReadTextAsync(file))
                ?? throw new JsonException("The advanced-settings visibility baseline is empty.");
        }
        catch (FileNotFoundException) { return null; }
    }

    public async Task<bool> TrySetAdvancedVisibilityBaselineAsync(IEnumerable<AdvancedSettingVisibility> settings)
    {
        StorageFile file;
        try
        {
            file = await ApplicationData.Current.LocalFolder.CreateFileAsync(
                AdvancedVisibilityBaselineFileName, CreationCollisionOption.FailIfExists);
        }
        catch (Exception)
        {
            return false;
        }

        await FileIO.WriteTextAsync(file, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        WriteBackupSafe();
        return true;
    }

    private static string BackupPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PowerPlanManagerBackup", Windows.ApplicationModel.Package.Current.Id.FamilyName,
        "settings-backup.json");

    // Call before consumers, including the lazy FirstLaunchUtc initializer.
    public Task RestoreFromBackupIfNeededAsync()
    {
        if (_backupReady) return Task.CompletedTask;
        try
        {
            string folder = ApplicationData.Current.LocalFolder.Path;
            string profilesPath = Path.Combine(folder, AdvancedProfilesFileName);
            string baselinePath = Path.Combine(folder, AdvancedVisibilityBaselineFileName);
            // Missing rules/profiles alone is normal. Any live setting or collection
            // proves this is not an empty installation and prevents restoration.
            bool empty = !_localSettings.Values.ContainsKey(AutomationRulesKey)
                && _localSettings.Values.Count == 0
                && !File.Exists(profilesPath) && !File.Exists(baselinePath);
            if (empty && File.Exists(BackupPath))
            {
                SettingsBackup backup = JsonSerializer.Deserialize<SettingsBackup>(File.ReadAllText(BackupPath))
                    ?? throw new JsonException("Empty settings backup.");
                if (backup.Version != 1 || backup.LocalSettings is null || backup.LocalSettings.Count == 0)
                    throw new JsonException("Invalid settings backup.");
                // Validate every value before writing. Persisted settings are bools or
                // strings, including enum names, GUIDs, dates and serialized rules.
                var values = backup.LocalSettings.ToDictionary(pair => pair.Key, pair =>
                    pair.Value.ValueKind switch
                    {
                        JsonValueKind.String => (object)pair.Value.GetString()!,
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        _ => throw new JsonException("Unsupported settings value.")
                    });
                try
                {
                    if (backup.AdvancedSettingsProfiles is not null)
                        WriteJsonAtomic(profilesPath, backup.AdvancedSettingsProfiles);
                    if (backup.AdvancedVisibilityBaseline is not null)
                        WriteJsonAtomic(baselinePath, backup.AdvancedVisibilityBaseline);
                    foreach (var pair in values) _localSettings.Values[pair.Key] = pair.Value;
                }
                catch
                {
                    // Roll back only our writes into the previously empty destination.
                    _localSettings.Values.Clear();
                    File.Delete(profilesPath);
                    File.Delete(baselinePath);
                    throw;
                }
            }
            _backupReady = true;
            _ = FirstLaunchUtc;
            WriteBackupSafe(); // Protect existing users without waiting for an edit.
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Settings restore failed: {ex}");
            // Preserve an unreadable backup instead of replacing it with defaults.
        }
        return Task.CompletedTask;
    }

    private void WriteBackupSafe()
    {
        if (!_backupReady) return;
        try
        {
            string folder = ApplicationData.Current.LocalFolder.Path;
            var backup = new SettingsBackup
            {
                LocalSettings = _localSettings.Values.ToDictionary(
                    pair => pair.Key, pair => JsonSerializer.SerializeToElement(pair.Value)),
                AdvancedSettingsProfiles = ReadCollection<AdvancedSettingsProfile>(Path.Combine(folder, AdvancedProfilesFileName)),
                AdvancedVisibilityBaseline = ReadCollection<AdvancedSettingVisibility>(Path.Combine(folder, AdvancedVisibilityBaselineFileName))
            };
            WriteJsonAtomic(BackupPath, backup);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Settings backup failed: {ex}");
        }
    }

    private static List<T>? ReadCollection<T>(string path) => File.Exists(path)
        ? JsonSerializer.Deserialize<List<T>>(File.ReadAllText(path))
            ?? throw new JsonException("Empty settings collection.")
        : null;

    private static void WriteJsonAtomic<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private bool GetBoolean(string key, bool defaultValue) =>
        _localSettings.Values.TryGetValue(key, out object? value) && value is bool boolean
            ? boolean
            : defaultValue;

    private string GetString(string key, string defaultValue) =>
        _localSettings.Values.TryGetValue(key, out object? value) && value is string serialized && !string.IsNullOrWhiteSpace(serialized)
            ? serialized
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
        WriteBackupSafe();
    }
}
