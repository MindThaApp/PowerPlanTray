using System.Diagnostics;
using PowerPlanTray.Core.Models;

namespace PowerPlanTray.Core.Services;

public sealed class ProcessWatcherService : IDisposable
{
    private const double PollIntervalSeconds = 3;
    private readonly Action<AutoSwitchRule> _appStarted;
    private readonly Action _lastAppStopped;
    private readonly object _sync = new();
    private List<AutoSwitchRule> _rules = new();
    private HashSet<Guid> _runningRuleIds = new();
    private readonly Dictionary<Guid, RuleRuntimeState> _ruleState = new();

    // Ground truth of currently-observed process names, refreshed every poll. UpdateRules()
    // derives running-rule membership from this instead of narrowing the previous
    // _runningRuleIds set (HashSet.IntersectWith can only shrink, never recover an id once
    // lost, which let a rule that's disabled then re-enabled while its app keeps running end
    // up permanently "invisible" to future start/stop edge detection).
    private HashSet<string> _runningProcessNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, ProcessCpuSample> _processCpuSamples = new();
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
        List<AutoSwitchRule> newlyStarted;
        bool restorePlan;
        lock (_sync)
        {
            bool hadRunningRule = _runningRuleIds.Count > 0;
            _rules = rules
                .Where(rule => rule.Trigger == AutomationTrigger.AppRunning &&
                    rule.Enabled && !string.IsNullOrWhiteSpace(rule.AppExecutableName))
                .Select(CloneRule)
                .ToList();

            // No fresh CPU sample is available outside a poll, so CPU-gated rules can only become
            // satisfied on a subsequent Poll(); rules with no extra conditions (or only a
            // minimum-running-time already met before this call) can be recognized immediately.
            HashSet<Guid> current = EvaluateRulesNoLock(_rules, DateTime.UtcNow, cpuPercentByName: null);

            newlyStarted = _rules
                .Where(rule => current.Contains(rule.Id) && !_runningRuleIds.Contains(rule.Id))
                .ToList();
            _runningRuleIds = current;
            restorePlan = hadRunningRule && _runningRuleIds.Count == 0;
        }

