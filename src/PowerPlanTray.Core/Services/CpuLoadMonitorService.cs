using System.Diagnostics;
using System.Runtime.InteropServices;
using PowerPlanTray.Core.Models;

namespace PowerPlanTray.Core.Services;

/// <summary>Samples total and configured-process CPU every three seconds and debounces rule transitions.</summary>
public sealed class CpuLoadMonitorService : IDisposable
{
    private const int RequiredSamples = 3;
    private readonly Action<AutoSwitchRule> _entered;
    private readonly Action<Guid> _exited;
    private readonly object _sync = new();
    private List<AutoSwitchRule> _rules = new();
    private readonly Dictionary<Guid, int> _matchingSamples = new();
    private readonly HashSet<Guid> _active = new();
    private readonly Dictionary<int, ProcessSample> _processSamples = new();
    private Timer? _timer;
    private int _polling;
    private readonly SystemCpuUsageSampler _cpuSampler = new();

    public event EventHandler<double>? SystemCpuLoadUpdated;

    public CpuLoadMonitorService(Action<AutoSwitchRule> entered, Action<Guid> exited)
    {
        _entered = entered;
        _exited = exited;
    }

    public void UpdateRules(IEnumerable<AutoSwitchRule> rules)
    {
        List<Guid> removedActive;
        lock (_sync)
        {
            _rules = rules.Where(r => r.Enabled && IsCpuTrigger(r.Trigger) && r.CpuThresholdPercent is >= 0 and <= 100)
                .Select(CloneRule).ToList();
            var ids = _rules.Select(r => r.Id).ToHashSet();
            _matchingSamples.Keys.Where(id => !ids.Contains(id)).ToList().ForEach(id => _matchingSamples.Remove(id));
            removedActive = _active.Where(id => !ids.Contains(id)).ToList();
            _active.RemoveWhere(id => !ids.Contains(id));
        }
        foreach (Guid id in removedActive) _exited(id);
    }

    public void Start() => _timer ??= new Timer(Poll, null, TimeSpan.Zero, TimeSpan.FromSeconds(3));
    public void Stop() { _timer?.Dispose(); _timer = null; }

    private void Poll(object? state)
    {
        if (Interlocked.Exchange(ref _polling, 1) != 0) return;
        try
        {
            List<AutoSwitchRule> rules;
            lock (_sync) rules = _rules.Select(CloneRule).ToList();
            double? systemCpu = SampleSystemCpu(); // first sample intentionally yields null
            if (systemCpu.HasValue) SystemCpuLoadUpdated?.Invoke(this, systemCpu.Value);
            Dictionary<string, double> processCpu = SampleProcessCpu(rules);
            foreach (AutoSwitchRule rule in rules)
            {
                double? load = rule.Trigger is AutomationTrigger.SystemCpuBelow or AutomationTrigger.SystemCpuAbove
                    ? systemCpu
                    : processCpu.TryGetValue(rule.AppExecutableName ?? "", out double value) ? value : null;
                Evaluate(rule, load);
            }
        }
        catch { /* transient process/system sampling failures are retried next poll */ }
        finally { Volatile.Write(ref _polling, 0); }
    }

    private void Evaluate(AutoSwitchRule rule, double? load)
    {
        bool condition = load.HasValue && (rule.Trigger is AutomationTrigger.SystemCpuBelow or AutomationTrigger.ProcessCpuBelow
            ? load.Value < rule.CpuThresholdPercent : load.Value > rule.CpuThresholdPercent);
        bool enter = false, exit = false;
        lock (_sync)
        {
            int count = condition ? _matchingSamples.GetValueOrDefault(rule.Id) + 1 : 0;
            _matchingSamples[rule.Id] = Math.Min(count, RequiredSamples);
            if (count >= RequiredSamples && _active.Add(rule.Id)) enter = true;
            else if (!condition && _active.Remove(rule.Id)) exit = true;
        }
        if (enter) _entered(rule);
        if (exit) _exited(rule.Id);
    }

    private double? SampleSystemCpu() => _cpuSampler.Sample();

    private Dictionary<string, double> SampleProcessCpu(IEnumerable<AutoSwitchRule> rules)
    {
        var wanted = rules.Where(r => r.Trigger is AutomationTrigger.ProcessCpuBelow or AutomationTrigger.ProcessCpuAbove)
            .Select(r => r.AppExecutableName).Where(n => !string.IsNullOrWhiteSpace(n)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var totals = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var liveIds = new HashSet<int>();
        DateTime now = DateTime.UtcNow;
        foreach (Process process in Process.GetProcesses())
        {
            using (process)
            {
                string name;
                TimeSpan cpu;
                try { name = process.ProcessName + ".exe"; if (!wanted.Contains(name)) continue; cpu = process.TotalProcessorTime; liveIds.Add(process.Id); }
                catch { continue; }
                if (_processSamples.TryGetValue(process.Id, out ProcessSample old))
                {
                    double wallMs = (now - old.At).TotalMilliseconds;
                    if (wallMs > 0) totals[name] = totals.GetValueOrDefault(name) + Math.Clamp((cpu - old.Cpu).TotalMilliseconds / (wallMs * Environment.ProcessorCount) * 100d, 0, 100);
                }
                _processSamples[process.Id] = new(cpu, now);
            }
        }
        foreach (int id in _processSamples.Keys.Where(id => !liveIds.Contains(id)).ToArray()) _processSamples.Remove(id);
        foreach (string name in totals.Keys.ToArray()) totals[name] = Math.Clamp(totals[name], 0, 100);
        return totals;
    }

    private static bool IsCpuTrigger(AutomationTrigger trigger) => trigger is AutomationTrigger.SystemCpuBelow or AutomationTrigger.SystemCpuAbove or AutomationTrigger.ProcessCpuBelow or AutomationTrigger.ProcessCpuAbove;
    private static AutoSwitchRule CloneRule(AutoSwitchRule r) => new() { Id = r.Id, Trigger = r.Trigger, TargetPlanGuid = r.TargetPlanGuid, AppExecutableName = r.AppExecutableName, CpuThresholdPercent = r.CpuThresholdPercent, Priority = r.Priority, Name = r.Name, Enabled = r.Enabled };
    public void Dispose() => Stop();
    private readonly record struct ProcessSample(TimeSpan Cpu, DateTime At);
}

/// <summary>Shared GetSystemTimes delta-based total system CPU percentage sampler, reused by callers that need a live 0-100 CPU reading.</summary>
internal sealed class SystemCpuUsageSampler
{
    private ulong? _previousIdle, _previousKernel, _previousUser;

    public double? Sample()
    {
        if (!GetSystemTimes(out FILETIME idleFt, out FILETIME kernelFt, out FILETIME userFt)) return null;
        ulong idle = ToUInt64(idleFt), kernel = ToUInt64(kernelFt), user = ToUInt64(userFt);
        if (!_previousIdle.HasValue) { _previousIdle = idle; _previousKernel = kernel; _previousUser = user; return null; }
        ulong idleDelta = idle - _previousIdle.Value;
        ulong totalDelta = kernel - _previousKernel!.Value + user - _previousUser!.Value;
        _previousIdle = idle; _previousKernel = kernel; _previousUser = user;
        return totalDelta == 0 ? null : Math.Clamp(100d * (totalDelta - Math.Min(idleDelta, totalDelta)) / totalDelta, 0, 100);
    }

    private static ulong ToUInt64(FILETIME ft) => ((ulong)ft.High << 32) | ft.Low;
    [StructLayout(LayoutKind.Sequential)] private struct FILETIME { public uint Low, High; }
    [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);
}
