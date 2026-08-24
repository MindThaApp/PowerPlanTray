using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using PowerPlanTray.Core.Models;
using PowerPlanTray.Core.Services;

namespace PowerPlanTray;

/// <summary>
/// The entry point of the PowerPlanTray application.
///
/// Phase 1 scope: no main UI window. All interaction happens through a
/// system-tray icon whose flyout menu lists the system's power plans and
/// lets the user switch the active one. Settings windows, automation
/// rules, and elevation flows are explicitly out of scope for this phase
/// (see the TODO(phase2) markers below).
/// </summary>
public partial class App : Application
{
    private readonly PowerSchemeService _powerSchemeService = new();
    private readonly AppSettingsService _appSettingsService = new();

    // Kept alive for the lifetime of the app: WinUI 3 requires at least one
    // Window (and the DispatcherQueue/message loop that comes with it) for
    // the process to keep running. This window is intentionally never
    // activated/shown - all UI is surfaced via the tray icon instead.
    private Window? _hiddenWindow;

    private TaskbarIcon? _trayIcon;
    private SettingsWindow? _settingsWindow;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _hiddenWindow = new Window
        {
            Title = "PowerPlanTray",
        };
        // Note: deliberately not calling _hiddenWindow.Activate() - the
        // window must exist to keep the app alive, but must never be shown.

        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "Power Plan Tray",
            IconSource = new BitmapImage(new Uri("ms-appx:///Assets/TrayIcon.ico")),
            MenuActivation = PopupActivationMode.LeftOrRightClick,
        };

        RebuildTrayMenu();

        _trayIcon.ForceCreate();

        if (!_appSettingsService.StartHidden)
        {
            ShowSettingsWindow();
        }
    }

    /// <summary>
    /// Rebuilds the tray icon's flyout menu from the current set of power
    /// schemes and the currently active scheme. Called on startup and after
    /// every scheme switch so the checked state stays accurate.
    /// </summary>
    private void RebuildTrayMenu()
    {
        if (_trayIcon is null)
        {
            return;
        }

        var flyout = new MenuFlyout();

        IReadOnlyList<PowerScheme> schemes;
        Guid activeGuid;
        try
        {
            schemes = _powerSchemeService.GetAllSchemes();
            activeGuid = _powerSchemeService.GetActiveSchemeGuid();
        }
        catch (Exception ex)
        {
            flyout.Items.Add(new MenuFlyoutItem
            {
                Text = $"Unable to read power plans: {ex.Message}",
                IsEnabled = false,
            });
            schemes = Array.Empty<PowerScheme>();
            activeGuid = Guid.Empty;
        }

        foreach (PowerScheme scheme in schemes)
        {
            IReadOnlySet<Guid> visibleGuids = _appSettingsService.GetVisiblePlanGuids();
            if (visibleGuids.Count > 0 && !visibleGuids.Contains(scheme.Guid))
            {
                continue;
            }

            var item = new RadioMenuFlyoutItem
            {
                Text = scheme.Name,
                GroupName = "PowerPlanTray.PowerSchemes",
                IsChecked = scheme.Guid == activeGuid,
                Tag = scheme.Guid,
            };
            item.Click += OnSchemeItemClick;
            flyout.Items.Add(item);
        }

        flyout.Items.Add(new MenuFlyoutSeparator());

        // TODO(phase2): add automation-rule quick-toggle menu items here.

        var settingsItem = new MenuFlyoutItem { Text = "Settings…" };
        settingsItem.Click += OnSettingsItemClick;
        flyout.Items.Add(settingsItem);

        flyout.Items.Add(new MenuFlyoutSeparator());

        var exitItem = new MenuFlyoutItem { Text = "Exit" };
        exitItem.Click += (_, _) => Application.Current.Exit();
        flyout.Items.Add(exitItem);

        // The same flyout is used for both left-click and right-click in
        // phase 1 (see MenuActivation = PopupActivationMode.LeftOrRightClick
        // above). Later phases may diverge left/right-click behavior.
        _trayIcon.ContextFlyout = flyout;
    }

    private void OnSettingsItemClick(object sender, RoutedEventArgs e) => ShowSettingsWindow();

    private void ShowSettingsWindow()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow();
            _settingsWindow.PowerPlansChanged += (_, _) => RebuildTrayMenu();
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }

        _settingsWindow.Activate();
    }

    private void OnSchemeItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is RadioMenuFlyoutItem { Tag: Guid schemeGuid })
        {
            try
            {
                _powerSchemeService.SetActiveScheme(schemeGuid);
            }
            catch (Exception)
            {
                // TODO(phase2): surface failures to the user (toast/notification),
                // e.g. when switching requires elevation.
            }
            finally
            {
                RebuildTrayMenu();
            }
        }
    }
}
