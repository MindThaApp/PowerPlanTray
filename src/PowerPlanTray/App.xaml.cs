using System.Windows.Input;
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
/// No main UI window. All interaction happens through a system-tray icon:
/// - Left-click opens a minimal popup listing only the power plans (a quick
///   picker - click a plan and it's applied immediately).
/// - Right-click opens the full context menu: the same plan list, the
///   Battery/AC automation submenu, Settings, and Exit.
/// </summary>
public partial class App : Application
{
    private readonly PowerSchemeService _powerSchemeService = new();
    private readonly AppSettingsService _appSettingsService = new();
    private readonly PowerSourceMonitor _powerSourceMonitor = new();
    private readonly AutomationRuleEngine _automationRuleEngine;

    // Kept alive for the lifetime of the app: WinUI 3 requires at least one
    // Window (and the DispatcherQueue/message loop that comes with it) for
    // the process to keep running. This window is intentionally never
    // activated/shown - all UI is surfaced via the tray icon instead.
    private Window? _hiddenWindow;

    private TaskbarIcon? _trayIcon;
    private SettingsWindow? _settingsWindow;

    // Two persistent MenuFlyout instances: a minimal plan-only picker for
    // left-click, and the full menu for right-click. Both are built once and
    // then have their Items refreshed in place (never replaced) whenever
    // they're about to be shown - see the big comment on TaskbarIcon.TrayPopup
    // below for why "replace the whole element from inside a click callback"
    // is unsafe here.
    private MenuFlyout? _planPickerFlyout;
    private MenuFlyout? _fullMenuFlyout;

    public App()
    {
        _automationRuleEngine = new AutomationRuleEngine(
            _powerSchemeService,
            _powerSourceMonitor,
            _appSettingsService);
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

        _planPickerFlyout = BuildPlanPickerFlyout();
        _fullMenuFlyout = BuildFullMenu();

        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "Power Plan Tray",
            IconSource = new BitmapImage(new Uri("ms-appx:///Assets/TrayIcon.ico")),

            // IMPORTANT: TaskbarIcon.ContextMenuMode defaults to PopupMenu,
            // which does NOT host the MenuFlyout as a real XAML popup at
            // all - it mirrors each item into a native Win32 popup menu
            // (that's why the flyout renders/screenshots fine even though
            // the hidden window below is never Activate()d) and, on
            // selection, invokes only the item's ICommand (Command
            // property) - it never raises the WinUI Click routed event.
            // Every MenuFlyoutItem/RadioMenuFlyoutItem/ToggleMenuFlyoutItem
            // built below must therefore be wired via .Command (see the
            // RelayCommand adapter at the bottom of this file), never via
            // .Click - a Click-only handler will silently never fire from
            // a real click even though everything else about it looks
            // correct. (github.com/HavenDV/H.NotifyIcon issue #109.)
            //
            // Both left- and right-click show ContextFlyout (the one path
            // this app has field-verified doesn't crash: H.NotifyIcon's
            // separate TrayPopup/PopupActivation mechanism was tried first
            // for the left-click picker and reliably fault-crashed the
            // process - 0xc000027b in CoreMessagingXP.dll, a native fail-fast
            // that bypasses every managed exception handler - even with no
            // reentrant mutation involved. So left vs. right click instead
            // swap *which* MenuFlyout is currently assigned to ContextFlyout,
            // via LeftClickCommand/RightClickCommand, before the library
            // shows it (confirmed by decompiling TaskbarIcon.OnMouseEvent:
            // both commands run synchronously before their respective
            // Show*() call for that click).
            MenuActivation = PopupActivationMode.LeftOrRightClick,
            LeftClickCommand = new RelayCommand(() =>
            {
                RefreshPlanPickerFlyout();
                if (_trayIcon is not null) _trayIcon.ContextFlyout = _planPickerFlyout;
            }),
            RightClickCommand = new RelayCommand(() =>
            {
                RefreshFullMenu(_fullMenuFlyout!);
                if (_trayIcon is not null) _trayIcon.ContextFlyout = _fullMenuFlyout;
            }),
        };

        // Whichever click type fires first, ContextFlyout needs a non-null
        // starting value.
        _trayIcon.ContextFlyout = _planPickerFlyout;

        _automationRuleEngine.TimedSwitchStateChanged += OnTimedSwitchStateChanged;
        _automationRuleEngine.Start();

