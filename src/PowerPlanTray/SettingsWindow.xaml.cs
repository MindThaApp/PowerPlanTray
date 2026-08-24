using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PowerPlanTray.Core.Services;
using PowerPlanTray.Core;
using PowerPlanTray.Core.Models;
using PowerPlanTray.Core.Content;
using Windows.ApplicationModel;
using Windows.Storage;
using Windows.Storage.Pickers;
using System.Text.Json;
using System.Runtime.InteropServices;

namespace PowerPlanTray;

public sealed partial class SettingsWindow : Window
{
    private readonly AppSettingsService _appSettingsService;
    private readonly StartupService _startupService = new();
    private readonly PowerSchemeService _powerSchemeService;
    private readonly ElevationService _elevationService = new();
    private readonly PowerSourceMonitor _powerSourceMonitor;
    private readonly AutomationRuleEngine _automationRuleEngine;
    private IReadOnlyList<PowerScheme> _automationSchemes = Array.Empty<PowerScheme>();
    private bool _isInitializing;
    private bool _hasInitialized;
    private bool _allAdvancedSettingsLoaded;
    private readonly List<(Guid SubgroupGuid, Guid SettingGuid)> _allAdvancedSettings = new();
    private List<AdvancedSettingsProfile> _advancedProfiles = new();
    private const string NoLaymanDescription = "No plain-language explanation written yet for this setting.";
    private static readonly Guid ProcessorSubgroupGuid = new("54533251-82be-4824-96c1-47b60b740d00");
    private static readonly Guid ProcessorMaximumStateGuid = new("bc5038f7-23e0-4960-96da-33abaf5935ec");

    public event EventHandler? PowerPlansChanged;

    public event EventHandler? AutomationSettingsChanged;

    public SettingsWindow(
        AppSettingsService appSettingsService,
        PowerSchemeService powerSchemeService,
        PowerSourceMonitor powerSourceMonitor,
        AutomationRuleEngine automationRuleEngine)
    {
        _appSettingsService = appSettingsService;
        _powerSchemeService = powerSchemeService;
        _powerSourceMonitor = powerSourceMonitor;
        _automationRuleEngine = automationRuleEngine;
        InitializeComponent();
        Activated += OnWindowActivated;
        _automationRuleEngine.TimedSwitchStateChanged += OnTimedSwitchStateChanged;
        Closed += OnWindowClosed;
    }

