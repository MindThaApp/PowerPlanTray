using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PowerPlanTray.Core.Services;
using Windows.ApplicationModel;

namespace PowerPlanTray;

public sealed partial class SettingsWindow : Window
{
    private readonly AppSettingsService _appSettingsService = new();
    private readonly StartupService _startupService = new();
    private bool _isInitializing;

    public SettingsWindow()
    {
        InitializeComponent();
        Activated += OnFirstActivated;

        // TODO(phase3): add the Power Plans NavigationViewItem and page.
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
