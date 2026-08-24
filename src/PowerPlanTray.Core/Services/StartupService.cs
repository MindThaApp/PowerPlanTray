using Windows.ApplicationModel;

namespace PowerPlanTray.Core.Services;

public sealed class StartupService
{
    public const string TaskId = "PowerPlanTrayStartup";

    public async Task<bool> IsEnabledAsync()
    {
        StartupTask startupTask = await StartupTask.GetAsync(TaskId);
        return startupTask.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
    }

    public async Task<StartupTaskState> RequestEnableAsync()
    {
        StartupTask startupTask = await StartupTask.GetAsync(TaskId);
        return await startupTask.RequestEnableAsync();
    }

    public void Disable()
    {
        StartupTask startupTask = StartupTask.GetAsync(TaskId).AsTask().GetAwaiter().GetResult();
        startupTask.Disable();
    }
}
