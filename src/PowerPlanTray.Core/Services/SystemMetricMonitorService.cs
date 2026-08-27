using System.Diagnostics;
using System.Runtime.InteropServices;
using PowerPlanTray.Core.Models;

namespace PowerPlanTray.Core.Services;

/// <summary>
/// Samples exactly one Task-Manager-Performance-tab-style metric (0-100%) at a time on a
/// ~1 second cadence, matching Task Manager's default refresh rate. Only the counters for the
/// currently selected metric are kept open, to avoid unnecessary PDH overhead.
/// </summary>
public sealed class SystemMetricMonitorService : IDisposable
{
    private readonly object _sync = new();
    private readonly SystemCpuUsageSampler _cpuSampler = new();
    private Timer? _timer;
    private TrayGaugeMetric _metric = TrayGaugeMetric.Cpu;
    private int _sampling;
    private PerformanceCounter? _diskCounter;
    private PerformanceCounter[]? _networkBytesCounters;
    private PerformanceCounter[]? _networkBandwidthCounters;
    private Dictionary<string, PerformanceCounter>? _gpuCounters;

    public event EventHandler<double>? MetricUpdated;

    public void SetMetric(TrayGaugeMetric metric)
    {
        lock (_sync)
        {
            if (_metric == metric) return;
            _metric = metric;
            DisposeCounters();
        }
    }

    public void Start() => _timer ??= new Timer(Poll, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        lock (_sync) DisposeCounters();
    }

    private void Poll(object? state)
    {
        if (Interlocked.Exchange(ref _sampling, 1) != 0) return;
        try
        {
            TrayGaugeMetric metric;
            lock (_sync) metric = _metric;
            double? value = metric switch
            {
                TrayGaugeMetric.Cpu => _cpuSampler.Sample(),
                TrayGaugeMetric.Memory => SampleMemory(),
                TrayGaugeMetric.Disk => SampleDisk(),
                TrayGaugeMetric.Network => SampleNetwork(),
                TrayGaugeMetric.Gpu => SampleGpu(),
                _ => null,
            };
            if (value.HasValue) MetricUpdated?.Invoke(this, value.Value);
        }
        catch { /* transient counter failures are retried next poll */ }
        finally { Volatile.Write(ref _sampling, 0); }
    }

    private static double? SampleMemory()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref status) ? status.dwMemoryLoad : null;
    }

    private double? SampleDisk()
    {
        try
        {
            _diskCounter ??= new PerformanceCounter("PhysicalDisk", "% Disk Time", "_Total", readOnly: true);
            return Math.Clamp(_diskCounter.NextValue(), 0, 100);
        }
        catch { return 0; }
    }

    private double? SampleNetwork()
    {
        try
        {
            if (_networkBytesCounters is null)
            {
                var category = new PerformanceCounterCategory("Network Interface");
                string[] instances = category.GetInstanceNames();
                _networkBytesCounters = instances.Select(name => new PerformanceCounter("Network Interface", "Bytes Total/sec", name, readOnly: true)).ToArray();
                _networkBandwidthCounters = instances.Select(name => new PerformanceCounter("Network Interface", "Current Bandwidth", name, readOnly: true)).ToArray();
            }
            double bytesPerSec = 0, bandwidthBitsPerSec = 0;
            foreach (PerformanceCounter counter in _networkBytesCounters)
                try { bytesPerSec += counter.NextValue(); } catch { }
            foreach (PerformanceCounter counter in _networkBandwidthCounters!)
                try { bandwidthBitsPerSec += counter.NextValue(); } catch { }
            if (bandwidthBitsPerSec <= 0) return 0;
            double bandwidthBytesPerSec = bandwidthBitsPerSec / 8d;
            return Math.Clamp(100d * bytesPerSec / bandwidthBytesPerSec, 0, 100);
        }
        catch { return 0; }
    }

    // "GPU Engine" isn't present on every machine (e.g. some VMs/older drivers); treat that,
    // or any sampling failure, as "unavailable" and report 0 instead of throwing.
    private double? SampleGpu()
    {
        try
        {
            if (!PerformanceCounterCategory.Exists("GPU Engine")) return 0;
            var category = new PerformanceCounterCategory("GPU Engine");
            string[] instances = category.GetInstanceNames()
                .Where(name => name.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            _gpuCounters ??= new Dictionary<string, PerformanceCounter>();
            foreach (string stale in _gpuCounters.Keys.Where(name => !instances.Contains(name)).ToArray())
            {
                _gpuCounters[stale].Dispose();
                _gpuCounters.Remove(stale);
            }
            double total = 0;
            foreach (string name in instances)
            {
                if (!_gpuCounters.TryGetValue(name, out PerformanceCounter? counter))
                {
                    counter = new PerformanceCounter("GPU Engine", "Utilization Percentage", name, readOnly: true);
                    _gpuCounters[name] = counter;
                }
                try { total += counter.NextValue(); } catch { }
            }
            return Math.Clamp(total, 0, 100);
        }
        catch { return 0; }
    }

    private void DisposeCounters()
    {
        _diskCounter?.Dispose();
        _diskCounter = null;
        foreach (PerformanceCounter? counter in _networkBytesCounters ?? Array.Empty<PerformanceCounter>()) counter.Dispose();
        foreach (PerformanceCounter? counter in _networkBandwidthCounters ?? Array.Empty<PerformanceCounter>()) counter.Dispose();
        _networkBytesCounters = null;
        _networkBandwidthCounters = null;
        if (_gpuCounters is not null)
        {
            foreach (PerformanceCounter counter in _gpuCounters.Values) counter.Dispose();
            _gpuCounters = null;
        }
    }

    public void Dispose() => Stop();

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
}
