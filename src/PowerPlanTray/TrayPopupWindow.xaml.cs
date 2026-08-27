using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.Win32;
using PowerPlanTray.Core.Models;
using PowerPlanTray.Core.Services;
using Windows.Graphics;

namespace PowerPlanTray;

public sealed partial class TrayPopupWindow : Window
{
    private static string L(string key) => Localization.Get(key);
    private static string F(string key, params object?[] args) => Localization.Format(key, args);
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
        PopupBorder.FlowDirection = Localization.FlowDirection;

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
        _automation.TimedSwitchStateChanged += OnTimedSwitchStateChanged;
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
        // Consume a hide queued by the tray mouse-down before deciding whether
        // this same click should close or open the popup.
        _deactivationTimer.Stop();
        if (_isShowing) Hide();
        else Show(fullMenu);
    }

    public void ApplyPreferences()
    {
        PopupBorder.RequestedTheme = _settings.Theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark or AppTheme.OledBlack => ElementTheme.Dark,
            _ => IsWindowsLightTheme() ? ElementTheme.Light : ElementTheme.Dark,
        };
        bool useSolidBlack = _settings.Theme == AppTheme.OledBlack;
        PopupBorder.Background = new SolidColorBrush(useSolidBlack ? Colors.Black : Colors.Transparent);
        ApplyBackdrop(useSolidBlack);
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
        AddAction(L("OpenApp"), () => { _showSettings(); Hide(); }, automationId: L("OpenAppAutomationName"));
        AddSeparator();
        IReadOnlyList<PowerScheme> all;
        Guid active;
        try
        {
            all = _powerSchemes.GetAllSchemes();
            active = _powerSchemes.GetActiveSchemeGuid();
            IReadOnlySet<Guid> visible = _settings.GetVisiblePlanGuids();
            foreach (PowerScheme scheme in all.Where(s => visible.Count == 0 || visible.Contains(s.Guid)))
            {
                Button planButton = AddAction(scheme.Guid == active ? $"✓  {scheme.Name}" : $"    {scheme.Name}", () =>
                {
                    _switchScheme(scheme.Guid);
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (!_isShowing) return;
                        BuildContent(fullMenu);
                        PopupContent.Children.OfType<Button>()
                            .FirstOrDefault(button => button.Tag is Guid guid && guid == scheme.Guid)
                            ?.Focus(FocusState.Programmatic);
                    });
                }, new Windows.UI.Text.FontWeight { Weight = (ushort)(scheme.Guid == active ? 600 : 400) }, F("SelectPowerPlan", scheme.Name));
                planButton.Tag = scheme.Guid;
            }
        }
        catch (Exception ex)
        {
            all = Array.Empty<PowerScheme>();
            AddMessage(F("UnableToReadPowerPlans", ex.Message));
        }

        if (!fullMenu) return;
        AddSeparator();
        AddAutomationSection(all);

        AddSeparator();
        AddAction(L("SettingsMenu"), () => { Hide(); _showSettings(); }, automationId: L("OpenSettingsAutomationName"));
        AddAction(L("Exit"), _exit, automationId: L("ExitAutomationName"));
    }

    private void AddAutomationSection(IReadOnlyList<PowerScheme> schemes)
    {
        var panel = new StackPanel { Spacing = 8, Padding = new Thickness(4, 4, 4, 4), MinWidth = 390 };
        AddSectionHeading(panel, L("TimedSwitches/Text"));
        AddTimedSwitchControls(panel, schemes);

        AddSectionHeading(panel, L("BatteryAndAcPower/Text"));
        var enabled = new ToggleSwitch
        {
            Header = L("Enabled"),
            IsOn = _settings.AutoSwitchBatteryAcEnabled,
            IsEnabled = _powerSource.HasBattery,
        };
        enabled.Toggled += (_, _) =>
        {
            _settings.AutoSwitchBatteryAcEnabled = enabled.IsOn;
            _automation.RefreshConfiguration(enabled.IsOn);
        };
        panel.Children.Add(enabled);
        var powerSourcePlans = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        powerSourcePlans.Children.Add(CreatePlanPicker(L("OnBatteryLabel"), schemes, _settings.BatteryPlanGuid, guid =>
        {
            _settings.BatteryPlanGuid = guid;
            _automation.RefreshConfiguration(_settings.AutoSwitchBatteryAcEnabled && _powerSource.IsOnBattery);
        }, 189));
        powerSourcePlans.Children.Add(CreatePlanPicker(L("OnAcPowerLabel"), schemes, _settings.AcPlanGuid, guid =>
        {
            _settings.AcPlanGuid = guid;
            _automation.RefreshConfiguration(_settings.AutoSwitchBatteryAcEnabled && !_powerSource.IsOnBattery);
        }, 189));
        panel.Children.Add(powerSourcePlans);

        List<AutoSwitchRule> rules = _settings.GetAutomationRules();
        AddRuleSection(panel, L("AppRulesLabel"), rules.Where(rule => rule.Trigger is
            AutomationTrigger.AppRunning or AutomationTrigger.ProcessCpuBelow or AutomationTrigger.ProcessCpuAbove), schemes);
        AddRuleSection(panel, L("SystemLoad/Text"), rules.Where(rule => rule.Trigger is
            AutomationTrigger.SystemCpuBelow or AutomationTrigger.SystemCpuAbove), schemes);

        var expander = new Expander
        {
            Header = L("Automation/Text"),
            Content = panel,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        expander.Expanding += (_, _) => ScheduleResizeToContent();
        expander.Collapsed += (_, _) => ScheduleResizeToContent();
        expander.SizeChanged += (_, _) => ScheduleResizeToContent();
        PopupContent.Children.Add(expander);
    }

    private void AddTimedSwitchControls(StackPanel panel, IReadOnlyList<PowerScheme> schemes)
    {
        TimedSwitchInfo? timed = _automation.CurrentTimedSwitch;
        if (timed is not null)
        {
            string planName = schemes.FirstOrDefault(scheme => scheme.Guid == timed.TargetPlanGuid)?.Name ?? "Temporary plan";
            int minutes = Math.Max(1, (int)Math.Ceiling(timed.Remaining.TotalMinutes));
            panel.Children.Add(new TextBlock { Text = $"{planName} — {F("TemporaryPlanMinutesRemaining", minutes)}", TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(CreateAction(L("CancelTemporaryPlan"), () =>
            {
                _automation.CancelTimedSwitch();
                BuildContent(true);
                ScheduleResizeToContent();
            }));
            return;
        }

        var planPicker = new ComboBox { Header = L("PowerPlan/Header"), ItemsSource = schemes, DisplayMemberPath = "Name", Width = 174 };
        Guid active = _powerSchemes.GetActiveSchemeGuid();
        planPicker.SelectedItem = schemes.FirstOrDefault(scheme => scheme.Guid != active) ?? schemes.FirstOrDefault();
        var durationPicker = new ComboBox { Header = "Duration", Width = 120, SelectedIndex = 0 };
        durationPicker.Items.Add("30 minutes");
        durationPicker.Items.Add("1 hour");
        durationPicker.Items.Add("2 hours");
        durationPicker.Items.Add("4 hours");
        var apply = new Button { Content = L("Apply/Content"), Width = 80, VerticalAlignment = VerticalAlignment.Bottom };
        var error = new TextBlock { Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"], TextWrapping = TextWrapping.Wrap };
        apply.Click += async (_, _) =>
        {
            if (planPicker.SelectedItem is not PowerScheme plan) return;
            TimeSpan duration = durationPicker.SelectedIndex switch
            {
                1 => TimeSpan.FromHours(1),
                2 => TimeSpan.FromHours(2),
                3 => TimeSpan.FromHours(4),
                _ => TimeSpan.FromMinutes(30),
            };
            try
            {
                await _automation.ApplyTimedSwitchAsync(plan.Guid, duration);
            }
            catch (Exception ex) { error.Text = $"Couldn't apply the temporary plan: {ex.Message}"; }
        };
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        controls.Children.Add(planPicker);
        controls.Children.Add(durationPicker);
        controls.Children.Add(apply);
        panel.Children.Add(controls);
        panel.Children.Add(error);
    }

    private void OnTimedSwitchStateChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_isShowing || !_fullMenu) return;
            BuildContent(true);
            ScheduleResizeToContent();
        });
    }

    private void AddRuleSection(StackPanel panel, string heading, IEnumerable<AutoSwitchRule> rules, IReadOnlyList<PowerScheme> schemes)
    {
        AddSectionHeading(panel, heading);
        AutoSwitchRule[] configured = rules.OrderBy(rule => rule.Priority).ToArray();
        if (configured.Length == 0)
        {
            panel.Children.Add(new TextBlock { Text = "No rules configured.", Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });
            return;
        }

        foreach (AutoSwitchRule rule in configured)
        {
            string planName = schemes.FirstOrDefault(scheme => scheme.Guid == rule.TargetPlanGuid)?.Name ?? "Unavailable plan";
            string description = rule.Trigger switch
            {
                AutomationTrigger.AppRunning => $"{rule.AppExecutableName} → {planName}",
                AutomationTrigger.ProcessCpuBelow => $"{rule.AppExecutableName} CPU below {rule.CpuThresholdPercent:G}% → {planName}",
                AutomationTrigger.ProcessCpuAbove => $"{rule.AppExecutableName} CPU above {rule.CpuThresholdPercent:G}% → {planName}",
                AutomationTrigger.SystemCpuBelow => $"CPU below {rule.CpuThresholdPercent:G}% → {planName}",
                _ => $"CPU above {rule.CpuThresholdPercent:G}% → {planName}",
            };
            var checkBox = new CheckBox
            {
                IsChecked = rule.Enabled,
                Content = new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap },
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            checkBox.Click += (_, _) => SetRuleEnabled(rule.Id, checkBox.IsChecked == true);
            panel.Children.Add(checkBox);
        }
    }

    private void SetRuleEnabled(Guid id, bool enabled)
    {
        List<AutoSwitchRule> rules = _settings.GetAutomationRules();
        AutoSwitchRule? rule = rules.FirstOrDefault(candidate => candidate.Id == id);
        if (rule is null) return;
        rule.Enabled = enabled;
        _settings.SetAutomationRules(rules);
        _automation.RefreshConfiguration();
    }

    private static void AddSectionHeading(StackPanel panel, string text) => panel.Children.Add(new TextBlock
    {
        Text = text,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        Margin = new Thickness(0, 4, 0, 0),
    });

    private static FrameworkElement CreatePlanPicker(string header, IReadOnlyList<PowerScheme> schemes, Guid? selected, Action<Guid> changed, double? width = null)
    {
        var combo = new ComboBox { Header = header, ItemsSource = schemes, DisplayMemberPath = "Name", HorizontalAlignment = HorizontalAlignment.Stretch };
        if (width.HasValue) combo.Width = width.Value;
        combo.SelectedItem = schemes.FirstOrDefault(s => s.Guid == selected);
        combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is PowerScheme scheme) changed(scheme.Guid); };
        return combo;
    }

    private Button AddAction(string text, Action action, Windows.UI.Text.FontWeight? weight = null, string? automationId = null)
    {
        Button button = CreateAction(text, action, weight, automationId);
        PopupContent.Children.Add(button);
        return button;
    }

    private static Button CreateAction(string text, Action action, Windows.UI.Text.FontWeight? weight = null, string? automationId = null)
    {
        var button = new Button
        {
            Content = text,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = Localization.FlowDirection == FlowDirection.RightToLeft
                ? HorizontalAlignment.Right
                : HorizontalAlignment.Left,
            FontWeight = weight ?? new Windows.UI.Text.FontWeight { Weight = 400 },
            Padding = new Thickness(12, 8, 12, 8),
        };
        if (automationId is not null) Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, automationId);
        button.Click += (_, _) => action();
        return button;
    }

    private void AddMessage(string text) => PopupContent.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, TextAlignment = Localization.FlowDirection == FlowDirection.RightToLeft ? TextAlignment.Right : TextAlignment.Left, Margin = new Thickness(12, 6, 12, 6) });
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

    private void ApplyBackdrop(bool useSolidBlack)
    {
        if (useSolidBlack)
        {
            SystemBackdrop = null;
            return;
        }
        try { SystemBackdrop = new MicaBackdrop { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.BaseAlt }; }
        catch
        {
            try { SystemBackdrop = new DesktopAcrylicBackdrop(); } catch { }
        }
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

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool SystemParametersInfo(int action, int param, out RECT rect, int flags);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
}
