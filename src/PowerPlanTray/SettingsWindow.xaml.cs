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
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.Win32;
using System.Reflection;
using Windows.Foundation;

namespace PowerPlanTray;

public sealed partial class SettingsWindow : Window
{
    private static string L(string key) => Localization.Get(key);
    private static string F(string key, params object?[] args) => Localization.Format(key, args);
    private readonly AppSettingsService _appSettingsService;
    private readonly StartupService _startupService = new();
    private readonly PowerSchemeService _powerSchemeService;
    private readonly ElevationService _elevationService = new();
    private readonly PowerSourceMonitor _powerSourceMonitor;
    private readonly AutomationRuleEngine _automationRuleEngine;
    private IReadOnlyList<PowerScheme> _automationSchemes = Array.Empty<PowerScheme>();
    private bool _isInitializing;
    private bool _hasInitialized;
    private readonly List<(Guid SubgroupGuid, Guid SettingGuid)> _allAdvancedSettings = new();
    private readonly HashSet<(bool IsHiddenSection, Guid SubgroupGuid)> _expandedAdvancedCategories = new();
    private readonly HashSet<(Guid SubgroupGuid, Guid SettingGuid)> _expandedAdvancedSettings = new();
    private readonly HashSet<StackPanel> _pendingAdvancedReflows = new();
    private List<AdvancedSettingsProfile> _advancedProfiles = new();
    private static string NoLaymanDescription => L("NoLaymanDescription");
    private static readonly Guid ProcessorSubgroupGuid = new("54533251-82be-4824-96c1-47b60b740d00");
    private static readonly Guid ProcessorMaximumStateGuid = new("bc5038f7-23e0-4960-96da-33abaf5935ec");

    public event EventHandler? PowerPlansChanged;

