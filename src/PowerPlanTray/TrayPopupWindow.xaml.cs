using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using PowerPlanTray.Core.Models;
using PowerPlanTray.Core.Services;
using Windows.Graphics;

namespace PowerPlanTray;

public sealed partial class TrayPopupWindow : Window
{
    private const int SpiGetWorkArea = 0x0030;
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const double DefaultDpi = 96d;

    private readonly PowerSchemeService _powerSchemes;
    private readonly AppSettingsService _settings;
    private readonly PowerSourceMonitor _powerSource;
    private readonly AutomationRuleEngine _automation;
    private readonly Action _showSettings;
    private readonly Action _exit;
    private readonly Action<Guid> _switchScheme;
    private readonly AppWindow _appWindow;
    private readonly IntPtr _hwnd;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _deactivationTimer;
    private bool _isShowing;
    private bool _fullMenu;
    private bool _resizeToContentPending;

    public TrayPopupWindow(
        PowerSchemeService powerSchemes,
        AppSettingsService settings,
        PowerSourceMonitor powerSource,
        AutomationRuleEngine automation,
        Action<Guid> switchScheme,
        Action showSettings,
        Action exit)
    {
        _powerSchemes = powerSchemes;
        _settings = settings;
        _powerSource = powerSource;
        _automation = automation;
        _switchScheme = switchScheme;
        _showSettings = showSettings;
        _exit = exit;
        InitializeComponent();

        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WindowId id = Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(id);
        _appWindow.IsShownInSwitchers = false;
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMinimizable = false;
            presenter.IsMaximizable = false;
            presenter.IsAlwaysOnTop = true;
        }

