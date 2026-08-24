using System.Diagnostics;
using PowerPlanTray.Core.Models;

namespace PowerPlanTray.Core.Services;

public sealed class ProcessWatcherService : IDisposable
{
    private readonly Action<AutoSwitchRule> _appStarted;
    private readonly Action _lastAppStopped;
    private readonly object _sync = new();
    private List<AutoSwitchRule> _rules = new();
    private HashSet<Guid> _runningRuleIds = new();
    private Timer? _timer;
    private int _polling;

    public ProcessWatcherService(
        Action<AutoSwitchRule> appStarted,
        Action lastAppStopped)
    {
        _appStarted = appStarted;
        _lastAppStopped = lastAppStopped;
    }

    public void UpdateRules(IEnumerable<AutoSwitchRule> rules)
    {
        lock (_sync)
        {
            _rules = rules
                .Where(rule => rule.Trigger == AutomationTrigger.AppRunning &&
                    rule.Enabled && !string.IsNullOrWhiteSpace(rule.AppExecutableName))
                .Select(CloneRule)
                .ToList();
            _runningRuleIds.IntersectWith(_rules.Select(rule => rule.Id));
        }
    }

    public void Start() => _timer ??= new Timer(Poll, null, TimeSpan.Zero, TimeSpan.FromSeconds(3));

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void Poll(object? state)
    {
        if (Interlocked.Exchange(ref _polling, 1) != 0) return;
        try
        {
            HashSet<string> processNames = Process.GetProcesses()
                .Select(process =>
                {
                    try { return process.ProcessName + ".exe"; }
                    finally { process.Dispose(); }
                })
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            List<AutoSwitchRule> rules;
            HashSet<Guid> previous;
            lock (_sync)
            {
                rules = _rules.Select(CloneRule).ToList();
                previous = new HashSet<Guid>(_runningRuleIds);
            }

            var current = rules
                .Where(rule => processNames.Contains(rule.AppExecutableName!))
                .Select(rule => rule.Id)
                .ToHashSet();

            foreach (AutoSwitchRule rule in rules.Where(rule => current.Contains(rule.Id) && !previous.Contains(rule.Id)))
            {
                _appStarted(rule);
            }

            bool anyStopped = previous.Except(current).Any();
            if (anyStopped && current.Count == 0)
            {
                _lastAppStopped();
            }

            lock (_sync) _runningRuleIds = current;
        }
        catch
        {
            // Processes can exit while being enumerated. The next poll retries.
        }
        finally
        {
            Volatile.Write(ref _polling, 0);
        }
    }

    private static AutoSwitchRule CloneRule(AutoSwitchRule rule) => new()
    {
        Id = rule.Id,
        Trigger = rule.Trigger,
        TargetPlanGuid = rule.TargetPlanGuid,
        AppExecutableName = rule.AppExecutableName,
        Name = rule.Name,
        Enabled = rule.Enabled,
        CpuThresholdPercent = rule.CpuThresholdPercent,
    };

    public void Dispose() => Stop();
}