    private async void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated) return;
        if (!_hasInitialized)
        {
            _isInitializing = true;
            try
            {
                StartWithWindowsCheckBox.IsChecked = await _startupService.IsEnabledAsync();
            }
            catch (Exception)
            {
                // The startup task extension may not be registered/queryable in
                // every environment (e.g. dev-signed sideloads) - don't let an
                // async-void exception here take down the whole app.
                StartWithWindowsCheckBox.IsChecked = false;
            }
            LaunchBehaviorRadioButtons.SelectedIndex = _appSettingsService.StartHidden ? 0 : 1;
            _isInitializing = false;
            RefreshPowerPlans();
            RefreshAutomationPage();
            InitializeAdvancedPlans();
            _hasInitialized = true;
        }
        RefreshCpuBoostState();
    }

    public void RefreshActiveSchemeSettings()
    {
        RefreshCpuBoostState();
        if (AdvancedPlanComboBox.ItemsSource is IEnumerable<PowerScheme> schemes)
        {
            Guid active = _powerSchemeService.GetActiveSchemeGuid();
            AdvancedPlanComboBox.SelectedItem = schemes.FirstOrDefault(scheme => scheme.Guid == active);
        }
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        string? tag = (args.SelectedItemContainer as NavigationViewItem)?.Tag as string;
        GeneralPage.Visibility = tag == "General" ? Visibility.Visible : Visibility.Collapsed;
        PowerPlansPage.Visibility = tag == "PowerPlans" ? Visibility.Visible : Visibility.Collapsed;
        AutomationPage.Visibility = tag == "Automation" ? Visibility.Visible : Visibility.Collapsed;
        AdvancedPage.Visibility = tag == "Advanced" ? Visibility.Visible : Visibility.Collapsed;

        if (tag == "PowerPlans")
        {
            RefreshPowerPlans();
        }
        else if (tag == "Automation")
        {
            RefreshAutomationPage();
        }
        else if (tag == "Advanced")
        {
            RefreshAdvancedSettings();
        }
    }

    private void RefreshAutomationPage()
    {
        _isInitializing = true;
        try
        {
            _automationSchemes = _powerSchemeService.GetAllSchemes();
            BatteryAcSection.Visibility = _powerSourceMonitor.HasBattery ? Visibility.Visible : Visibility.Collapsed;
            BatteryAcToggle.IsOn = _appSettingsService.AutoSwitchBatteryAcEnabled;
            PopulatePlanComboBox(BatteryPlanComboBox, _appSettingsService.BatteryPlanGuid);
            PopulatePlanComboBox(AcPlanComboBox, _appSettingsService.AcPlanGuid);
            PopulatePlanComboBox(AppRulePlanComboBox, null);
            PopulatePlanComboBox(TimedPlanComboBox, _automationRuleEngine.CurrentTimedSwitch?.TargetPlanGuid);
            RefreshAppRules();
            RefreshTimedSwitchStatus();
        }
        catch (Exception ex)
        {
            AppRuleStatusText.Text = $"Couldn't read automation settings: {ex.Message}";
        }
        finally
        {
            _isInitializing = false;
        }
    }

    private void PopulatePlanComboBox(ComboBox comboBox, Guid? selectedGuid)
    {
        comboBox.ItemsSource = _automationSchemes;
        comboBox.SelectedItem = _automationSchemes.FirstOrDefault(scheme => scheme.Guid == selectedGuid);
        if (comboBox.SelectedItem is null && _automationSchemes.Count > 0) comboBox.SelectedIndex = 0;
    }

    private void RefreshAppRules()
    {
        AppRulesPanel.Children.Clear();
        foreach (AutoSwitchRule rule in _appSettingsService.GetAutomationRules()
            .Where(rule => rule.Trigger == AutomationTrigger.AppRunning))
        {
            string planName = _automationSchemes.FirstOrDefault(scheme => scheme.Guid == rule.TargetPlanGuid)?.Name
                ?? "Unavailable plan";
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var enabled = new CheckBox
            {
                Content = $"{rule.AppExecutableName} → {planName}",
                IsChecked = rule.Enabled,
                Tag = rule.Id,
                Width = 380,
            };
            enabled.Click += OnAppRuleEnabledClick;
            var remove = new Button { Content = "Remove", Tag = rule.Id };
            remove.Click += OnRemoveAppRuleClick;
            row.Children.Add(enabled);
            row.Children.Add(remove);
            AppRulesPanel.Children.Add(row);
        }
    }

    private void OnBatteryAcToggleToggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        _appSettingsService.AutoSwitchBatteryAcEnabled = BatteryAcToggle.IsOn;
        AutomationConfigurationChanged(applyPowerState: BatteryAcToggle.IsOn);
    }

    private void OnBatteryPlanSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        _appSettingsService.BatteryPlanGuid = (BatteryPlanComboBox.SelectedItem as PowerScheme)?.Guid;
        AutomationConfigurationChanged(applyPowerState: _powerSourceMonitor.IsOnBattery);
    }

    private void OnAcPlanSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        _appSettingsService.AcPlanGuid = (AcPlanComboBox.SelectedItem as PowerScheme)?.Guid;
        AutomationConfigurationChanged(applyPowerState: !_powerSourceMonitor.IsOnBattery);
    }

    private void OnAddAppRuleClick(object sender, RoutedEventArgs e)
    {
        string executable = AppExecutableTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(executable) || AppRulePlanComboBox.SelectedItem is not PowerScheme plan)
        {
            AppRuleStatusText.Text = "Enter an executable name and choose a power plan.";
            return;
        }
        if (!executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) executable += ".exe";
        List<AutoSwitchRule> rules = _appSettingsService.GetAutomationRules();
        rules.Add(new AutoSwitchRule
        {
            Trigger = AutomationTrigger.AppRunning,
            AppExecutableName = executable,
            TargetPlanGuid = plan.Guid,
        });
        SaveRules(rules);
        AppExecutableTextBox.Text = string.Empty;
        AppRuleStatusText.Text = string.Empty;
    }

    private void OnAppRuleEnabledClick(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: Guid id } checkBox) return;
        List<AutoSwitchRule> rules = _appSettingsService.GetAutomationRules();
        AutoSwitchRule? rule = rules.FirstOrDefault(candidate => candidate.Id == id);
        if (rule is null) return;
        rule.Enabled = checkBox.IsChecked == true;
        SaveRules(rules);
    }

    private void OnRemoveAppRuleClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid id }) return;
        List<AutoSwitchRule> rules = _appSettingsService.GetAutomationRules();
        rules.RemoveAll(rule => rule.Id == id);
        SaveRules(rules);
    }

    private void SaveRules(List<AutoSwitchRule> rules)
    {
        _appSettingsService.SetAutomationRules(rules);
        _automationRuleEngine.RefreshConfiguration();
        RefreshAppRules();
        AutomationSettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private async void OnBrowseInstalledAppsClick(object sender, RoutedEventArgs e)
    {
        AppRuleStatusText.Text = "Finding Start Menu apps…";
        IReadOnlyList<InstalledAppChoice> apps;
        try
        {
            apps = await Task.Run(FindInstalledApps);
        }
        catch (Exception ex)
        {
            AppRuleStatusText.Text = $"Couldn't read installed apps: {ex.Message}";
            return;
        }

        if (apps.Count == 0)
        {
            AppRuleStatusText.Text = "No Start Menu shortcuts targeting executable files were found.";
            return;
        }

        var search = new AutoSuggestBox { PlaceholderText = "Search installed apps" };
        var list = new ListView
        {
            ItemsSource = apps,
            SelectionMode = ListViewSelectionMode.Single,
            Height = 360,
            MinWidth = 520,
        };
        search.TextChanged += (_, args) =>
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
            string query = search.Text.Trim();
            list.ItemsSource = string.IsNullOrEmpty(query)
                ? apps
                : apps.Where(app => app.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                    || app.ExecutableName.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        };

        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(search);
        content.Children.Add(list);
        var dialog = new ContentDialog
        {
            Title = "Browse installed apps",
            Content = content,
            PrimaryButtonText = "Select",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
        };
        ContentDialogResult result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && list.SelectedItem is InstalledAppChoice selected)
        {
            AppExecutableTextBox.Text = selected.ExecutableName;
            AppRuleStatusText.Text = $"Selected {selected.DisplayName} ({selected.ExecutableName}).";
        }
        else if (result == ContentDialogResult.Primary)
        {
            AppRuleStatusText.Text = "Select an app from the list.";
        }
        else
        {
            AppRuleStatusText.Text = string.Empty;
        }
    }

    private static IReadOnlyList<InstalledAppChoice> FindInstalledApps()
    {
        string[] roots =
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
        };
        var results = new Dictionary<string, InstalledAppChoice>(StringComparer.OrdinalIgnoreCase);
        Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null) return Array.Empty<InstalledAppChoice>();

        object? shellObject = null;
        try
        {
            shellObject = Activator.CreateInstance(shellType);
            dynamic shell = shellObject!;
            foreach (string root in roots.Where(Directory.Exists))
            {
                foreach (string shortcutPath in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
                {
                    string displayName = Path.GetFileNameWithoutExtension(shortcutPath);
                    if (IsUnhelpfulShortcut(displayName)) continue;

                    object? shortcutObject = null;
                    try
                    {
                        shortcutObject = shell.CreateShortcut(shortcutPath);
                        dynamic shortcut = shortcutObject;
                        string targetPath = Environment.ExpandEnvironmentVariables((string)shortcut.TargetPath);
                        if (!string.Equals(Path.GetExtension(targetPath), ".exe", StringComparison.OrdinalIgnoreCase)) continue;
                        string executableName = Path.GetFileName(targetPath);
                        if (string.IsNullOrWhiteSpace(executableName)) continue;
                        results.TryAdd($"{displayName}\0{executableName}", new InstalledAppChoice(displayName, executableName));
                    }
                    catch (COMException) { }
                    finally
                    {
                        if (shortcutObject is not null && Marshal.IsComObject(shortcutObject))
                            Marshal.FinalReleaseComObject(shortcutObject);
                    }
                }
            }
        }
        finally
        {
            if (shellObject is not null && Marshal.IsComObject(shellObject))
                Marshal.FinalReleaseComObject(shellObject);
        }

        return results.Values.OrderBy(app => app.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private static bool IsUnhelpfulShortcut(string name) =>
        new[] { "uninstall", "help", "readme", "documentation", "manual" }
            .Any(term => name.Contains(term, StringComparison.OrdinalIgnoreCase));

    private async void OnBrowseExecutableClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".exe");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            StorageFile? file = await picker.PickSingleFileAsync();
            if (file is null) return;
            AppExecutableTextBox.Text = file.Name;
            AppRuleStatusText.Text = $"Selected {Path.GetFileNameWithoutExtension(file.Name)} ({file.Name}).";
        }
        catch (Exception ex)
        {
            AppRuleStatusText.Text = $"Couldn't open the executable picker: {ex.Message}";
        }
    }

    private sealed record InstalledAppChoice(string DisplayName, string ExecutableName)
    {
        public override string ToString() => $"{DisplayName} — {ExecutableName}";
    }

    private async void OnApplyTimedSwitchClick(object sender, RoutedEventArgs e)
    {
        if (TimedPlanComboBox.SelectedItem is not PowerScheme plan) return;
        TimeSpan duration = TimedDurationRadioButtons.SelectedIndex switch
        {
            1 => TimeSpan.FromHours(1),
            2 => TimeSpan.FromHours(2),
            3 => TimeSpan.FromHours(4),
            _ => TimeSpan.FromMinutes(30),
        };
        await ApplyTimedSwitchAsync(plan, duration);
    }

    private async void OnApplyCustomTimedSwitchClick(object sender, RoutedEventArgs e)
    {
        TimedSwitchErrorText.Text = string.Empty;
        if (TimedPlanComboBox.SelectedItem is not PowerScheme plan)
        {
            TimedSwitchErrorText.Text = "Choose a power plan first.";
            return;
        }

        double hours = CustomHoursNumberBox.Value;
        double minutes = CustomMinutesNumberBox.Value;
        if (double.IsNaN(hours) || double.IsNaN(minutes) || hours < 0 || minutes < 0)
        {
            TimedSwitchErrorText.Text = "Enter a duration greater than zero.";
            return;
        }

        TimeSpan duration;
        try { duration = TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes); }
        catch (OverflowException)
        {
            TimedSwitchErrorText.Text = "The custom duration is too large.";
            return;
        }
        if (duration <= TimeSpan.Zero)
        {
            TimedSwitchErrorText.Text = "Enter a duration greater than zero.";
            return;
        }

        await ApplyTimedSwitchAsync(plan, duration);
    }

    private async Task ApplyTimedSwitchAsync(PowerScheme plan, TimeSpan duration)
    {
        TimedSwitchErrorText.Text = string.Empty;
        try { await _automationRuleEngine.ApplyTimedSwitchAsync(plan.Guid, duration); }
        catch (Exception ex) { TimedSwitchErrorText.Text = $"Couldn't apply the temporary plan: {ex.Message}"; }
    }

    private void OnCancelTimedSwitchClick(object sender, RoutedEventArgs e) => _automationRuleEngine.CancelTimedSwitch();

    private void OnTimedSwitchStateChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(RefreshTimedSwitchStatus);
    }

    private void RefreshTimedSwitchStatus()
    {
        TimedSwitchInfo? info = _automationRuleEngine.CurrentTimedSwitch;
        TimedSwitchStatusPanel.Visibility = info is null ? Visibility.Collapsed : Visibility.Visible;
        if (info is null) return;
        string planName = _automationSchemes.FirstOrDefault(scheme => scheme.Guid == info.TargetPlanGuid)?.Name ?? "Temporary plan";
        int minutes = Math.Max(1, (int)Math.Ceiling(info.Remaining.TotalMinutes));
        TimedSwitchStatusText.Text = $"{planName} for {minutes} more minute{(minutes == 1 ? string.Empty : "s")}";
    }

    private void AutomationConfigurationChanged(bool applyPowerState)
    {
        _automationRuleEngine.RefreshConfiguration(applyPowerState && _appSettingsService.AutoSwitchBatteryAcEnabled);
        AutomationSettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _automationRuleEngine.TimedSwitchStateChanged -= OnTimedSwitchStateChanged;
    }

    private void InitializeAdvancedPlans()
    {
        try
        {
            IReadOnlyList<PowerScheme> schemes = _powerSchemeService.GetAllSchemes();
            Guid active = _powerSchemeService.GetActiveSchemeGuid();
            AdvancedPlanComboBox.ItemsSource = schemes;
            AdvancedPlanComboBox.SelectedItem = schemes.FirstOrDefault(scheme => scheme.Guid == active);
        }
        catch (Exception ex) { AdvancedStatusText.Text = $"Couldn't read power plans: {ex.Message}"; }
    }

    private void OnAdvancedPlanSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_hasInitialized)
        {
            _allAdvancedSettingsLoaded = false;
            _allAdvancedSettings.Clear();
            AllAdvancedSettingsPanel.Children.Clear();
            AllAdvancedSection.Visibility = Visibility.Collapsed;
            ShowAllAdvancedButton.Content = "Show all advanced settings";
            RefreshAdvancedSettings();
        }
    }

    private void RefreshAdvancedSettings()
    {
        if (AdvancedPlanComboBox.SelectedItem is not PowerScheme scheme) return;
        AdvancedSettingsPanel.Children.Clear();
        AdvancedStatusText.Text = string.Empty;
        try
        {
            foreach (IGrouping<Guid, CommonPowerSetting> subgroup in SettingDescriptions.CommonSettings.GroupBy(setting => setting.SubgroupGuid))
            {
                var group = new StackPanel { Spacing = 10 };
                string subgroupName;
                try { subgroupName = _powerSchemeService.GetSubgroupName(scheme.Guid, subgroup.Key); }
                catch { subgroupName = subgroup.Key.ToString(); }
                group.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(subgroupName) ? subgroup.Key.ToString() : subgroupName, Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"] });
                foreach (CommonPowerSetting setting in subgroup)
                    group.Children.Add(CreateSettingRowOrUnavailable(scheme.Guid, setting.SubgroupGuid, setting.SettingGuid));
                AdvancedSettingsPanel.Children.Add(group);
            }
        }
        catch (Exception ex) { AdvancedStatusText.Text = $"Couldn't read advanced settings: {ex.Message}"; }
    }

    private FrameworkElement CreateSettingRowOrUnavailable(Guid schemeGuid, Guid subgroupGuid, Guid settingGuid)
    {
        try
        {
            string name = _powerSchemeService.GetSettingName(schemeGuid, subgroupGuid, settingGuid);
            return CreateSettingRow(schemeGuid, subgroupGuid, settingGuid,
                string.IsNullOrWhiteSpace(name) ? settingGuid.ToString() : name);
        }
        catch (Exception ex)
        {
            return new TextBlock { Text = $"{settingGuid}: unavailable on this PC ({ex.Message})", TextWrapping = TextWrapping.Wrap };
        }
    }

    private async void OnShowAllAdvancedClick(object sender, RoutedEventArgs e)
    {
        if (AllAdvancedSection.Visibility == Visibility.Visible)
        {
            AllAdvancedSection.Visibility = Visibility.Collapsed;
            ShowAllAdvancedButton.Content = "Show all advanced settings";
            return;
        }

        AllAdvancedSection.Visibility = Visibility.Visible;
        ShowAllAdvancedButton.Content = "Hide all advanced settings";
        if (_allAdvancedSettingsLoaded || AdvancedPlanComboBox.SelectedItem is not PowerScheme scheme) return;
        AdvancedStatusText.Text = "Loading all advanced settings...";
        await Task.Yield();
        try
        {
            foreach ((Guid subgroupGuid, string subgroupName) in _powerSchemeService.GetSubgroups(scheme.Guid))
            {
                var group = new StackPanel { Spacing = 10 };
                group.Children.Add(new TextBlock { Text = subgroupName, Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"] });
                foreach ((Guid settingGuid, string settingName) in _powerSchemeService.GetSettings(scheme.Guid, subgroupGuid))
                {
                    _allAdvancedSettings.Add((subgroupGuid, settingGuid));
                    group.Children.Add(CreateSettingRow(scheme.Guid, subgroupGuid, settingGuid, settingName));
                }
                AllAdvancedSettingsPanel.Children.Add(group);
            }
            _allAdvancedSettingsLoaded = true;
            _advancedProfiles = await _appSettingsService.GetAdvancedSettingsProfilesAsync();
            AdvancedProfileComboBox.ItemsSource = _advancedProfiles;
            AdvancedStatusText.Text = $"Loaded {_allAdvancedSettings.Count} advanced settings.";
        }
        catch (Exception ex) { AdvancedStatusText.Text = $"Couldn't read advanced settings: {ex.Message}"; }
    }

    private async void OnRestoreWindowsDefaultsClick(object sender, RoutedEventArgs e) =>
        await SetVisibilityAsync(SettingDescriptions.CommonSettings.Select(s => (s.SubgroupGuid, s.SettingGuid)), false, "Restoring common Windows visibility");

    private async void OnEnableAllAdvancedClick(object sender, RoutedEventArgs e) =>
        await SetVisibilityAsync(_allAdvancedSettings, false, "Enabling all settings");

    private async void OnDisableAllAdvancedClick(object sender, RoutedEventArgs e) =>
        await SetVisibilityAsync(_allAdvancedSettings, true, "Disabling all settings");

    private async Task<bool> SetVisibilityAsync(IEnumerable<(Guid SubgroupGuid, Guid SettingGuid)> settings, bool hidden, string operation)
    {
        var targets = settings.Distinct().ToArray();
        AdvancedStatusText.Text = $"{operation} (0/{targets.Length})...";
        bool succeeded = await _elevationService.SetSettingsHiddenAsync(targets, hidden);
        AdvancedStatusText.Text = succeeded ? $"{operation} complete ({targets.Length}/{targets.Length})."
            : _elevationService.LastOperationWasCancelled ? "Administrator permission was cancelled." : $"{operation} failed.";
        return succeeded;
    }

    private AdvancedSettingsProfile CaptureProfile(string name)
    {
        if (AdvancedPlanComboBox.SelectedItem is not PowerScheme scheme) throw new InvalidOperationException("Select a power plan first.");
        return new AdvancedSettingsProfile
        {
            Name = name,
            SavedAt = DateTime.Now,
            Settings = _allAdvancedSettings.Select(setting => new AdvancedSettingSnapshot
            {
                SubgroupGuid = setting.SubgroupGuid,
                SettingGuid = setting.SettingGuid,
                Hidden = _powerSchemeService.IsSettingHidden(setting.SubgroupGuid, setting.SettingGuid),
                AcValue = _powerSchemeService.GetACValue(scheme.Guid, setting.SubgroupGuid, setting.SettingGuid),
                DcValue = _powerSchemeService.GetDCValue(scheme.Guid, setting.SubgroupGuid, setting.SettingGuid),
            }).ToList(),
        };
    }

    private async Task<string?> PromptForProfileNameAsync()
    {
        var input = new TextBox { Header = "Profile name", PlaceholderText = "My power settings" };
        var dialog = new ContentDialog { Title = "Save profile", Content = input, PrimaryButtonText = "Save", CloseButtonText = "Cancel", XamlRoot = Content.XamlRoot };
        return await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(input.Text) ? input.Text.Trim() : null;
    }

    private async void OnSaveProfileClick(object sender, RoutedEventArgs e)
    {
        string? name = await PromptForProfileNameAsync();
        if (name is null) return;
        try
        {
            AdvancedSettingsProfile profile = CaptureProfile(name);
            _advancedProfiles.RemoveAll(existing => string.Equals(existing.Name, name, StringComparison.OrdinalIgnoreCase));
            _advancedProfiles.Add(profile);
            await _appSettingsService.SetAdvancedSettingsProfilesAsync(_advancedProfiles);
            AdvancedProfileComboBox.ItemsSource = null;
            AdvancedProfileComboBox.ItemsSource = _advancedProfiles;
            AdvancedProfileComboBox.SelectedItem = profile;
            AdvancedStatusText.Text = $"Saved profile '{name}' with {profile.Settings.Count} settings.";
        }
        catch (Exception ex) { AdvancedStatusText.Text = $"Couldn't save profile: {ex.Message}"; }
    }

    private async void OnLoadProfileClick(object sender, RoutedEventArgs e)
    {
        if (AdvancedProfileComboBox.SelectedItem is AdvancedSettingsProfile profile) await ApplyProfileAsync(profile);
        else AdvancedStatusText.Text = "Choose a saved profile first.";
    }

    private async Task ApplyProfileAsync(AdvancedSettingsProfile profile)
    {
        if (AdvancedPlanComboBox.SelectedItem is not PowerScheme scheme) return;
        var available = _allAdvancedSettings.ToHashSet();
        var applicable = profile.Settings.Where(s => available.Contains((s.SubgroupGuid, s.SettingGuid))).ToArray();
        int applied = 0;
        foreach (AdvancedSettingSnapshot snapshot in applicable)
        {
            try
            {
                _powerSchemeService.SetACValue(scheme.Guid, snapshot.SubgroupGuid, snapshot.SettingGuid, snapshot.AcValue);
                _powerSchemeService.SetDCValue(scheme.Guid, snapshot.SubgroupGuid, snapshot.SettingGuid, snapshot.DcValue);
                applied++;
            }
            catch { }
        }
        bool visibilityApplied = await _elevationService.SetSettingsHiddenAsync(
            applicable.Select(s => (s.SubgroupGuid, s.SettingGuid, s.Hidden)));
        int skipped = profile.Settings.Count - applied;
        AdvancedStatusText.Text = visibilityApplied
            ? $"Applied {applied} settings from '{profile.Name}'; skipped {skipped}."
            : $"Applied values for {applied} settings, but couldn't apply all visibility states; skipped {skipped}.";
    }

    private async void OnExportProfileClick(object sender, RoutedEventArgs e)
    {
        string? name = await PromptForProfileNameAsync();
        if (name is null) return;
        try
        {
            AdvancedSettingsProfile profile = CaptureProfile(name);
            var picker = new FileSavePicker { SuggestedFileName = name };
            picker.FileTypeChoices.Add("JSON profile", new List<string> { ".json" });
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            StorageFile? file = await picker.PickSaveFileAsync();
            if (file is null) return;
            await FileIO.WriteTextAsync(file, JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true }));
            AdvancedStatusText.Text = $"Saved {profile.Settings.Count} settings to {file.Name}.";
        }
        catch (Exception ex) { AdvancedStatusText.Text = $"Couldn't export profile: {ex.Message}"; }
    }

    private async void OnImportProfileClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".json");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            StorageFile? file = await picker.PickSingleFileAsync();
            if (file is null) return;
            AdvancedSettingsProfile? profile = JsonSerializer.Deserialize<AdvancedSettingsProfile>(await FileIO.ReadTextAsync(file));
            if (profile is null || profile.Settings is null) throw new InvalidDataException("The file is not a valid advanced-settings profile.");
            await ApplyProfileAsync(profile);
        }
        catch (Exception ex) { AdvancedStatusText.Text = $"Couldn't import profile: {ex.Message}"; }
    }

    private FrameworkElement CreateSettingRow(Guid schemeGuid, Guid subgroupGuid, Guid settingGuid, string settingName)
    {
        var panel = new StackPanel { Spacing = 6, Padding = new Thickness(12) };
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        heading.Children.Add(new TextBlock { Text = settingName, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center, MaxWidth = 650, TextWrapping = TextWrapping.Wrap });
        var info = new Button { Content = "i", Width = 32, Height = 32, CornerRadius = new CornerRadius(16), Padding = new Thickness(0) };
        info.Click += (_, _) => ShowSettingInfo(info, subgroupGuid, settingGuid);
        heading.Children.Add(info);
        panel.Children.Add(heading);

        PowerSettingMetadata metadata = _powerSchemeService.GetSettingMetadata(subgroupGuid, settingGuid);
        var values = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        values.Children.Add(CreateValueEditor("Plugged in", schemeGuid, subgroupGuid, settingGuid, true, metadata));
        values.Children.Add(CreateValueEditor("On battery", schemeGuid, subgroupGuid, settingGuid, false, metadata));
        panel.Children.Add(values);

        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var unhide = new CheckBox { Content = "Shown in Windows Power Options", IsChecked = !_powerSchemeService.IsSettingHidden(subgroupGuid, settingGuid) };
        var status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        unhide.Click += async (_, _) =>
        {
            unhide.IsEnabled = false;
            bool succeeded = await _elevationService.SetSettingHiddenAsync(subgroupGuid, settingGuid, unhide.IsChecked != true);
            status.Text = succeeded ? "Updated." : _elevationService.LastOperationWasCancelled
                ? "Administrator permission was cancelled." : "Couldn't update visibility.";
            unhide.IsChecked = !_powerSchemeService.IsSettingHidden(subgroupGuid, settingGuid);
            unhide.IsEnabled = true;
        };
        footer.Children.Add(unhide);
        footer.Children.Add(status);
        panel.Children.Add(footer);
        return panel;
    }

    private FrameworkElement CreateValueEditor(string header, Guid schemeGuid, Guid subgroupGuid, Guid settingGuid,
        bool ac, PowerSettingMetadata metadata)
    {
        uint current = ac ? _powerSchemeService.GetACValue(schemeGuid, subgroupGuid, settingGuid)
                          : _powerSchemeService.GetDCValue(schemeGuid, subgroupGuid, settingGuid);
        if (metadata.Choices.Count > 0)
        {
            var combo = new ComboBox { Header = header, ItemsSource = metadata.Choices, DisplayMemberPath = "Name", Width = 220 };
            combo.SelectedItem = metadata.Choices.FirstOrDefault(choice => choice.Value == current);
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem is PowerSettingChoice choice) SetValue(ac, schemeGuid, subgroupGuid, settingGuid, choice.Value);
            };
            return combo;
        }

        var number = new NumberBox { Header = string.IsNullOrWhiteSpace(metadata.Units) ? header : $"{header} ({metadata.Units})",
            Value = current, Width = 220, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        if (metadata.Minimum is uint min) number.Minimum = min;
        if (metadata.Maximum is uint max) number.Maximum = max;
        if (metadata.Increment is uint increment && increment > 0) number.SmallChange = increment;
        number.ValueChanged += (_, args) =>
        {
            if (!double.IsNaN(args.NewValue) && args.NewValue >= 0 && args.NewValue <= uint.MaxValue)
                SetValue(ac, schemeGuid, subgroupGuid, settingGuid, checked((uint)Math.Round(args.NewValue)));
        };
        return number;
    }

    private void SetValue(bool ac, Guid schemeGuid, Guid subgroupGuid, Guid settingGuid, uint value)
    {
        try
        {
            if (ac) _powerSchemeService.SetACValue(schemeGuid, subgroupGuid, settingGuid, value);
            else _powerSchemeService.SetDCValue(schemeGuid, subgroupGuid, settingGuid, value);
            AdvancedStatusText.Text = "Setting applied.";
            RefreshCpuBoostState();
        }
        catch (Exception ex) { AdvancedStatusText.Text = $"Couldn't apply setting: {ex.Message}"; }
    }

    private void ShowSettingInfo(Button target, Guid subgroupGuid, Guid settingGuid)
    {
        var content = new StackPanel { Spacing = 8, MaxWidth = 480 };
        content.Children.Add(new TextBlock { Text = "Windows description", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        content.Children.Add(new TextBlock { Text = _powerSchemeService.GetSettingDescription(subgroupGuid, settingGuid) ?? "No Windows description is available.", TextWrapping = TextWrapping.Wrap });
        content.Children.Add(new TextBlock { Text = "In plain terms", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
        content.Children.Add(new TextBlock { Text = SettingDescriptions.GetLaymanDescription(settingGuid) ?? NoLaymanDescription, TextWrapping = TextWrapping.Wrap });
        target.Flyout = new Flyout { Content = content };
        target.Flyout.ShowAt(target);
    }

    private void RefreshCpuBoostState()
    {
        try
        {
            Guid active = _powerSchemeService.GetActiveSchemeGuid();
            uint value = _powerSchemeService.GetACValue(active, ProcessorSubgroupGuid, ProcessorMaximumStateGuid);
            _isInitializing = true;
            DisableCpuBoostCheckBox.IsChecked = value <= 99;
            _isInitializing = false;
            CpuBoostStatusText.Text = string.Empty;
        }
        catch (Exception ex) { CpuBoostStatusText.Text = $"Couldn't read CPU boost state: {ex.Message}"; _isInitializing = false; }
    }

    private async void OnDisableCpuBoostClick(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        try
        {
            Guid active = _powerSchemeService.GetActiveSchemeGuid();
            uint current = _powerSchemeService.GetACValue(active, ProcessorSubgroupGuid, ProcessorMaximumStateGuid);
            if (DisableCpuBoostCheckBox.IsChecked == true)
            {
                if (current == 100) _powerSchemeService.SetACValue(active, ProcessorSubgroupGuid, ProcessorMaximumStateGuid, 99);
            }
            else if (current <= 99)
            {
                var dialog = new ContentDialog { Title = "Re-enable CPU boost?",
                    Content = $"This will increase the maximum processor state from {current}% to 100% to re-enable CPU boost. Continue?",
                    PrimaryButtonText = "Yes", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close,
                    XamlRoot = Content.XamlRoot };
                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                    _powerSchemeService.SetACValue(active, ProcessorSubgroupGuid, ProcessorMaximumStateGuid, 100);
                else
                {
                    _isInitializing = true;
                    DisableCpuBoostCheckBox.IsChecked = true;
                    _isInitializing = false;
                }
            }
        }
        catch (Exception ex) { CpuBoostStatusText.Text = $"Couldn't update CPU boost: {ex.Message}"; RefreshCpuBoostState(); }
    }

    private void RefreshPowerPlans()
    {
        IReadOnlyList<PowerScheme> schemes;
        try
        {
            schemes = _powerSchemeService.GetAllSchemes();
        }
        catch (Exception ex)
        {
            PowerPlanStatusText.Text = $"Couldn't read power plans: {ex.Message}";
            return;
        }

        var installed = schemes.Select(scheme => scheme.Guid).ToHashSet();
        DefaultPlansPanel.Children.Clear();
        foreach ((Guid guid, string name) in WellKnownSchemes.DefaultSchemes)
        {
            bool isInstalled = installed.Contains(guid);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            row.Children.Add(new TextBlock { Text = name, Width = 180, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock
            {
                Text = isInstalled ? "Installed" : "Not installed",
                Width = 100,
                VerticalAlignment = VerticalAlignment.Center,
            });
            var button = new Button { Content = isInstalled ? "Disable" : "Enable", Tag = guid };
            button.Click += isInstalled ? OnDisablePlanClick : OnEnablePlanClick;
            row.Children.Add(button);
            DefaultPlansPanel.Children.Add(row);
        }

        IReadOnlySet<Guid> visibleGuids = _appSettingsService.GetVisiblePlanGuids();
        bool unfiltered = visibleGuids.Count == 0;
        VisibilityPlansPanel.Children.Clear();
        foreach (PowerScheme scheme in schemes)
        {
            var checkBox = new CheckBox
            {
                Content = scheme.Name,
                Tag = scheme.Guid,
                IsChecked = unfiltered || visibleGuids.Contains(scheme.Guid),
            };
            checkBox.Click += OnPlanVisibilityClick;
            VisibilityPlansPanel.Children.Add(checkBox);
        }
    }

    private async void OnEnablePlanClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid guid } button) return;
        button.IsEnabled = false;
        bool succeeded = await _elevationService.DuplicateSchemeAsync(guid);
        PowerPlanStatusText.Text = succeeded
            ? "The power plan was enabled."
            : _elevationService.LastOperationWasCancelled
                ? "Administrator permission was cancelled."
                : "Couldn't enable this power plan.";
        RefreshPowerPlans();
        if (succeeded) PowerPlansChanged?.Invoke(this, EventArgs.Empty);
    }

    private async void OnDisablePlanClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid guid } button) return;
        button.IsEnabled = false;
        bool succeeded = await _elevationService.DeleteSchemeAsync(guid);
        PowerPlanStatusText.Text = succeeded
            ? "The power plan was disabled."
            : _elevationService.LastOperationWasCancelled
                ? "Administrator permission was cancelled."
                : "Couldn't remove this plan (it may be the active plan).";
        RefreshPowerPlans();
        if (succeeded) PowerPlansChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPlanVisibilityClick(object sender, RoutedEventArgs e)
    {
        var visibleGuids = VisibilityPlansPanel.Children
            .OfType<CheckBox>()
            .Where(checkBox => checkBox.IsChecked == true && checkBox.Tag is Guid)
            .Select(checkBox => (Guid)checkBox.Tag)
            .ToArray();
        _appSettingsService.SetVisiblePlanGuids(visibleGuids);
        PowerPlansChanged?.Invoke(this, EventArgs.Empty);
    }

    private async void OnStartWithWindowsClick(object sender, RoutedEventArgs e)
    {
        if (_isInitializing)
        {
            return;
        }

        try
        {
            if (StartWithWindowsCheckBox.IsChecked == true)
            {
                StartupTaskState state = await _startupService.RequestEnableAsync();
                bool enabled = state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
                StartWithWindowsCheckBox.IsChecked = enabled;
                _appSettingsService.StartWithWindows = enabled;

                if (!enabled)
                {
                    var dialog = new ContentDialog
                    {
                        Title = "Startup permission needed",
                        Content = "Enable Power Plan Tray in Windows Settings > Apps > Startup, then try again.",
                        CloseButtonText = "OK",
                        XamlRoot = Content.XamlRoot,
                    };
                    await dialog.ShowAsync();
                }
            }
            else
            {
                _startupService.Disable();
                _appSettingsService.StartWithWindows = false;
            }
        }
        catch (Exception)
        {
            // The startup task extension may not be queryable in every
            // environment (e.g. dev-signed sideloads) - don't let an
            // async-void exception here take down the whole app.
            StartWithWindowsCheckBox.IsChecked = _appSettingsService.StartWithWindows;
        }
    }

    private void OnLaunchBehaviorSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitializing && LaunchBehaviorRadioButtons.SelectedIndex >= 0)
        {
            _appSettingsService.StartHidden = LaunchBehaviorRadioButtons.SelectedIndex == 0;
        }
    }
}
