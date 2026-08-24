using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PowerPlanTray.Core.Services;
using PowerPlanTray.Core;
using PowerPlanTray.Core.Models;
using Windows.ApplicationModel;

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
        Activated += OnFirstActivated;
        _automationRuleEngine.TimedSwitchStateChanged += OnTimedSwitchStateChanged;
        Closed += OnWindowClosed;
        // TODO(phase5): add the Advanced NavigationViewItem and page.
    }

    private async void OnFirstActivated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= OnFirstActivated;
        _isInitializing = true;
        StartWithWindowsCheckBox.IsChecked = await _startupService.IsEnabledAsync();
        LaunchBehaviorRadioButtons.SelectedIndex = _appSettingsService.StartHidden ? 0 : 1;
        _isInitializing = false;
        RefreshPowerPlans();
        RefreshAutomationPage();
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        string? tag = (args.SelectedItemContainer as NavigationViewItem)?.Tag as string;
        GeneralPage.Visibility = tag == "General" ? Visibility.Visible : Visibility.Collapsed;
        PowerPlansPage.Visibility = tag == "PowerPlans" ? Visibility.Visible : Visibility.Collapsed;
        AutomationPage.Visibility = tag == "Automation" ? Visibility.Visible : Visibility.Collapsed;

        if (tag == "PowerPlans")
        {
            RefreshPowerPlans();
        }
        else if (tag == "Automation")
        {
            RefreshAutomationPage();
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
        try { await _automationRuleEngine.ApplyTimedSwitchAsync(plan.Guid, duration); }
        catch (Exception ex) { AppRuleStatusText.Text = $"Couldn't apply the temporary plan: {ex.Message}"; }
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

    private void OnLaunchBehaviorSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitializing && LaunchBehaviorRadioButtons.SelectedIndex >= 0)
        {
            _appSettingsService.StartHidden = LaunchBehaviorRadioButtons.SelectedIndex == 0;
        }
    }
}