        RefreshPlanPickerFlyout();

        _trayIcon.ForceCreate();

        if (!_appSettingsService.StartHidden)
        {
            ShowSettingsWindow();
        }
    }

    /// <summary>
    /// Reads the current set of power schemes and the active scheme once, so
    /// every menu/popup builder shares the exact same fetch-and-filter logic
    /// instead of duplicating it. <paramref name="onlyVisible"/> applies the
    /// user's tray-visibility filter (used for plan pickers); pass false to
    /// get every installed scheme (used for the automation Battery/AC pickers,
    /// matching how the Settings window's Automation tab lists plans).
    /// </summary>
    private (IReadOnlyList<PowerScheme> Schemes, Guid ActiveGuid, string? Error) GetSchemes(bool onlyVisible)
    {
        try
        {
            IReadOnlyList<PowerScheme> all = _powerSchemeService.GetAllSchemes();
            Guid active = _powerSchemeService.GetActiveSchemeGuid();

            if (onlyVisible)
            {
                IReadOnlySet<Guid> visibleGuids = _appSettingsService.GetVisiblePlanGuids();
                if (visibleGuids.Count > 0)
                {
                    all = all.Where(scheme => visibleGuids.Contains(scheme.Guid)).ToArray();
                }
            }

            return (all, active, null);
        }
        catch (Exception ex)
        {
            return (Array.Empty<PowerScheme>(), Guid.Empty, ex.Message);
        }
    }

    /// <summary>
    /// Applies the given scheme as the active Windows power plan. Shared by
    /// every plan-selection UI (left-click popup, right-click plan list).
    /// </summary>
    private void SwitchScheme(Guid schemeGuid)
    {
        try
        {
            _powerSchemeService.SetActiveScheme(schemeGuid);
        }
        catch (Exception ex)
        {
            // TODO(phase2): surface failures to the user (toast/notification),
            // e.g. when switching requires elevation. Logged for now so a
            // failure is at least observable instead of silently invisible.
            System.Diagnostics.Debug.WriteLine($"PowerPlanTray: failed to switch power scheme to {schemeGuid}: {ex}");
        }
        finally
        {
            _settingsWindow?.RefreshActiveSchemeSettings();
        }
    }

    // ---------------------------------------------------------------------
    // Left-click: minimal plan-picker flyout (plans only, no separator/
    // Settings/Exit/automation - just RadioMenuFlyoutItems, same widget kind
    // the full menu uses, swapped onto ContextFlyout via LeftClickCommand).
    // ---------------------------------------------------------------------

    private MenuFlyout BuildPlanPickerFlyout()
    {
        var flyout = new MenuFlyout();
        flyout.Opening += (sender, _) => RefreshPlanPickerFlyout((MenuFlyout)sender!);
        RefreshPlanPickerFlyout(flyout);
        return flyout;
    }

    /// <summary>
    /// Rebuilds the left-click flyout's plan items in place. Called once at
    /// startup, again from <see cref="TaskbarIcon.LeftClickCommand"/>
    /// immediately before each left-click shows it, and again on the
    /// flyout's own Opening event as a second safety net - so the checkmark
    /// is always current no matter which hook actually fires first.
    /// </summary>
    private void RefreshPlanPickerFlyout(MenuFlyout? flyout = null)
    {
        flyout ??= _planPickerFlyout;
        if (flyout is null)
        {
            return;
        }

        flyout.Items.Clear();

        (IReadOnlyList<PowerScheme> schemes, Guid activeGuid, string? error) = GetSchemes(onlyVisible: true);

        if (error is not null)
        {
            flyout.Items.Add(new MenuFlyoutItem
            {
                Text = $"Unable to read power plans: {error}",
                IsEnabled = false,
            });
        }
        else if (schemes.Count == 0)
        {
            flyout.Items.Add(new MenuFlyoutItem
            {
                Text = "No power plans configured for the tray.",
                IsEnabled = false,
            });
        }
        else
        {
            foreach (PowerScheme scheme in schemes)
            {
                var item = new RadioMenuFlyoutItem
                {
                    Text = scheme.Name,
                    GroupName = "PowerPlanTray.PowerSchemes.Picker",
                    IsChecked = scheme.Guid == activeGuid,
                    Tag = scheme.Guid,
                };
                item.Command = new RelayCommand(() => SwitchScheme(scheme.Guid));
                flyout.Items.Add(item);
            }
        }
    }

    // ---------------------------------------------------------------------
    // Right-click: full menu (ContextFlyout / MenuActivation)
    // ---------------------------------------------------------------------

    private MenuFlyout BuildFullMenu()
    {
        var flyout = new MenuFlyout();
        // Rebuild the contents fresh every time the menu is about to open, so
        // checkmarks/labels never go stale between opens (this is also what
        // fixes the "checkmark never updates" half of bug 1 - the previous
        // version only rebuilt on startup/after-switch, not on open).
        flyout.Opening += (sender, _) => RefreshFullMenu((MenuFlyout)sender!);
        RefreshFullMenu(flyout);
        return flyout;
    }

    private void RefreshFullMenu(MenuFlyout flyout)
    {
        flyout.Items.Clear();

        (IReadOnlyList<PowerScheme> visibleSchemes, Guid activeGuid, string? error) = GetSchemes(onlyVisible: true);

        if (error is not null)
        {
            flyout.Items.Add(new MenuFlyoutItem
            {
                Text = $"Unable to read power plans: {error}",
                IsEnabled = false,
            });
        }
        else
        {
            foreach (PowerScheme scheme in visibleSchemes)
            {
                var item = new RadioMenuFlyoutItem
                {
                    Text = scheme.Name,
                    GroupName = "PowerPlanTray.PowerSchemes.Full",
                    IsChecked = scheme.Guid == activeGuid,
                    Tag = scheme.Guid,
                };
                item.Command = new RelayCommand(() => SwitchScheme(scheme.Guid));
                flyout.Items.Add(item);
            }
        }

        flyout.Items.Add(new MenuFlyoutSeparator());

        (IReadOnlyList<PowerScheme> allSchemes, _, string? allError) = GetSchemes(onlyVisible: false);
        flyout.Items.Add(BuildAutomationSubmenu(allError is null ? allSchemes : Array.Empty<PowerScheme>()));

        TimedSwitchInfo? timedSwitch = _automationRuleEngine.CurrentTimedSwitch;
        if (timedSwitch is not null)
        {
            string planName = visibleSchemes.Concat(allSchemes)
                .FirstOrDefault(scheme => scheme.Guid == timedSwitch.TargetPlanGuid)?.Name
                ?? "Temporary plan";
            int minutes = Math.Max(1, (int)Math.Ceiling(timedSwitch.Remaining.TotalMinutes));
            flyout.Items.Add(new MenuFlyoutSeparator());
            flyout.Items.Add(new MenuFlyoutItem
            {
                Text = $"{planName}: {minutes} min remaining",
                IsEnabled = false,
            });
            var cancelItem = new MenuFlyoutItem { Text = "Cancel temporary plan" };
            cancelItem.Command = new RelayCommand(() => _automationRuleEngine.CancelTimedSwitch());
            flyout.Items.Add(cancelItem);
        }

        flyout.Items.Add(new MenuFlyoutSeparator());

        var settingsItem = new MenuFlyoutItem { Text = "Settings…" };
        settingsItem.Command = new RelayCommand(ShowSettingsWindow);
        flyout.Items.Add(settingsItem);

        flyout.Items.Add(new MenuFlyoutSeparator());

        var exitItem = new MenuFlyoutItem { Text = "Exit" };
        exitItem.Command = new RelayCommand(() => Application.Current.Exit());
        flyout.Items.Add(exitItem);
    }

    /// <summary>
    /// Builds the "Auto-switch on Battery/AC" submenu: an enable/disable
    /// toggle plus radio-select plan pickers for "On Battery" and "On AC
    /// Power". Selecting a plan updates AppSettingsService immediately and
    /// nudges AutomationRuleEngine to pick it up live (same
    /// RefreshConfiguration pattern the Settings window's Automation tab
    /// uses), with no app restart required.
    /// </summary>
    private MenuFlyoutSubItem BuildAutomationSubmenu(IReadOnlyList<PowerScheme> allSchemes)
    {
        bool enabled = _appSettingsService.AutoSwitchBatteryAcEnabled;
        Guid? batteryGuid = _appSettingsService.BatteryPlanGuid;
        Guid? acGuid = _appSettingsService.AcPlanGuid;
        bool configured = batteryGuid.HasValue && acGuid.HasValue;

        var sub = new MenuFlyoutSubItem { Text = "Auto-switch on Battery/AC" };
        if (enabled && configured)
        {
            sub.Icon = new FontIcon { Glyph = "\uE73E" };
        }

        var toggle = new ToggleMenuFlyoutItem
        {
            Text = "Enabled",
            IsChecked = enabled,
            IsEnabled = _powerSourceMonitor.HasBattery,
        };
        // Toggling via the native PopupMenu bridge (see the ContextMenuMode
        // comment on the TaskbarIcon setup) invokes Command, not Click, and
        // there is no reliable post-toggle IsChecked to read back from a
        // shadow native menu item - so just flip the pre-toggle `enabled`
        // captured above instead of reading item.IsChecked from a handler.
        toggle.Command = new RelayCommand(() =>
        {
            bool newValue = !enabled;
            _appSettingsService.AutoSwitchBatteryAcEnabled = newValue;
            _automationRuleEngine.RefreshConfiguration(applyCurrentPowerState: newValue);
        });
        sub.Items.Add(toggle);
        sub.Items.Add(new MenuFlyoutSeparator());

        sub.Items.Add(new MenuFlyoutItem { Text = "On Battery", IsEnabled = false });
        foreach (PowerScheme scheme in allSchemes)
        {
            var item = new RadioMenuFlyoutItem
            {
                Text = scheme.Name,
                GroupName = "PowerPlanTray.Automation.Battery",
                IsChecked = batteryGuid.HasValue && scheme.Guid == batteryGuid.Value,
                Tag = scheme.Guid,
            };
            item.Command = new RelayCommand(() =>
            {
                _appSettingsService.BatteryPlanGuid = scheme.Guid;
                _automationRuleEngine.RefreshConfiguration(
                    applyCurrentPowerState: _appSettingsService.AutoSwitchBatteryAcEnabled && _powerSourceMonitor.IsOnBattery);
            });
            sub.Items.Add(item);
        }

        sub.Items.Add(new MenuFlyoutSeparator());

        sub.Items.Add(new MenuFlyoutItem { Text = "On AC Power", IsEnabled = false });
        foreach (PowerScheme scheme in allSchemes)
        {
            var item = new RadioMenuFlyoutItem
            {
                Text = scheme.Name,
                GroupName = "PowerPlanTray.Automation.Ac",
                IsChecked = acGuid.HasValue && scheme.Guid == acGuid.Value,
                Tag = scheme.Guid,
            };
            item.Command = new RelayCommand(() =>
            {
                _appSettingsService.AcPlanGuid = scheme.Guid;
                _automationRuleEngine.RefreshConfiguration(
                    applyCurrentPowerState: _appSettingsService.AutoSwitchBatteryAcEnabled && !_powerSourceMonitor.IsOnBattery);
            });
            sub.Items.Add(item);
        }

        return sub;
    }

    // ---------------------------------------------------------------------
    // Settings window / misc
    // ---------------------------------------------------------------------

    private void ShowSettingsWindow()
    {
        try
        {
            if (_settingsWindow is null)
            {
                _settingsWindow = new SettingsWindow(
                    _appSettingsService,
                    _powerSchemeService,
                    _powerSourceMonitor,
                    _automationRuleEngine);
                _settingsWindow.PowerPlansChanged += (_, _) => RefreshPlanPickerFlyout();
                _settingsWindow.AutomationSettingsChanged += (_, _) => RefreshPlanPickerFlyout();
                _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            }

            _settingsWindow.Activate();
        }
        catch (Exception)
        {
            // TODO(phase2): surface a notification that Settings failed to open
            // instead of silently swallowing - better than crashing the whole
            // tray app on a XAML/window construction failure.
            _settingsWindow = null;
        }
    }

    private void OnTimedSwitchStateChanged(object? sender, EventArgs e) =>
        _hiddenWindow?.DispatcherQueue.TryEnqueue(() => RefreshPlanPickerFlyout());

    /// <summary>
    /// Minimal ICommand adapter so TaskbarIcon.LeftClickCommand can invoke a
    /// plain method - no MVVM toolkit is referenced by this project.
    /// </summary>
    private sealed class RelayCommand : ICommand
    {
        private readonly Action _execute;

        public RelayCommand(Action execute) => _execute = execute;

        public event EventHandler? CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
            try
            {
                _execute();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PowerPlanTray: RelayCommand.Execute threw: {ex}");
            }
        }
    }
}