    public event EventHandler? AutomationSettingsChanged;
    public event EventHandler? UiPreferencesChanged;

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
        WindowRoot.FlowDirection = Localization.FlowDirection;
        Title = L("SettingsWindowTitle");
        AboutVersionText.Text = F("VersionFormat", GetAppVersion());
        LaunchBehaviorRadioButtons.ItemsSource = new[] { L("StartHiddenInTray"), L("ShowThisWindow") };
        SystemCpuDirectionComboBox.ItemsSource = new[] { L("Below"), L("Above") };
        AppTriggerTypeComboBox.ItemsSource = new[] { L("AppIsRunning"), L("AppCpuLoad") };
        AppCpuDirectionComboBox.ItemsSource = new[] { L("Below"), L("Above") };
        TimedDurationRadioButtons.ItemsSource = new[] { L("ThirtyMinutes"), L("OneHour"), L("TwoHours"), L("FourHours") };
        ThemeComboBox.ItemsSource = new[] { L("FollowWindows"), L("Light"), L("Dark"), L("OledBlack") };
        TrayIconModeComboBox.ItemsSource = new[] { L("Static"), L("CpuPercentage"), L("CpuBarChart"), L("PowerPlanAbbreviation") };
        PopupSizeComboBox.ItemsSource = SettingsWindowSizeComboBox.ItemsSource = PopupTextSizeComboBox.ItemsSource = new[] { L("Small"), L("Medium"), L("Large") };
        AppTriggerTypeComboBox.SelectionChanged += OnAppTriggerTypeChanged;
        WindowRoot.Loaded += (_, _) => ApplyPinnedPaneState();
        SettingsNavigationView.PaneOpening += OnNavigationPaneOpening;
        SettingsNavigationView.PaneClosing += OnNavigationPaneClosing;
        SettingsNavigationView.PaneClosed += OnNavigationPaneClosed;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        ConfigureNavigationPane();
        InitializeUiPreferences();
        ApplyUiPreferences();
        ApplyWindowSize();
        Activated += OnWindowActivated;
        _automationRuleEngine.TimedSwitchStateChanged += OnTimedSwitchStateChanged;
        Closed += OnWindowClosed;
    }

    private void InitializeUiPreferences()
    {
        _isInitializing = true;
        ThemeComboBox.SelectedIndex = (int)_appSettingsService.Theme;
        TrayIconModeComboBox.SelectedIndex = (int)_appSettingsService.TrayIconMode;
        PopupSizeComboBox.SelectedIndex = (int)_appSettingsService.PopupSize;
        PopupTextSizeComboBox.SelectedIndex = (int)_appSettingsService.PopupTextSize;
        SettingsWindowSizeComboBox.SelectedIndex = (int)_appSettingsService.SettingsWindowSize;
        _isInitializing = false;
    }

    public void ApplyUiPreferences()
    {
        ElementTheme theme = _appSettingsService.Theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark or AppTheme.OledBlack => ElementTheme.Dark,
            _ => IsWindowsLightTheme() ? ElementTheme.Light : ElementTheme.Dark,
        };
        WindowRoot.RequestedTheme = theme;
        bool useSolidBlack = _appSettingsService.Theme == AppTheme.OledBlack;
        WindowRoot.Background = useSolidBlack
            ? new SolidColorBrush(Colors.Black)
            : new SolidColorBrush(Colors.Transparent);
        ApplyBackdrop(useSolidBlack);
        IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        AppWindow appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd));
        appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        appWindow.TitleBar.ButtonForegroundColor = theme == ElementTheme.Dark ? Colors.White : Colors.Black;
        appWindow.TitleBar.ButtonInactiveForegroundColor = theme == ElementTheme.Dark ? Colors.Gray : Colors.DarkGray;
    }

    private void ApplyWindowSize()
    {
        (int width, int height) = _appSettingsService.SettingsWindowSize switch
        {
            UiSize.Small => (760, 560),
            UiSize.Large => (1200, 850),
            _ => (960, 700),
        };
        IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        AppWindow appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd));
        appWindow.Resize(new SizeInt32(width, height));
    }

    private void ApplyBackdrop(bool useSolidBlack)
    {
        if (useSolidBlack)
        {
            SystemBackdrop = null;
            return;
        }
        try { SystemBackdrop = new MicaBackdrop { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.BaseAlt }; }
        catch { try { SystemBackdrop = new DesktopAcrylicBackdrop(); } catch { } }
    }

    private static bool IsWindowsLightTheme()
    {
        try
        {
            object? value = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
            return Convert.ToInt32(value) != 0;
        }
        catch { return Application.Current.RequestedTheme == ApplicationTheme.Light; }
    }

    private void ConfigureNavigationPane()
    {
        // Measure the real localized labels, then add the compact icon column
        // and standard item padding instead of retaining NavigationView's wide default.
        double longest = SettingsNavigationView.MenuItems.OfType<NavigationViewItem>()
            .Select(item => item.Content?.ToString() ?? string.Empty)
            .Select(label => { var text = new TextBlock { Text = label }; text.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity)); return text.DesiredSize.Width; })
            .Max();
        SettingsNavigationView.OpenPaneLength = Math.Ceiling(longest + SettingsNavigationView.CompactPaneLength + 36);
        PinPaneToggle.IsChecked = _appSettingsService.NavigationPanePinned;
        SettingsNavigationView.IsPaneOpen = _appSettingsService.NavigationPanePinned;
        SettingsNavigationView.IsPaneToggleButtonVisible = true;
    }

    private void OnPinPaneClick(object sender, RoutedEventArgs e)
    {
        bool pinned = PinPaneToggle.IsChecked == true;
        _appSettingsService.NavigationPanePinned = pinned;
        SettingsNavigationView.PaneDisplayMode = pinned ? NavigationViewPaneDisplayMode.Left : NavigationViewPaneDisplayMode.LeftCompact;
        SettingsNavigationView.IsPaneOpen = pinned;
        ToolTipService.SetToolTip(PinPaneToggle, pinned ? L("UnpinNavigationPane") : L("PinNavigationPaneOpen"));
    }

    private void ApplyPinnedPaneState()
    {
        bool pinned = _appSettingsService.NavigationPanePinned;
        PinPaneToggle.IsChecked = pinned;
        SettingsNavigationView.PaneDisplayMode = pinned ? NavigationViewPaneDisplayMode.Left : NavigationViewPaneDisplayMode.LeftCompact;
        SettingsNavigationView.IsPaneToggleButtonVisible = true;
        SettingsNavigationView.IsPaneOpen = pinned;
        ToolTipService.SetToolTip(PinPaneToggle, pinned ? L("UnpinNavigationPane") : L("PinNavigationPaneOpen"));
    }

    private void OnNavigationPaneOpening(NavigationView sender, object args)
    {
        sender.PaneDisplayMode = NavigationViewPaneDisplayMode.Left;
    }

    private void OnNavigationPaneClosing(NavigationView sender, NavigationViewPaneClosingEventArgs args)
    {
        if (_appSettingsService.NavigationPanePinned) args.Cancel = true;
    }

    private void OnNavigationPaneClosed(NavigationView sender, object args)
    {
        if (_appSettingsService.NavigationPanePinned)
        {
            sender.PaneDisplayMode = NavigationViewPaneDisplayMode.Left;
            sender.IsPaneOpen = true;
        }
        else
        {
            sender.PaneDisplayMode = NavigationViewPaneDisplayMode.LeftCompact;
        }
    }

    private void OnUiPreferenceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing || ThemeComboBox.SelectedIndex < 0 || TrayIconModeComboBox.SelectedIndex < 0 || PopupSizeComboBox.SelectedIndex < 0 ||
            PopupTextSizeComboBox.SelectedIndex < 0 || SettingsWindowSizeComboBox.SelectedIndex < 0) return;

        _appSettingsService.Theme = (AppTheme)ThemeComboBox.SelectedIndex;
        _appSettingsService.TrayIconMode = (TrayIconMode)TrayIconModeComboBox.SelectedIndex;
        _appSettingsService.PopupSize = (UiSize)PopupSizeComboBox.SelectedIndex;
        _appSettingsService.PopupTextSize = (UiSize)PopupTextSizeComboBox.SelectedIndex;
        _appSettingsService.SettingsWindowSize = (UiSize)SettingsWindowSizeComboBox.SelectedIndex;
        ApplyUiPreferences();
        if (ReferenceEquals(sender, SettingsWindowSizeComboBox)) ApplyWindowSize();
        UiPreferencesChanged?.Invoke(this, EventArgs.Empty);
    }

    private async void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated) return;
        ApplyUiPreferences();
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
        RefreshActiveSchemeSettings();
    }

    public void RefreshActiveSchemeSettings()
    {
        RefreshGeneralPlanSelector();
        RefreshCpuBoostState();
        if (AdvancedPlanComboBox.ItemsSource is IEnumerable<PowerScheme> schemes)
        {
            Guid active = _powerSchemeService.GetActiveSchemeGuid();
            AdvancedPlanComboBox.SelectedItem = schemes.FirstOrDefault(scheme => scheme.Guid == active);
        }
    }

    private async void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        string? tag = (args.SelectedItemContainer as NavigationViewItem)?.Tag as string;
        GeneralPage.Visibility = tag == "General" ? Visibility.Visible : Visibility.Collapsed;
        PowerPlansPage.Visibility = tag == "PowerPlans" ? Visibility.Visible : Visibility.Collapsed;
        AutomationPage.Visibility = tag == "Automation" ? Visibility.Visible : Visibility.Collapsed;
        AdvancedPage.Visibility = tag == "Advanced" ? Visibility.Visible : Visibility.Collapsed;
        ProfilesPage.Visibility = tag == "Profiles" ? Visibility.Visible : Visibility.Collapsed;
        UiPage.Visibility = tag == "UI" ? Visibility.Visible : Visibility.Collapsed;
        AboutPage.Visibility = tag == "About" ? Visibility.Visible : Visibility.Collapsed;

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
        else if (tag == "Profiles")
        {
            await InitializeProfilesAsync();
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
            PopulatePlanComboBox(SystemCpuPlanComboBox, null);
            PopulatePlanComboBox(TimedPlanComboBox, _automationRuleEngine.CurrentTimedSwitch?.TargetPlanGuid);
            EnsureCpuPriorities();
            RefreshSystemCpuRules();
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
            .Where(rule => rule.Trigger is AutomationTrigger.AppRunning or AutomationTrigger.ProcessCpuBelow or AutomationTrigger.ProcessCpuAbove)
            .OrderBy(rule => IsCpuTrigger(rule.Trigger) ? rule.Priority : int.MaxValue))
        {
            string planName = _automationSchemes.FirstOrDefault(scheme => scheme.Guid == rule.TargetPlanGuid)?.Name
                ?? "Unavailable plan";
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var enabled = new CheckBox
            {
                Content = rule.Trigger == AutomationTrigger.AppRunning
                    ? $"{rule.AppExecutableName} running → {planName}"
                    : $"Priority {rule.Priority}: {rule.AppExecutableName} CPU {(rule.Trigger == AutomationTrigger.ProcessCpuBelow ? "below" : "above")} {rule.CpuThresholdPercent:G}% → {planName}",
                IsChecked = rule.Enabled,
                Tag = rule.Id,
                Width = IsCpuTrigger(rule.Trigger) ? 300 : 380,
            };
            enabled.Click += OnAppRuleEnabledClick;
            var remove = new Button { Content = "Remove", Tag = rule.Id };
            remove.Click += OnRemoveAppRuleClick;
            row.Children.Add(enabled);
            AddPriorityButtons(row, rule);
            row.Children.Add(remove);
            AppRulesPanel.Children.Add(row);
        }
    }

    private static string GetAppVersion()
    {
        try
        {
            PackageVersion version = Package.Current.Id.Version;
            return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
        }
        catch
        {
            return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";
        }
    }

    private async void OnPrivacyPolicyClick(object sender, RoutedEventArgs e) =>
        await Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/ramin-azizi/PowerPlanTray/blob/master/PRIVACY.md"));

    private async void OnTermsOfUseClick(object sender, RoutedEventArgs e) =>
        await Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/ramin-azizi/PowerPlanTray/blob/master/TERMS.md"));

    private void RefreshGeneralPlanSelector()
    {
        try
        {
            IReadOnlyList<PowerScheme> schemes = _powerSchemeService.GetAllSchemes();
            Guid active = _powerSchemeService.GetActiveSchemeGuid();
            bool wasInitializing = _isInitializing;
            _isInitializing = true;
            GeneralPlanComboBox.ItemsSource = schemes;
            GeneralPlanComboBox.SelectedItem = schemes.FirstOrDefault(scheme => scheme.Guid == active);
            _isInitializing = wasInitializing;
        }
        catch (Exception ex)
        {
            CpuBoostStatusText.Text = $"Couldn't read power plans: {ex.Message}";
        }
    }

    private void OnGeneralPlanSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing || GeneralPlanComboBox.SelectedItem is not PowerScheme scheme) return;
        try
        {
            _powerSchemeService.SetActiveScheme(scheme.Guid);
            RefreshActiveSchemeSettings();
        }
        catch (Exception ex)
        {
            CpuBoostStatusText.Text = $"Couldn't switch power plans: {ex.Message}";
            RefreshActiveSchemeSettings();
        }
    }

    private void RefreshSystemCpuRules()
    {
        SystemCpuRulesPanel.Children.Clear();
        foreach (AutoSwitchRule rule in _appSettingsService.GetAutomationRules()
            .Where(rule => rule.Trigger is AutomationTrigger.SystemCpuBelow or AutomationTrigger.SystemCpuAbove)
            .OrderBy(rule => rule.Priority))
        {
            string planName = _automationSchemes.FirstOrDefault(scheme => scheme.Guid == rule.TargetPlanGuid)?.Name ?? "Unavailable plan";
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var enabled = new CheckBox
            {
                Content = $"Priority {rule.Priority}: CPU {(rule.Trigger == AutomationTrigger.SystemCpuBelow ? "below" : "above")} {rule.CpuThresholdPercent:G}% → {planName}",
                IsChecked = rule.Enabled,
                Tag = rule.Id,
                Width = 300,
            };
            enabled.Click += OnCpuRuleEnabledClick;
            row.Children.Add(enabled);
            AddPriorityButtons(row, rule);
            var remove = new Button { Content = "Remove", Tag = rule.Id };
            remove.Click += OnRemoveCpuRuleClick;
            row.Children.Add(remove);
            SystemCpuRulesPanel.Children.Add(row);
        }
    }

    private void AddPriorityButtons(StackPanel row, AutoSwitchRule rule)
    {
        if (!IsCpuTrigger(rule.Trigger)) return;
        List<AutoSwitchRule> cpuRules = GetCpuRulesInPriorityOrder(_appSettingsService.GetAutomationRules());
        int index = cpuRules.FindIndex(candidate => candidate.Id == rule.Id);
        var up = new Button { Content = "↑", Tag = rule.Id, IsEnabled = index > 0 };
        ToolTipService.SetToolTip(up, "Raise priority");
        up.Click += OnRaiseCpuRulePriorityClick;
        var down = new Button { Content = "↓", Tag = rule.Id, IsEnabled = index >= 0 && index < cpuRules.Count - 1 };
        ToolTipService.SetToolTip(down, "Lower priority");
        down.Click += OnLowerCpuRulePriorityClick;
        row.Children.Add(up);
        row.Children.Add(down);
    }

    private void OnCpuRuleEnabledClick(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: Guid id } checkBox) return;
        List<AutoSwitchRule> rules = _appSettingsService.GetAutomationRules();
        AutoSwitchRule? rule = rules.FirstOrDefault(candidate => candidate.Id == id);
        if (rule is null) return;
        rule.Enabled = checkBox.IsChecked == true;
        SaveRules(rules);
    }

    private void OnRemoveCpuRuleClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid id }) return;
        List<AutoSwitchRule> rules = _appSettingsService.GetAutomationRules();
        rules.RemoveAll(rule => rule.Id == id);
        SaveRules(rules);
    }

    private void OnRaiseCpuRulePriorityClick(object sender, RoutedEventArgs e) => MoveCpuRule(sender, -1);
    private void OnLowerCpuRulePriorityClick(object sender, RoutedEventArgs e) => MoveCpuRule(sender, 1);

    private void MoveCpuRule(object sender, int offset)
    {
        if (sender is not Button { Tag: Guid id }) return;
        List<AutoSwitchRule> rules = _appSettingsService.GetAutomationRules();
        NormalizeCpuPriorities(rules);
        List<AutoSwitchRule> cpuRules = GetCpuRulesInPriorityOrder(rules);
        int index = cpuRules.FindIndex(rule => rule.Id == id);
        int otherIndex = index + offset;
        if (index < 0 || otherIndex < 0 || otherIndex >= cpuRules.Count) return;
        (cpuRules[index].Priority, cpuRules[otherIndex].Priority) = (cpuRules[otherIndex].Priority, cpuRules[index].Priority);
        SaveRules(rules);
    }

    private static bool IsCpuTrigger(AutomationTrigger trigger) => trigger is
        AutomationTrigger.SystemCpuBelow or AutomationTrigger.SystemCpuAbove or
        AutomationTrigger.ProcessCpuBelow or AutomationTrigger.ProcessCpuAbove;

    private static List<AutoSwitchRule> GetCpuRulesInPriorityOrder(IEnumerable<AutoSwitchRule> rules) => rules
        .Where(rule => IsCpuTrigger(rule.Trigger))
        .OrderBy(rule => rule.Priority <= 0 ? int.MaxValue : rule.Priority)
        .ToList();

    private static int NextCpuPriority(IEnumerable<AutoSwitchRule> rules) =>
        rules.Where(rule => IsCpuTrigger(rule.Trigger)).Select(rule => rule.Priority).DefaultIfEmpty(0).Max() + 1;

    private static void NormalizeCpuPriorities(IEnumerable<AutoSwitchRule> rules)
    {
        List<AutoSwitchRule> cpuRules = rules.Where(rule => IsCpuTrigger(rule.Trigger)).ToList();
        if (cpuRules.All(rule => rule.Priority > 0) && cpuRules.Select(rule => rule.Priority).Distinct().Count() == cpuRules.Count)
            cpuRules = cpuRules.OrderBy(rule => rule.Priority).ToList();
        int priority = 1;
        foreach (AutoSwitchRule rule in cpuRules) rule.Priority = priority++;
    }

    private void EnsureCpuPriorities()
    {
        List<AutoSwitchRule> rules = _appSettingsService.GetAutomationRules();
        Dictionary<Guid, int> original = rules.Where(rule => IsCpuTrigger(rule.Trigger)).ToDictionary(rule => rule.Id, rule => rule.Priority);
        NormalizeCpuPriorities(rules);
        if (rules.Any(rule => IsCpuTrigger(rule.Trigger) && original[rule.Id] != rule.Priority))
        {
            _appSettingsService.SetAutomationRules(rules);
            _automationRuleEngine.RefreshConfiguration();
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
        bool cpuRule = AppTriggerTypeComboBox.SelectedIndex == 1;
        double threshold = AppCpuThresholdNumberBox.Value;
        if (cpuRule && (double.IsNaN(threshold) || threshold is < 0 or > 100))
        {
            AppRuleStatusText.Text = "Enter a CPU threshold from 0 to 100%.";
            return;
        }
        rules.Add(new AutoSwitchRule
        {
            Trigger = !cpuRule ? AutomationTrigger.AppRunning : AppCpuDirectionComboBox.SelectedIndex == 1 ? AutomationTrigger.ProcessCpuAbove : AutomationTrigger.ProcessCpuBelow,
            AppExecutableName = executable,
            TargetPlanGuid = plan.Guid,
            CpuThresholdPercent = cpuRule ? threshold : 15,
            Priority = cpuRule ? NextCpuPriority(rules) : 0,
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
        NormalizeCpuPriorities(rules);
        _appSettingsService.SetAutomationRules(rules);
        _automationRuleEngine.RefreshConfiguration();
        RefreshSystemCpuRules();
        RefreshAppRules();
        AutomationSettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnAppTriggerTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        bool visible = AppTriggerTypeComboBox.SelectedIndex == 1;
        AppCpuDirectionComboBox.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        AppCpuThresholdNumberBox.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnAddSystemCpuRuleClick(object sender, RoutedEventArgs e)
    {
        if (SystemCpuDirectionComboBox.SelectedIndex < 0 || SystemCpuPlanComboBox.SelectedItem is not PowerScheme plan) return;
        double threshold = SystemCpuThresholdNumberBox.Value;
        if (double.IsNaN(threshold) || threshold is < 0 or > 100)
        {
            SystemCpuRuleStatusText.Text = "Enter a CPU threshold from 0 to 100%.";
            return;
        }
        List<AutoSwitchRule> rules = _appSettingsService.GetAutomationRules();
        rules.Add(new AutoSwitchRule
        {
            Name = "System CPU load",
            Trigger = SystemCpuDirectionComboBox.SelectedIndex == 1 ? AutomationTrigger.SystemCpuAbove : AutomationTrigger.SystemCpuBelow,
            CpuThresholdPercent = threshold,
            TargetPlanGuid = plan.Guid,
            Priority = NextCpuPriority(rules),
        });
        SaveRules(rules);
        SystemCpuRuleStatusText.Text = string.Empty;
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
            _allAdvancedSettings.Clear();
            AllAdvancedSettingsPanel.Children.Clear();
            AllAdvancedSection.Visibility = Visibility.Collapsed;
            AdvancedExpanderHeaderText.Text = "Show Hidden Advanced Settings";
            RefreshAdvancedSettings();
        }
    }

    private void RefreshAdvancedSettings()
    {
        if (AdvancedPlanComboBox.SelectedItem is not PowerScheme scheme) return;
        AdvancedSettingsPanel.Children.Clear();
        AllAdvancedSettingsPanel.Children.Clear();
        _allAdvancedSettings.Clear();
        AdvancedStatusText.Text = string.Empty;
        try
        {
            int visibleCount = 0;
            int hiddenCount = 0;
            foreach ((Guid subgroupGuid, string subgroupName) in _powerSchemeService.GetSubgroups(scheme.Guid))
            {
                var visibleSettings = new List<(Guid SettingGuid, string SettingName)>();
                var hiddenSettings = new List<(Guid SettingGuid, string SettingName)>();
                foreach ((Guid settingGuid, string settingName) in _powerSchemeService.GetSettings(scheme.Guid, subgroupGuid))
                {
                    _allAdvancedSettings.Add((subgroupGuid, settingGuid));
                    if (_powerSchemeService.IsSettingHidden(subgroupGuid, settingGuid))
                        hiddenSettings.Add((settingGuid, settingName));
                    else
                        visibleSettings.Add((settingGuid, settingName));
                }

                string categoryName = string.IsNullOrWhiteSpace(subgroupName) ? subgroupGuid.ToString() : subgroupName;
                if (visibleSettings.Count > 0)
                {
                    AdvancedSettingsPanel.Children.Add(CreateSettingsCategory(scheme.Guid, subgroupGuid, categoryName, visibleSettings, false));
                    visibleCount += visibleSettings.Count;
                }
                if (hiddenSettings.Count > 0)
                {
                    AllAdvancedSettingsPanel.Children.Add(CreateSettingsCategory(scheme.Guid, subgroupGuid, categoryName, hiddenSettings, true));
                    hiddenCount += hiddenSettings.Count;
                }
            }
            AdvancedStatusText.Text = $"Loaded {visibleCount} shown and {hiddenCount} hidden advanced settings.";
        }
        catch (Exception ex) { AdvancedStatusText.Text = $"Couldn't read advanced settings: {ex.Message}"; }
    }

    private FrameworkElement CreateSettingsCategory(Guid schemeGuid, Guid subgroupGuid, string subgroupName,
        IReadOnlyList<(Guid SettingGuid, string SettingName)> settings, bool isHiddenSection)
    {
        var settingList = new StackPanel
        {
            Spacing = 6,
            Margin = new Thickness(8, 0, 8, 0)
        };
        foreach ((Guid settingGuid, string settingName) in settings)
        {
            settingList.Children.Add(CreateSettingRowOrUnavailable(schemeGuid, subgroupGuid, settingGuid, settingName));
        }

        var settingExpanders = settingList.Children.OfType<Expander>().ToArray();
        var toggleIcon = new FontIcon { FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 12 };
        var toggleSettings = new Button
        {
            Content = toggleIcon,
            Width = 28,
            Height = 28,
            MinWidth = 28,
            MinHeight = 28,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 2, 4, 0),
            VerticalAlignment = VerticalAlignment.Top
        };
        void UpdateSettingsToggle()
        {
            bool allExpanded = settingExpanders.Length > 0 && settingExpanders.All(setting => setting.IsExpanded);
            toggleIcon.Glyph = allExpanded ? "\uE70E" : "\uE70D";
            string label = allExpanded ? "Collapse all settings in this category" : "Expand all settings in this category";
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggleSettings, label);
            ToolTipService.SetToolTip(toggleSettings, label);
        }
        foreach (Expander setting in settingExpanders)
        {
            setting.RegisterPropertyChangedCallback(Expander.IsExpandedProperty, (_, _) =>
            {
                QueueAdvancedReflow(settingList);
                UpdateSettingsToggle();
            });
        }
        toggleSettings.Click += (_, _) =>
        {
            bool expand = settingExpanders.Any(setting => !setting.IsExpanded);
            QueueAdvancedReflow(settingList);
            foreach (Expander setting in settingExpanders) setting.IsExpanded = expand;
            UpdateSettingsToggle();
        };
        UpdateSettingsToggle();

        var categoryContent = new Grid();
        categoryContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        categoryContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        categoryContent.Children.Add(toggleSettings);
        Grid.SetColumn(settingList, 1);
        categoryContent.Children.Add(settingList);

        var categoryKey = (isHiddenSection, subgroupGuid);
        var category = new Expander
        {
            Header = new TextBlock
            {
                Text = $"{subgroupName} ({settings.Count})",
                Style = (Style)Application.Current.Resources["BodyTextBlockStyle"]
            },
            Content = categoryContent,
            IsExpanded = _expandedAdvancedCategories.Contains(categoryKey),
            Tag = categoryKey,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            MinHeight = 36,
            Padding = new Thickness(2)
        };
        category.RegisterPropertyChangedCallback(Expander.IsExpandedProperty, (_, _) =>
        {
            if (category.Parent is StackPanel parent) QueueAdvancedReflow(parent);
            if (category.IsExpanded) _expandedAdvancedCategories.Add(categoryKey);
            else _expandedAdvancedCategories.Remove(categoryKey);
        });
        return category;
    }

    private void QueueAdvancedReflow(StackPanel panel)
    {
        if (!_pendingAdvancedReflows.Add(panel)) return;

        var positions = panel.Children
            .OfType<UIElement>()
            .ToDictionary(child => child, child => child.TransformToVisual(panel).TransformPoint(new Point()).Y);
        double oldHeight = panel.ActualHeight;

        void OnLayoutUpdated(object? sender, object args)
        {
            if (Math.Abs(panel.ActualHeight - oldHeight) < 0.5) return;
            panel.LayoutUpdated -= OnLayoutUpdated;
            _pendingAdvancedReflows.Remove(panel);

            foreach ((UIElement child, double oldY) in positions)
            {
                if (!panel.Children.Contains(child)) continue;
                double newY = child.TransformToVisual(panel).TransformPoint(new Point()).Y;
                double offset = oldY - newY;
                if (Math.Abs(offset) < 0.5) continue;

                var transform = child.RenderTransform as TranslateTransform ?? new TranslateTransform();
                child.RenderTransform = transform;
                transform.Y = offset;

                var animation = new DoubleAnimation
                {
                    From = offset,
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(300),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                Storyboard.SetTarget(animation, transform);
                Storyboard.SetTargetProperty(animation, nameof(TranslateTransform.Y));
                var storyboard = new Storyboard();
                storyboard.Children.Add(animation);
                storyboard.Begin();
            }
        }

        panel.LayoutUpdated += OnLayoutUpdated;
    }

    private void SetAdvancedCategoriesExpanded(StackPanel panel, bool expanded)
    {
        if (!panel.Children.OfType<Expander>().Any(category => category.IsExpanded != expanded)) return;
        QueueAdvancedReflow(panel);
        foreach (Expander category in panel.Children.OfType<Expander>())
        {
            category.IsExpanded = expanded;
            if (category.Tag is ValueTuple<bool, Guid> categoryKey)
            {
                if (expanded) _expandedAdvancedCategories.Add(categoryKey);
                else _expandedAdvancedCategories.Remove(categoryKey);
            }
        }
    }

    private void OnExpandShownAdvancedClick(object sender, RoutedEventArgs e) =>
        SetAdvancedCategoriesExpanded(AdvancedSettingsPanel, true);

    private void OnCollapseShownAdvancedClick(object sender, RoutedEventArgs e) =>
        SetAdvancedCategoriesExpanded(AdvancedSettingsPanel, false);

    private void OnExpandHiddenAdvancedClick(object sender, RoutedEventArgs e) =>
        SetAdvancedCategoriesExpanded(AllAdvancedSettingsPanel, true);

    private void OnCollapseHiddenAdvancedClick(object sender, RoutedEventArgs e) =>
        SetAdvancedCategoriesExpanded(AllAdvancedSettingsPanel, false);

    private FrameworkElement CreateSettingRowOrUnavailable(Guid schemeGuid, Guid subgroupGuid, Guid settingGuid, string? settingName = null)
    {
        try
        {
            string name = string.IsNullOrWhiteSpace(settingName)
                ? _powerSchemeService.GetSettingName(schemeGuid, subgroupGuid, settingGuid)
                : settingName;
            return CreateSettingRow(schemeGuid, subgroupGuid, settingGuid,
                string.IsNullOrWhiteSpace(name) ? settingGuid.ToString() : name);
        }
        catch (Exception ex)
        {
            return new TextBlock { Text = $"{settingGuid}: unavailable on this PC ({ex.Message})", TextWrapping = TextWrapping.Wrap };
        }
    }

    private void OnShowAllAdvancedClick(object sender, RoutedEventArgs e)
    {
        if (AllAdvancedSection.Visibility == Visibility.Visible)
        {
            AllAdvancedSection.Visibility = Visibility.Collapsed;
            AdvancedExpanderHeaderText.Text = "Show Hidden Advanced Settings";
            return;
        }

        AllAdvancedSection.Visibility = Visibility.Visible;
        AdvancedExpanderHeaderText.Text = "Hide Hidden Advanced Settings";
    }

    private async void OnRestoreWindowsDefaultsClick(object sender, RoutedEventArgs e) =>
        await SetVisibilityAsync(SettingDescriptions.CommonSettings.Select(s => (s.SubgroupGuid, s.SettingGuid)), false, "Restoring common Windows visibility");

    private async void OnEnableAllAdvancedClick(object sender, RoutedEventArgs e) =>
        await SetVisibilityAsync(GetCurrentlyHiddenAdvancedSettings(), false, "Enabling all hidden settings");

    private async void OnDisableAllAdvancedClick(object sender, RoutedEventArgs e) =>
        await SetVisibilityAsync(GetCurrentlyHiddenAdvancedSettings(), true, "Disabling all hidden settings");

    private IEnumerable<(Guid SubgroupGuid, Guid SettingGuid)> GetCurrentlyHiddenAdvancedSettings() =>
        _allAdvancedSettings.Where(setting => _powerSchemeService.IsSettingHidden(setting.SubgroupGuid, setting.SettingGuid)).ToArray();

    private async Task<bool> SetVisibilityAsync(IEnumerable<(Guid SubgroupGuid, Guid SettingGuid)> settings, bool hidden, string operation)
    {
        var targets = settings.Distinct().ToArray();
        AdvancedStatusText.Text = $"{operation} (0/{targets.Length})...";
        bool succeeded = await _elevationService.SetSettingsHiddenAsync(targets, hidden);
        if (succeeded)
        {
            RefreshAdvancedSettings();
            AdvancedStatusText.Text = $"{operation} complete ({targets.Length}/{targets.Length}).";
        }
        else
        {
            AdvancedStatusText.Text = _elevationService.LastOperationWasCancelled
                ? "Administrator permission was cancelled." : $"{operation} failed.";
        }
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

    private async Task InitializeProfilesAsync()
    {
        try
        {
            ProfilesStatusText.Text = "Loading profiles...";
            _advancedProfiles = await _appSettingsService.GetAdvancedSettingsProfilesAsync();
            AdvancedProfileComboBox.ItemsSource = _advancedProfiles;

            if (_allAdvancedSettings.Count == 0 && AdvancedPlanComboBox.SelectedItem is PowerScheme scheme)
            {
                foreach ((Guid subgroupGuid, _) in _powerSchemeService.GetSubgroups(scheme.Guid))
                {
                    foreach ((Guid settingGuid, _) in _powerSchemeService.GetSettings(scheme.Guid, subgroupGuid))
                    {
                        _allAdvancedSettings.Add((subgroupGuid, settingGuid));
                    }
                }
            }

            ProfilesStatusText.Text = _advancedProfiles.Count == 0
                ? "No saved profiles yet."
                : $"Loaded {_advancedProfiles.Count} saved profile{(_advancedProfiles.Count == 1 ? string.Empty : "s")}.";
        }
        catch (Exception ex) { ProfilesStatusText.Text = $"Couldn't load profiles: {ex.Message}"; }
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
            ProfilesStatusText.Text = $"Saved profile '{name}' with {profile.Settings.Count} settings.";
        }
        catch (Exception ex) { ProfilesStatusText.Text = $"Couldn't save profile: {ex.Message}"; }
    }

    private async void OnLoadProfileClick(object sender, RoutedEventArgs e)
    {
        if (AdvancedProfileComboBox.SelectedItem is AdvancedSettingsProfile profile) await ApplyProfileAsync(profile);
        else ProfilesStatusText.Text = "Choose a saved profile first.";
    }

    private async void OnDeleteProfileClick(object sender, RoutedEventArgs e)
    {
        if (AdvancedProfileComboBox.SelectedItem is not AdvancedSettingsProfile profile)
        {
            ProfilesStatusText.Text = "Choose a saved profile first.";
            return;
        }

        try
        {
            _advancedProfiles.Remove(profile);
            await _appSettingsService.SetAdvancedSettingsProfilesAsync(_advancedProfiles);
            AdvancedProfileComboBox.ItemsSource = null;
            AdvancedProfileComboBox.ItemsSource = _advancedProfiles;
            ProfilesStatusText.Text = $"Deleted profile '{profile.Name}'.";
        }
        catch (Exception ex) { ProfilesStatusText.Text = $"Couldn't delete profile: {ex.Message}"; }
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
        ProfilesStatusText.Text = visibilityApplied
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
            ProfilesStatusText.Text = $"Saved {profile.Settings.Count} settings to {file.Name}.";
        }
        catch (Exception ex) { ProfilesStatusText.Text = $"Couldn't export profile: {ex.Message}"; }
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
        catch (Exception ex) { ProfilesStatusText.Text = $"Couldn't import profile: {ex.Message}"; }
    }

    private FrameworkElement CreateSettingRow(Guid schemeGuid, Guid subgroupGuid, Guid settingGuid, string settingName)
    {
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        heading.Children.Add(new TextBlock { Text = settingName,
            VerticalAlignment = VerticalAlignment.Center, MaxWidth = 650, TextWrapping = TextWrapping.Wrap, FontSize = 13 });
        var info = new Button { Content = "i", Width = 26, Height = 26, MinWidth = 26, MinHeight = 26,
            CornerRadius = new CornerRadius(13), Padding = new Thickness(0) };
        info.Click += (_, _) => ShowSettingInfo(info, subgroupGuid, settingGuid);
        heading.Children.Add(info);

        var details = new StackPanel { Spacing = 6, Padding = new Thickness(12, 4, 12, 8) };
        PowerSettingMetadata metadata = _powerSchemeService.GetSettingMetadata(subgroupGuid, settingGuid);
        var values = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        values.Children.Add(CreateValueEditor("Plugged in", schemeGuid, subgroupGuid, settingGuid, true, metadata));
        values.Children.Add(CreateValueEditor("On battery", schemeGuid, subgroupGuid, settingGuid, false, metadata));
        details.Children.Add(values);

        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var unhide = new CheckBox { Content = "Shown in Windows Power Options", IsChecked = !_powerSchemeService.IsSettingHidden(subgroupGuid, settingGuid) };
        var status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        unhide.Click += async (_, _) =>
        {
            unhide.IsEnabled = false;
            bool succeeded = await _elevationService.SetSettingHiddenAsync(subgroupGuid, settingGuid, unhide.IsChecked != true);
            if (succeeded)
            {
                RefreshAdvancedSettings();
                AdvancedStatusText.Text = "Windows Power Options visibility updated.";
            }
            else
            {
                status.Text = _elevationService.LastOperationWasCancelled
                    ? "Administrator permission was cancelled." : "Couldn't update visibility.";
                unhide.IsChecked = !_powerSchemeService.IsSettingHidden(subgroupGuid, settingGuid);
                unhide.IsEnabled = true;
            }
        };
        footer.Children.Add(unhide);
        footer.Children.Add(status);
        details.Children.Add(footer);
        var settingKey = (subgroupGuid, settingGuid);
        var setting = new Expander
        {
            Header = heading,
            Content = details,
            IsExpanded = _expandedAdvancedSettings.Contains(settingKey),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            MinHeight = 28,
            Padding = new Thickness(0)
        };
        setting.Expanding += (_, _) => _expandedAdvancedSettings.Add(settingKey);
        setting.Collapsed += (_, _) => _expandedAdvancedSettings.Remove(settingKey);
        return setting;
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
        double availableWidth = Math.Max(0, WindowRoot.ActualWidth - 96);
        var content = new StackPanel { Spacing = 8, MaxWidth = Math.Min(480, availableWidth) };
        content.Children.Add(new TextBlock { Text = "Windows description", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        content.Children.Add(new TextBlock { Text = _powerSchemeService.GetSettingDescription(subgroupGuid, settingGuid) ?? "No Windows description is available.", TextWrapping = TextWrapping.Wrap });
        content.Children.Add(new TextBlock { Text = "In plain terms", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
        content.Children.Add(new TextBlock { Text = SettingDescriptions.GetLaymanDescription(settingGuid) ?? NoLaymanDescription, TextWrapping = TextWrapping.Wrap });
        target.Flyout = new Flyout { Content = content, ShouldConstrainToRootBounds = true };
        target.Flyout.ShowAt(target);
    }

    private void RefreshCpuBoostState()
    {
        try
        {
            Guid active = _powerSchemeService.GetActiveSchemeGuid();
            uint value = _powerSchemeService.GetACValue(active, ProcessorSubgroupGuid, ProcessorMaximumStateGuid);
            bool wasInitializing = _isInitializing;
            _isInitializing = true;
            DisableCpuBoostCheckBox.IsChecked = value <= 99;
            _isInitializing = wasInitializing;
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
                var dialog = new ContentDialog { Title = L("ReenableCpuBoostTitle"),
                    Content = F("ReenableCpuBoostContent", current),
                    PrimaryButtonText = L("Yes"), CloseButtonText = L("CancelLabel"), DefaultButton = ContentDialogButton.Close,
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
                        Title = L("StartupPermissionNeeded"),
                        Content = L("StartupPermissionInstructions"),
                        CloseButtonText = L("Ok"),
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