        // A rule whose gate is already satisfied when it becomes newly enabled/configured should
        // switch immediately instead of waiting for the next poll.
        foreach (AutoSwitchRule rule in newlyStarted) _appStarted(rule);
        if (restorePlan) _lastAppStopped();
    }

    public void Start() => _timer ??= new Timer(Poll, null, TimeSpan.Zero, TimeSpan.FromSeconds(PollIntervalSeconds));

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;

        // Reset tracked state so a later Start() begins from a clean, accurate slate rather
        // than stale running-rule/process/debounce state left over from before the stop.
        lock (_sync)
        {
            _runningRuleIds.Clear();
            _runningProcessNames.Clear();
            _ruleState.Clear();
            _processCpuSamples.Clear();
        }
    }

    private void Poll(object? state)
    {
        if (Interlocked.Exchange(ref _polling, 1) != 0) return;
        List<Process> processes = new();
        try
        {
            processes = Process.GetProcesses().ToList();
            var processNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Process process in processes)
            {
                try { processNames.Add(process.ProcessName + ".exe"); } catch { /* exited mid-enumeration */ }
            }

            List<AutoSwitchRule> rules;
            HashSet<Guid> previous;
            lock (_sync)
            {
                rules = _rules.Select(CloneRule).ToList();
                previous = new HashSet<Guid>(_runningRuleIds);
            }

            Dictionary<string, double>? cpuPercentByName = rules.Any(rule => rule.AppCpuGateEnabled)
                ? SampleProcessCpu(processes, rules)
                : null;

            HashSet<Guid> current;
            lock (_sync)
            {
                _runningProcessNames = processNames;
                current = EvaluateRulesNoLock(rules, DateTime.UtcNow, cpuPercentByName);
            }

            foreach (AutoSwitchRule rule in rules.Where(rule => current.Contains(rule.Id) && !previous.Contains(rule.Id)))
            {
                _appStarted(rule);
            }

            bool anyStopped = previous.Except(current).Any();

            lock (_sync) _runningRuleIds = current;

            if (anyStopped && current.Count == 0)
            {
                _lastAppStopped();
            }
        }
        catch
        {
            // Processes can exit while being enumerated. The next poll retries.
        }
        finally
        {
            foreach (Process process in processes) process.Dispose();
            Volatile.Write(ref _polling, 0);
        }
    }

    /// <summary>
    /// Must be called with <see cref="_sync"/> held. Updates each rule's running-since/CPU-debounce
    /// state (pruning state for rules no longer configured) and returns the set of rule ids whose
    /// full gate -- presence, plus an optional minimum-running-time or CPU-usage debounce -- is
    /// currently satisfied.
    /// </summary>
    private HashSet<Guid> EvaluateRulesNoLock(List<AutoSwitchRule> rules, DateTime nowUtc, Dictionary<string, double>? cpuPercentByName)
    {
        HashSet<Guid> ids = rules.Select(rule => rule.Id).ToHashSet();
        foreach (Guid staleId in _ruleState.Keys.Where(id => !ids.Contains(id)).ToList())
            _ruleState.Remove(staleId);

        var satisfied = new HashSet<Guid>();
        foreach (AutoSwitchRule rule in rules)
        {
            bool isRunning = _runningProcessNames.Contains(rule.AppExecutableName!);
            _ruleState.TryGetValue(rule.Id, out RuleRuntimeState runtime);

            if (!isRunning)
            {
                _ruleState[rule.Id] = runtime with { RunningSinceUtc = null, MatchingCpuSamples = 0 };
                continue;
            }

            if (runtime.RunningSinceUtc is null) runtime = runtime with { RunningSinceUtc = nowUtc };

            bool gateSatisfied;
            if (rule.AppCpuGateEnabled)
            {
                double? load = cpuPercentByName is not null &&
                    cpuPercentByName.TryGetValue(rule.AppExecutableName!, out double value) ? value : null;
                bool matches = load.HasValue && load.Value > rule.CpuThresholdPercent;
                double sustainedSeconds = rule.SustainedSeconds > 0 ? rule.SustainedSeconds : PollIntervalSeconds;
                int requiredSamples = Math.Max(1, (int)Math.Ceiling(sustainedSeconds / PollIntervalSeconds));
                int count = matches ? runtime.MatchingCpuSamples + 1 : 0;
                runtime = runtime with { MatchingCpuSamples = Math.Min(count, requiredSamples) };
                gateSatisfied = count >= requiredSamples;
            }
            else
            {
                gateSatisfied = rule.SustainedSeconds <= 0 ||
                    (nowUtc - runtime.RunningSinceUtc!.Value).TotalSeconds >= rule.SustainedSeconds;
            }

            _ruleState[rule.Id] = runtime;
            if (gateSatisfied) satisfied.Add(rule.Id);
        }

        return satisfied;
    }

    private Dictionary<string, double> SampleProcessCpu(List<Process> processes, List<AutoSwitchRule> rules)
    {
        var wanted = rules
            .Where(rule => rule.AppCpuGateEnabled && !string.IsNullOrWhiteSpace(rule.AppExecutableName))
            .Select(rule => rule.AppExecutableName!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var totals = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (wanted.Count == 0) return totals;

        var liveIds = new HashSet<int>();
        DateTime now = DateTime.UtcNow;
        lock (_sync)
        {
            foreach (Process process in processes)
            {
                string name;
                int id;
                TimeSpan cpu;
                try
                {
                    name = process.ProcessName + ".exe";
                    if (!wanted.Contains(name)) continue;
                    id = process.Id;
                    cpu = process.TotalProcessorTime;
                    liveIds.Add(id);
                }
                catch { continue; }

                if (_processCpuSamples.TryGetValue(id, out ProcessCpuSample old))
                {
                    double wallMs = (now - old.At).TotalMilliseconds;
                    if (wallMs > 0)
                    {
                        totals[name] = totals.GetValueOrDefault(name) +
                            Math.Clamp((cpu - old.Cpu).TotalMilliseconds / (wallMs * Environment.ProcessorCount) * 100d, 0, 100);
                    }
                }
                _processCpuSamples[id] = new ProcessCpuSample(cpu, now);
            }
            foreach (int id in _processCpuSamples.Keys.Where(id => !liveIds.Contains(id)).ToArray())
                _processCpuSamples.Remove(id);
        }

        foreach (string name in totals.Keys.ToArray()) totals[name] = Math.Clamp(totals[name], 0, 100);
        return totals;
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
        Priority = rule.Priority,
        SustainedSeconds = rule.SustainedSeconds,
        AppCpuGateEnabled = rule.AppCpuGateEnabled,
    };

    public void Dispose() => Stop();

    private readonly record struct RuleRuntimeState(DateTime? RunningSinceUtc, int MatchingCpuSamples);
    private readonly record struct ProcessCpuSample(TimeSpan Cpu, DateTime At);
}
