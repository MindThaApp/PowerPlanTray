using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PowerPlanTray.Core.Services;
using PowerPlanTray.Core;
using PowerPlanTray.Core.Models;
using Windows.ApplicationModel;

namespace PowerPlanTray;

public sealed partial class SettingsWindow : Window
{
    private readonly AppSettingsService _appSettingsService = new();
    private readonly StartupService _startupService = new();
    private readonly PowerSchemeService _powerSchemeService = new();
    private readonly ElevationService _elevationService = new();
    private bool _isInitializing;

    public event EventHandler? PowerPlansChanged;

    public SettingsWindow()
    {
        InitializeComponent();
        Activated += OnFirstActivated;

        // TODO(phase4): add the Automation NavigationViewItem and page.
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
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        string? tag = (args.SelectedItemContainer as NavigationViewItem)?.Tag as string;
        GeneralPage.Visibility = tag == "General" ? Visibility.Visible : Visibility.Collapsed;
        PowerPlansPage.Visibility = tag == "PowerPlans" ? Visibility.Visible : Visibility.Collapsed;

        if (tag == "PowerPlans")
        {
            RefreshPowerPlans();
        }
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
