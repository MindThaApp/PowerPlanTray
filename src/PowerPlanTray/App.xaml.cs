using System.Windows.Input;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using PowerPlanTray.Core.Models;
using PowerPlanTray.Core.Services;
using System.Drawing;
using Microsoft.Windows.AppLifecycle;

namespace PowerPlanTray;

public partial class App : Application
{
    private static string L(string key) => Localization.Get(key);
    private static string F(string key, params object?[] args) => Localization.Format(key, args);
    private readonly PowerSchemeService _powerSchemeService = new();
    private readonly AppSettingsService _appSettingsService = new();
#if !LITE_EDITION
    private readonly LicensingService _licensingService;
#endif
    private readonly PowerSourceMonitor _powerSourceMonitor = new();
    private readonly AutomationRuleEngine _automationRuleEngine;
    private readonly SystemMetricMonitorService _gaugeMetricMonitor = new();
    private Window? _hiddenWindow;
    private TaskbarIcon? _trayIcon;
    private TrayPopupWindow? _trayPopup;
    private SettingsWindow? _settingsWindow;
    private Icon? _dynamicTrayIcon;
    private double _latestCpuLoad;
    private double _latestGaugeValue;
    private readonly BitmapImage _staticTrayIcon = new(new Uri("ms-appx:///Assets/TrayIcon.ico"));

    public App()
    {
#if !LITE_EDITION
        _licensingService = new LicensingService(_appSettingsService);
#endif
        _automationRuleEngine = new AutomationRuleEngine(_powerSchemeService, _powerSourceMonitor, _appSettingsService);
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        _hiddenWindow = new Window { Title = "PowerPlanTray background host" };
        _trayPopup = new TrayPopupWindow(
            _powerSchemeService, _appSettingsService, _powerSourceMonitor, _automationRuleEngine, SwitchScheme,
            ShowSettingsWindow, () => Current.Exit());

        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "Power Plan Manager Pro",
            IconSource = _staticTrayIcon,
            // Commands still receive tray mouse messages; None prevents the
            // library from also opening its native OS-drawn PopupMenu.
            MenuActivation = PopupActivationMode.None,
            RightClickCommand = new RelayCommand(() => _trayPopup.Show(fullMenu: true)),
        };
        _automationRuleEngine.SystemCpuLoadUpdated += OnSystemCpuLoadUpdated;
        _gaugeMetricMonitor.MetricUpdated += OnGaugeMetricUpdated;
        // LeftClickCommand deliberately waits for the system double-click
        // interval. This app has no double-click action, so handle the raw
        // mouse-up notification directly for an immediate, reliable toggle.
        _trayIcon.TrayIcon.MessageWindow.MouseEventReceived += (_, eventArgs) =>
        {
            if (eventArgs.MouseEvent == MouseEvent.IconLeftMouseUp)
                _trayPopup.Toggle(fullMenu: false);
            else if (eventArgs.MouseEvent == MouseEvent.IconLeftDoubleClick)
                ShowSettingsWindow();
        };

#if LITE_EDITION
        await Task.CompletedTask;
        _automationRuleEngine.SetProAccessEnabled(false);
#else
        _automationRuleEngine.SetProAccessEnabled(await _licensingService.IsProUnlockedAsync());
#endif
        _automationRuleEngine.Start();
#if !LITE_EDITION
        _ = DisableProAutomationWhenTrialExpiresAsync();
#endif
        _trayIcon.ForceCreate();
        ApplyTrayIconMode();
        ExtendedActivationKind activationKind = AppInstance.GetCurrent().GetActivatedEventArgs().Kind;
        bool startHidden = activationKind == ExtendedActivationKind.StartupTask
            ? _appSettingsService.StartHiddenOnStartup
            : _appSettingsService.StartHiddenOnManualLaunch;
        if (!startHidden) ShowSettingsWindow();
    }

#if !LITE_EDITION
    private async Task DisableProAutomationWhenTrialExpiresAsync()
    {
        TimeSpan remaining = _licensingService.TrialRemaining;
        if (remaining <= TimeSpan.Zero) return;
        try
        {
            await Task.Delay(remaining);
            bool unlocked = await _licensingService.IsProUnlockedAsync(forceLicenseRefresh: true);
            _hiddenWindow?.DispatcherQueue.TryEnqueue(() => _automationRuleEngine.SetProAccessEnabled(unlocked));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PowerPlanTray: trial-expiration refresh failed: {ex}");
        }
    }
