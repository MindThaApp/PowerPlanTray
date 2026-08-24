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

    private readonly PowerSchemeService _powerSchemes;
    private readonly AppSettingsService _settings;
    private readonly PowerSourceMonitor _powerSource;
    private readonly AutomationRuleEngine _automation;
    private readonly Action _showSettings;
    private readonly Action _exit;
    private readonly Action<Guid> _switchScheme;
    private readonly AppWindow _appWindow;
    private bool _isShowing;

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

        IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WindowId id = Win32Interop.GetWindowIdFromWindow(hwnd);
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

        long style = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(style | WsExToolWindow));
        TryApplyBackdrop();
        Activated += OnActivated;
        PopupBorder.KeyDown += OnKeyDown;
    }

    public void Show(bool fullMenu)
    {
        ApplyPreferences();
        BuildContent(fullMenu);
        PositionAboveTaskbar();
        _isShowing = true;
        Activate();
        PopupBorder.Focus(FocusState.Programmatic);
    }

    public void Hide()
    {
        _isShowing = false;
        _appWindow.Hide();
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
            .Where(r => r.Enabled && r.Trigger == AutomationTrigger.AppRunning).ToList();
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
        PopupContent.Children.Add(new Expander
        {
            Header = _settings.AutoSwitchBatteryAcEnabled ? "✓  Auto-switch on Battery/AC" : "Auto-switch on Battery/AC",
            Content = panel,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        });
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
        (int width, int height) = _settings.PopupSize switch
        {
            UiSize.Small => (300, 390),
            UiSize.Large => (440, 650),
            _ => (360, 520),
        };
        RECT workArea;
        if (!SystemParametersInfo(SpiGetWorkArea, 0, out workArea, 0))
            workArea = new RECT { Left = 0, Top = 0, Right = GetSystemMetrics(0), Bottom = GetSystemMetrics(1) };
        const int gap = 8;
        _appWindow.MoveAndResize(new RectInt32(workArea.Right - width - gap, workArea.Bottom - height - gap, width, height));
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_isShowing && args.WindowActivationState == WindowActivationState.Deactivated) Hide();
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
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
}
