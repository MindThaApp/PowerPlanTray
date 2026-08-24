using Windows.System.Power;

namespace PowerPlanTray.Core.Services;

public sealed class PowerSourceMonitor : IDisposable
{
    private bool _started;
    private bool _lastIsOnBattery;

    public event EventHandler<bool>? PowerSourceChanged;

    public bool IsOnBattery => PowerManager.BatteryStatus == BatteryStatus.Discharging;

    public bool HasBattery => PowerManager.BatteryStatus != BatteryStatus.NotPresent;

    public void Start()
    {
        if (_started) return;
        _lastIsOnBattery = IsOnBattery;
        PowerManager.PowerSupplyStatusChanged += OnPowerSupplyStatusChanged;
        _started = true;
    }

    public void Stop()
    {
        if (!_started) return;
        PowerManager.PowerSupplyStatusChanged -= OnPowerSupplyStatusChanged;
        _started = false;
    }

    private void OnPowerSupplyStatusChanged(object? sender, object e)
    {
        bool isOnBattery = IsOnBattery;
        if (!HasBattery || isOnBattery == _lastIsOnBattery) return;
        _lastIsOnBattery = isOnBattery;
        PowerSourceChanged?.Invoke(this, isOnBattery);
    }

    public void Dispose() => Stop();
}
