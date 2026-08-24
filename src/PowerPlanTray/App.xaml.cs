using System.Windows.Input;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using PowerPlanTray.Core.Services;

namespace PowerPlanTray;

public partial class App : Application
{
    private readonly PowerSchemeService _powerSchemeService = new();
    private readonly AppSettingsService _appSettingsService = new();
    private readonly PowerSourceMonitor _powerSourceMonitor = new();
    private readonly AutomationRuleEngine _automationRuleEngine;
    private Window? _hiddenWindow;
    private TaskbarIcon? _trayIcon;
    private TrayPopupWindow? _trayPopup;
    private SettingsWindow? _settingsWindow;

    public App()
    {
        _automationRuleEngine = new AutomationRuleEngine(_powerSchemeService, _powerSourceMonitor, _appSettingsService);
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _hiddenWindow = new Window { Title = "PowerPlanTray background host" };
        _trayPopup = new TrayPopupWindow(
            _powerSchemeService, _appSettingsService, _powerSourceMonitor, _automationRuleEngine, SwitchScheme,
            ShowSettingsWindow, () => Current.Exit());

        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "Power Plan Tray",
            IconSource = new BitmapImage(new Uri("ms-appx:///Assets/TrayIcon.ico")),
            // Commands still receive tray mouse messages; None prevents the
            // library from also opening its native OS-drawn PopupMenu.
            MenuActivation = PopupActivationMode.None,
            LeftClickCommand = new RelayCommand(() => _trayPopup.Show(fullMenu: false)),
            RightClickCommand = new RelayCommand(() => _trayPopup.Show(fullMenu: true)),
        };

        _automationRuleEngine.Start();
        _trayIcon.ForceCreate();
        if (!_appSettingsService.StartHidden) ShowSettingsWindow();
    }

    private void ShowSettingsWindow()
    {
        try
        {
            if (_settingsWindow is null)
            {
                _settingsWindow = new SettingsWindow(_appSettingsService, _powerSchemeService, _powerSourceMonitor, _automationRuleEngine);
                _settingsWindow.UiPreferencesChanged += (_, _) => _trayPopup?.ApplyPreferences();
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
        finally { _settingsWindow?.RefreshActiveSchemeSettings(); }
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
