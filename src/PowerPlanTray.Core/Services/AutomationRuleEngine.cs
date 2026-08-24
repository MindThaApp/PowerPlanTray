using PowerPlanTray.Core.Models;

namespace PowerPlanTray.Core.Services;

public sealed record TimedSwitchInfo(Guid TargetPlanGuid, DateTimeOffset EndTime)
{
    public TimeSpan Remaining => EndTime > DateTimeOffset.Now
        ? EndTime - DateTimeOffset.Now
        : TimeSpan.Zero;
}

public sealed class AutomationRuleEngine : IDisposable
{
    private readonly PowerSchemeService _powerSchemeService;
    private readonly PowerSourceMonitor _powerSourceMonitor;
    private readonly AppSettingsService _appSettingsService;
    private readonly ProcessWatcherService _processWatcher;
    private readonly Stack<Guid> _restorePlans = new();
    private readonly object _sync = new();
    private CancellationTokenSource? _timedSwitchCancellation;
    private long _timedSwitchGeneration;
    private bool _started;

    public event EventHandler? TimedSwitchStateChanged;

    public TimedSwitchInfo? CurrentTimedSwitch { get; private set; }

    public AutomationRuleEngine(
        PowerSchemeService powerSchemeService,
        PowerSourceMonitor powerSourceMonitor,
        AppSettingsService appSettingsService)
    {
        _powerSchemeService = powerSchemeService;
        _powerSourceMonitor = powerSourceMonitor;
        _appSettingsService = appSettingsService;
        _processWatcher = new ProcessWatcherService(OnAppStarted, OnLastAppStopped);
    }

    public void Start()
    {
        if (_started) return;
        _powerSourceMonitor.PowerSourceChanged += OnPowerSourceChanged;
        _powerSourceMonitor.Start();
        RefreshConfiguration(applyCurrentPowerState: true);
        _started = true;
    }

    public void RefreshConfiguration(bool applyCurrentPowerState = false)
    {
        List<AutoSwitchRule> rules = _appSettingsService.GetAutomationRules();
        _processWatcher.UpdateRules(rules);
        if (rules.Any(rule => rule.Trigger == AutomationTrigger.AppRunning && rule.Enabled))
            _processWatcher.Start();
        else
            _processWatcher.Stop();

        if (applyCurrentPowerState && _appSettingsService.AutoSwitchBatteryAcEnabled)
            ApplyPowerSource(_powerSourceMonitor.IsOnBattery);
    }

    public async Task<bool> ApplyTimedSwitchAsync(
        Guid targetPlanGuid,
        TimeSpan duration,
        CancellationToken ct = default)
    {
        if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));

        CancellationTokenSource cancellation;
        long generation;
        lock (_sync)
        {
            CancelTimedSwitchCore();
            _restorePlans.Push(_powerSchemeService.GetActiveSchemeGuid());
            _powerSchemeService.SetActiveScheme(targetPlanGuid);
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _timedSwitchCancellation = cancellation;
            generation = ++_timedSwitchGeneration;
            CurrentTimedSwitch = new TimedSwitchInfo(targetPlanGuid, DateTimeOffset.Now.Add(duration));
        }
        TimedSwitchStateChanged?.Invoke(this, EventArgs.Empty);

        bool completed = true;
        try
        {
            await Task.Delay(duration, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            completed = false;
        }

        bool changed = false;
        lock (_sync)
        {
            if (generation == _timedSwitchGeneration && CurrentTimedSwitch is not null)
            {
                RestoreTopPlan();
                CurrentTimedSwitch = null;
                _timedSwitchCancellation?.Dispose();
                _timedSwitchCancellation = null;
                changed = true;
            }
        }
        if (changed) TimedSwitchStateChanged?.Invoke(this, EventArgs.Empty);
        return completed;
    }

    public void CancelTimedSwitch()
    {
        bool changed;
        lock (_sync) changed = CancelTimedSwitchCore();
        if (changed) TimedSwitchStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool CancelTimedSwitchCore()
    {
        if (CurrentTimedSwitch is null) return false;
        ++_timedSwitchGeneration;
        _timedSwitchCancellation?.Cancel();
        _timedSwitchCancellation?.Dispose();
        _timedSwitchCancellation = null;
        RestoreTopPlan();
        CurrentTimedSwitch = null;
        return true;
    }

    private void OnPowerSourceChanged(object? sender, bool isOnBattery)
    {
        if (_appSettingsService.AutoSwitchBatteryAcEnabled) ApplyPowerSource(isOnBattery);
    }

    private void ApplyPowerSource(bool isOnBattery)
    {
        Guid? planGuid = isOnBattery
            ? _appSettingsService.BatteryPlanGuid
            : _appSettingsService.AcPlanGuid;
        if (planGuid.HasValue) _powerSchemeService.SetActiveScheme(planGuid.Value);
    }

    private void OnAppStarted(AutoSwitchRule rule)
    {
        lock (_sync)
        {
            _restorePlans.Push(_powerSchemeService.GetActiveSchemeGuid());
            _powerSchemeService.SetActiveScheme(rule.TargetPlanGuid);
        }
    }

    private void OnLastAppStopped()
    {
        lock (_sync) RestoreTopPlan();
    }

    private void RestoreTopPlan()
    {
        if (_restorePlans.TryPop(out Guid restorePlan))
            _powerSchemeService.SetActiveScheme(restorePlan);
    }

    public void Dispose()
    {
        _powerSourceMonitor.PowerSourceChanged -= OnPowerSourceChanged;
        _powerSourceMonitor.Stop();
        _processWatcher.Dispose();
        _timedSwitchCancellation?.Cancel();
        _timedSwitchCancellation?.Dispose();
    }
}