#endif

    private void ShowSettingsWindow()
    {
        try
        {
            if (_settingsWindow is null)
            {
                _settingsWindow = new SettingsWindow(_appSettingsService, _powerSchemeService, _powerSourceMonitor, _automationRuleEngine
#if !LITE_EDITION
                    , _licensingService
#endif
                    );
                _settingsWindow.UiPreferencesChanged += (_, _) =>
                {
                    _trayPopup?.ApplyPreferences();
                    ApplyTrayIconMode();
                };
                _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            }
            _settingsWindow.ApplyUiPreferences();
            _settingsWindow.Activate();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PowerPlanTray: Settings failed to open: {ex}");
            _settingsWindow = null;
        }
    }

    private void SwitchScheme(Guid schemeGuid)
    {
        try { _powerSchemeService.SetActiveScheme(schemeGuid); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"PowerPlanTray: failed to switch scheme: {ex}"); }
        finally
        {
            _settingsWindow?.RefreshActiveSchemeSettings();
            if (_appSettingsService.TrayIconMode == TrayIconMode.PowerPlanAbbreviation) UpdateDynamicTrayIcon();
        }
    }

    private void ApplyTrayIconMode()
    {
        bool needsCpu = _appSettingsService.TrayIconMode is TrayIconMode.CpuPercentText or TrayIconMode.CpuBarChart;
        _automationRuleEngine.SetCpuStatusMonitoringRequired(needsCpu || _appSettingsService.TrayIconMode == TrayIconMode.PowerPlanAbbreviation);

        bool needsGauge = _appSettingsService.TrayIconMode == TrayIconMode.Gauge;
        if (needsGauge)
        {
            _gaugeMetricMonitor.SetMetric(_appSettingsService.TrayIconGaugeMetric);
            _gaugeMetricMonitor.Start();
        }
        else
        {
            _gaugeMetricMonitor.Stop();
        }

        if (_appSettingsService.TrayIconMode == TrayIconMode.Static)
        {
            if (_trayIcon is not null)
            {
                _trayIcon.Icon = null;
                _trayIcon.IconSource = _staticTrayIcon;
                _trayIcon.ToolTipText = "Power Plan Manager Pro";
            }
            _dynamicTrayIcon?.Dispose();
            _dynamicTrayIcon = null;
            return;
        }
        UpdateDynamicTrayIcon();
    }

    private void OnSystemCpuLoadUpdated(object? sender, double load)
    {
        _latestCpuLoad = load;
        _hiddenWindow?.DispatcherQueue.TryEnqueue(UpdateDynamicTrayIcon);
    }

    private void OnGaugeMetricUpdated(object? sender, double value)
    {
        _latestGaugeValue = value;
        _hiddenWindow?.DispatcherQueue.TryEnqueue(UpdateDynamicTrayIcon);
    }

    private static Color? ParseGaugeColor(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        string trimmed = hex.TrimStart('#');
        try
        {
            return trimmed.Length switch
            {
                6 => Color.FromArgb(255, Convert.ToInt32(trimmed[..2], 16), Convert.ToInt32(trimmed[2..4], 16), Convert.ToInt32(trimmed[4..6], 16)),
                8 => Color.FromArgb(Convert.ToInt32(trimmed[..2], 16), Convert.ToInt32(trimmed[2..4], 16), Convert.ToInt32(trimmed[4..6], 16), Convert.ToInt32(trimmed[6..8], 16)),
                _ => null,
            };
        }
        catch (FormatException) { return null; }
    }

    private void UpdateDynamicTrayIcon()
    {
        if (_trayIcon is null || _appSettingsService.TrayIconMode == TrayIconMode.Static) return;
        try
        {
            string planName = GetActivePlanName();
            Icon replacement = DynamicTrayIconRenderer.Render(
                _appSettingsService.TrayIconMode, _latestCpuLoad, planName,
                ParseGaugeColor(_appSettingsService.TrayIconGaugeColor), _latestGaugeValue);
            _trayIcon.IconSource = null;
            _trayIcon.Icon = replacement;
            Icon? previous = _dynamicTrayIcon;
            _dynamicTrayIcon = replacement;
            previous?.Dispose();
            _trayIcon.ToolTipText = _appSettingsService.TrayIconMode switch
            {
                TrayIconMode.CpuPercentText or TrayIconMode.CpuBarChart => F("TrayTooltipCpu", Math.Round(_latestCpuLoad), planName),
                TrayIconMode.Gauge => F("TrayTooltipGauge", GaugeMetricLabel(_appSettingsService.TrayIconGaugeMetric), Math.Round(_latestGaugeValue), planName),
                _ => F("TrayTooltipPlan", planName),
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PowerPlanTray: dynamic tray icon failed: {ex}");
            _trayIcon.Icon = null;
            _trayIcon.IconSource = _staticTrayIcon;
            _trayIcon.ToolTipText = "Power Plan Manager Pro";
            _dynamicTrayIcon?.Dispose();
            _dynamicTrayIcon = null;
        }
    }

    private static string GaugeMetricLabel(TrayGaugeMetric metric) => metric switch
    {
        TrayGaugeMetric.Memory => L("GaugeMetricMemory"),
        TrayGaugeMetric.Disk => L("GaugeMetricDisk"),
        TrayGaugeMetric.Network => L("GaugeMetricNetwork"),
        TrayGaugeMetric.Gpu => L("GaugeMetricGpu"),
        _ => L("GaugeMetricCpu"),
    };

    private string GetActivePlanName()
    {
        Guid active = _powerSchemeService.GetActiveSchemeGuid();
        return _powerSchemeService.GetAllSchemes().FirstOrDefault(scheme => scheme.Guid == active)?.Name ?? L("UnknownPlan");
    }

    private sealed class RelayCommand : ICommand
    {
        private readonly Action _execute;
        public RelayCommand(Action execute) => _execute = execute;
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter)
        {
            try { _execute(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"PowerPlanTray tray command failed: {ex}"); }
        }
    }
}