        long style = GetWindowLongPtr(_hwnd, GwlExStyle).ToInt64();
        SetWindowLongPtr(_hwnd, GwlExStyle, new IntPtr(style | WsExToolWindow));
        _deactivationTimer = DispatcherQueue.CreateTimer();
        _deactivationTimer.Interval = TimeSpan.FromMilliseconds(100);
        _deactivationTimer.IsRepeating = false;
        _deactivationTimer.Tick += OnDeactivationTimerTick;
        TryApplyBackdrop();
        Activated += OnActivated;
        PopupBorder.KeyDown += OnKeyDown;
    }

    public void Show(bool fullMenu)
    {
        _deactivationTimer.Stop();
        ApplyPreferences();
        _fullMenu = fullMenu;
        BuildContent(fullMenu);
        PositionAboveTaskbar();
        _isShowing = true;
        Activate();
        // Window.Activate alone can leave an always-on-top tool window visible
        // without making it the foreground window when invoked from the shell.
        // Without foreground activation there is no later Deactivated transition
        // when the user clicks elsewhere.
        SetForegroundWindow(_hwnd);
        PopupBorder.Focus(FocusState.Programmatic);
    }

    public void Hide()
    {
        _deactivationTimer.Stop();
        _isShowing = false;
        _appWindow.Hide();
    }

    public void Toggle(bool fullMenu)
    {
        if (_isShowing) Hide();
        else Show(fullMenu);
    }

    public void ApplyPreferences()
    {
        PopupBorder.RequestedTheme = _settings.Theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        PopupContent.Resources["ControlContentThemeFontSize"] = _settings.PopupTextSize switch
        {
            UiSize.Small => 13d,
            UiSize.Large => 18d,
            _ => 15d,
        };
    }

    private void BuildContent(bool fullMenu)
    {
        PopupContent.Children.Clear();
        IReadOnlyList<PowerScheme> all;
        Guid active;
        try
        {
            all = _powerSchemes.GetAllSchemes();
            active = _powerSchemes.GetActiveSchemeGuid();
            IReadOnlySet<Guid> visible = _settings.GetVisiblePlanGuids();
            foreach (PowerScheme scheme in all.Where(s => visible.Count == 0 || visible.Contains(s.Guid)))
            {
                AddAction(scheme.Guid == active ? $"✓  {scheme.Name}" : $"    {scheme.Name}", () =>
                {
                    _switchScheme(scheme.Guid);
                    Hide();
                }, new Windows.UI.Text.FontWeight { Weight = (ushort)(scheme.Guid == active ? 600 : 400) }, $"Select {scheme.Name} power plan");
            }
        }
        catch (Exception ex)
        {
            all = Array.Empty<PowerScheme>();
            AddMessage($"Unable to read power plans: {ex.Message}");
        }

        if (!fullMenu) return;
        AddSeparator();
        AddAutomationSection(all);

        List<AutoSwitchRule> rules = _settings.GetAutomationRules()
            .Where(r => r.Enabled && r.Trigger is AutomationTrigger.AppRunning or AutomationTrigger.ProcessCpuBelow or AutomationTrigger.ProcessCpuAbove).ToList();
        if (rules.Count > 0)
        {
            var rulesPanel = new StackPanel { Spacing = 2 };
            foreach (AutoSwitchRule rule in rules)
            {
                PowerScheme? target = all.FirstOrDefault(s => s.Guid == rule.TargetPlanGuid);
                if (target is not null)
                    rulesPanel.Children.Add(CreateAction($"{rule.AppExecutableName} → {target.Name}", () => { _switchScheme(target.Guid); Hide(); }));
            }
            PopupContent.Children.Add(new Expander { Header = "App rules", Content = rulesPanel, HorizontalAlignment = HorizontalAlignment.Stretch });
        }

        TimedSwitchInfo? timed = _automation.CurrentTimedSwitch;
        if (timed is not null)
        {
            int minutes = Math.Max(1, (int)Math.Ceiling(timed.Remaining.TotalMinutes));
            AddMessage($"Temporary plan: {minutes} min remaining");
            AddAction("Cancel temporary plan", () => { _automation.CancelTimedSwitch(); Hide(); });
        }

        AddSeparator();
        AddAction("Settings…", () => { Hide(); _showSettings(); }, automationId: "Open settings");
        AddAction("Exit", _exit, automationId: "Exit Power Plan Tray");
    }

    private void AddAutomationSection(IReadOnlyList<PowerScheme> schemes)
    {
        var panel = new StackPanel { Spacing = 8, Padding = new Thickness(4, 6, 4, 6) };
        var enabled = new ToggleSwitch
        {
            Header = "Enabled",
            IsOn = _settings.AutoSwitchBatteryAcEnabled,
            IsEnabled = _powerSource.HasBattery,
        };
        enabled.Toggled += (_, _) =>
        {
            _settings.AutoSwitchBatteryAcEnabled = enabled.IsOn;
            _automation.RefreshConfiguration(enabled.IsOn);
        };
        panel.Children.Add(enabled);
        panel.Children.Add(CreatePlanPicker("On battery", schemes, _settings.BatteryPlanGuid, guid =>
        {
            _settings.BatteryPlanGuid = guid;
            _automation.RefreshConfiguration(_settings.AutoSwitchBatteryAcEnabled && _powerSource.IsOnBattery);
        }));
        panel.Children.Add(CreatePlanPicker("On AC power", schemes, _settings.AcPlanGuid, guid =>
        {
            _settings.AcPlanGuid = guid;
            _automation.RefreshConfiguration(_settings.AutoSwitchBatteryAcEnabled && !_powerSource.IsOnBattery);
        }));
        var expander = new Expander
        {
            Header = _settings.AutoSwitchBatteryAcEnabled ? "✓  Auto-switch on Battery/AC" : "Auto-switch on Battery/AC",
            Content = panel,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        expander.Expanding += (_, _) => ScheduleResizeToContent();
        expander.Collapsed += (_, _) => ScheduleResizeToContent();
        expander.SizeChanged += (_, _) => ScheduleResizeToContent();
        PopupContent.Children.Add(expander);
    }

    private static FrameworkElement CreatePlanPicker(string header, IReadOnlyList<PowerScheme> schemes, Guid? selected, Action<Guid> changed)
    {
        var combo = new ComboBox { Header = header, ItemsSource = schemes, DisplayMemberPath = "Name", HorizontalAlignment = HorizontalAlignment.Stretch };
        combo.SelectedItem = schemes.FirstOrDefault(s => s.Guid == selected);
        combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is PowerScheme scheme) changed(scheme.Guid); };
        return combo;
    }

    private void AddAction(string text, Action action, Windows.UI.Text.FontWeight? weight = null, string? automationId = null) =>
        PopupContent.Children.Add(CreateAction(text, action, weight, automationId));

    private static Button CreateAction(string text, Action action, Windows.UI.Text.FontWeight? weight = null, string? automationId = null)
    {
        var button = new Button
        {
            Content = text,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            FontWeight = weight ?? new Windows.UI.Text.FontWeight { Weight = 400 },
            Padding = new Thickness(12, 8, 12, 8),
        };
        if (automationId is not null) Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, automationId);
        button.Click += (_, _) => action();
        return button;
    }

    private void AddMessage(string text) => PopupContent.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 6, 12, 6) });
    private void AddSeparator() => PopupContent.Children.Add(new Rectangle { Height = 1, Fill = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"], Margin = new Thickness(4) });

    private void PositionAboveTaskbar()
    {
        double preferenceScale = _settings.PopupSize switch
        {
            UiSize.Small => 0.90,
            UiSize.Large => 1.15,
            _ => 1.0,
        };
        // Measure the actual menu assembled for this invocation. The cap only
        // turns exceptionally long menus into a scrollable surface; it is not
        // the normal popup size.
        PopupBorder.Width = double.NaN;
        PopupBorder.Height = double.NaN;
        PopupBorder.Measure(new Windows.Foundation.Size(520, 900));
        Windows.Foundation.Size desired = PopupBorder.DesiredSize;
        double widthDip = Math.Clamp(Math.Ceiling(desired.Width * preferenceScale), 220, 520);
        double heightDip = Math.Clamp(Math.Ceiling(desired.Height * preferenceScale), 80, 700);
        PopupBorder.Width = widthDip;
        PopupBorder.Height = heightDip;
        double scale = GetDpiForWindow(_hwnd) / DefaultDpi;
        int width = (int)Math.Round(widthDip * scale);
        int height = (int)Math.Round(heightDip * scale);
        RECT workArea;
        if (!SystemParametersInfo(SpiGetWorkArea, 0, out workArea, 0))
            workArea = new RECT { Left = 0, Top = 0, Right = GetSystemMetrics(0), Bottom = GetSystemMetrics(1) };
        int gap = (int)Math.Round(8 * scale);
        _appWindow.MoveAndResize(new RectInt32(workArea.Right - width - gap, workArea.Bottom - height - gap, width, height));
        System.Diagnostics.Debug.WriteLine($"PowerPlanTray popup ({(_fullMenu ? "right" : "left")}): natural {desired.Width:F0}x{desired.Height:F0} DIP, scaled {widthDip:F0}x{heightDip:F0} DIP, {width}x{height} px, DPI scale {scale:F2}");
    }

    private void ScheduleResizeToContent()
    {
        if (_resizeToContentPending) return;
        _resizeToContentPending = true;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            _resizeToContentPending = false;
            if (!_isShowing) return;

            // Expander events are raised while its visual state is changing. Run
            // after that work and force layout so DesiredSize reflects the newly
            // shown or hidden controls before resizing and re-anchoring the window.
            PopupBorder.UpdateLayout();
            PositionAboveTaskbar();
        });
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            // A tray-icon mouse-down deactivates the popup before its click command
            // runs. Defer hiding so that command can observe the still-visible popup
            // and toggle it closed instead of immediately reopening it.
            if (_isShowing) _deactivationTimer.Start();
        }
        else
        {
            _deactivationTimer.Stop();
        }
    }

    private void OnDeactivationTimerTick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        if (_isShowing && GetForegroundWindow() != _hwnd) Hide();
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape) { e.Handled = true; Hide(); }
    }

    private void TryApplyBackdrop()
    {
        try { SystemBackdrop = new MicaBackdrop { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.BaseAlt }; }
        catch
        {
            try { SystemBackdrop = new DesktopAcrylicBackdrop(); } catch { }
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool SystemParametersInfo(int action, int param, out RECT rect, int flags);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
}
