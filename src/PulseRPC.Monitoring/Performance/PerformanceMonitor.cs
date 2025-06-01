using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseRPC.Monitoring.Metrics;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Management;

namespace PulseRPC.Monitoring.Performance;

/// <summary>
/// 性能监控器 - 监控系统性能和资源使用情况
/// </summary>
public class PerformanceMonitor : BackgroundService
{
    private readonly IMetricsCollector _metricsCollector;
    private readonly PerformanceMonitorOptions _options;
    private readonly ILogger<PerformanceMonitor> _logger;

    // 性能计数器
    private readonly PerformanceCounter? _cpuCounter;
    private readonly PerformanceCounter? _memoryCounter;
    private readonly PerformanceCounter? _diskCounter;
    private readonly PerformanceCounter? _networkCounter;

    // 进程相关信息
    private readonly Process _currentProcess;
    private long _lastGcGen0Count;
    private long _lastGcGen1Count;
    private long _lastGcGen2Count;
    private DateTime _lastSampleTime;

    public PerformanceMonitor(
        IMetricsCollector metricsCollector,
        IOptions<PerformanceMonitorOptions> options,
        ILogger<PerformanceMonitor> logger)
    {
        _metricsCollector = metricsCollector;
        _options = options.Value;
        _logger = logger;
        _currentProcess = Process.GetCurrentProcess();
        _lastSampleTime = DateTime.UtcNow;

        // 初始化性能计数器（仅Windows）
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _memoryCounter = new PerformanceCounter("Memory", "Available MBytes");
                _diskCounter = new PerformanceCounter("PhysicalDisk", "% Disk Time", "_Total");
                _networkCounter = new PerformanceCounter("Network Interface", "Bytes Total/sec", "*");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "无法初始化性能计数器，将使用基础监控");
            }
        }

        // 初始化GC计数
        _lastGcGen0Count = GC.CollectionCount(0);
        _lastGcGen1Count = GC.CollectionCount(1);
        _lastGcGen2Count = GC.CollectionCount(2);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("性能监控器已启动，采样间隔: {Interval}", _options.SamplingInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CollectSystemMetrics();
                await CollectProcessMetrics();
                await CollectGarbageCollectionMetrics();
                await CollectThreadMetrics();

                if (_options.EnableDetailedMetrics)
                {
                    await CollectDetailedSystemMetrics();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "收集性能指标时发生错误");
            }

            await Task.Delay(_options.SamplingInterval, stoppingToken);
        }
    }

    /// <summary>
    /// 收集系统级别指标
    /// </summary>
    private async Task CollectSystemMetrics()
    {
        var tags = new Dictionary<string, string>
        {
            ["host"] = Environment.MachineName,
            ["platform"] = RuntimeInformation.OSDescription
        };

        try
        {
            // CPU 使用率
            if (_cpuCounter != null)
            {
                var cpuUsage = _cpuCounter.NextValue();
                _metricsCollector.GetGauge("system_cpu_usage_percent", "System CPU usage percentage", tags)
                    .Set(cpuUsage);
            }

            // 内存使用情况
            var totalMemory = GC.GetTotalMemory(false);
            _metricsCollector.GetGauge("system_memory_used_bytes", "System memory used in bytes", tags)
                .Set(totalMemory);

            if (_memoryCounter != null)
            {
                var availableMemory = _memoryCounter.NextValue() * 1024 * 1024; // MB to bytes
                _metricsCollector.GetGauge("system_memory_available_bytes", "System available memory in bytes", tags)
                    .Set(availableMemory);
            }

            // 磁盘使用率
            if (_diskCounter != null)
            {
                var diskUsage = _diskCounter.NextValue();
                _metricsCollector.GetGauge("system_disk_usage_percent", "System disk usage percentage", tags)
                    .Set(diskUsage);
            }

            // 网络流量
            if (_networkCounter != null)
            {
                var networkBytes = _networkCounter.NextValue();
                _metricsCollector.GetGauge("system_network_bytes_per_second", "System network bytes per second", tags)
                    .Set(networkBytes);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "收集系统指标失败");
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// 收集进程级别指标
    /// </summary>
    private async Task CollectProcessMetrics()
    {
        var tags = new Dictionary<string, string>
        {
            ["process"] = _currentProcess.ProcessName,
            ["pid"] = _currentProcess.Id.ToString()
        };

        try
        {
            // 刷新进程信息
            _currentProcess.Refresh();

            // CPU 时间
            var totalCpuTime = _currentProcess.TotalProcessorTime.TotalMilliseconds;
            _metricsCollector.GetGauge("process_cpu_time_total_ms", "Process total CPU time in milliseconds", tags)
                .Set(totalCpuTime);

            // 内存使用
            var workingSet = _currentProcess.WorkingSet64;
            var privateMemory = _currentProcess.PrivateMemorySize64;
            var virtualMemory = _currentProcess.VirtualMemorySize64;

            _metricsCollector.GetGauge("process_working_set_bytes", "Process working set in bytes", tags)
                .Set(workingSet);
            _metricsCollector.GetGauge("process_private_memory_bytes", "Process private memory in bytes", tags)
                .Set(privateMemory);
            _metricsCollector.GetGauge("process_virtual_memory_bytes", "Process virtual memory in bytes", tags)
                .Set(virtualMemory);

            // 线程数
            var threadCount = _currentProcess.Threads.Count;
            _metricsCollector.GetGauge("process_thread_count", "Process thread count", tags)
                .Set(threadCount);

            // 句柄数
            var handleCount = _currentProcess.HandleCount;
            _metricsCollector.GetGauge("process_handle_count", "Process handle count", tags)
                .Set(handleCount);

            // 启动时间
            var uptime = DateTime.UtcNow - _currentProcess.StartTime.ToUniversalTime();
            _metricsCollector.GetGauge("process_uptime_seconds", "Process uptime in seconds", tags)
                .Set(uptime.TotalSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "收集进程指标失败");
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// 收集垃圾回收指标
    /// </summary>
    private async Task CollectGarbageCollectionMetrics()
    {
        var tags = new Dictionary<string, string>
        {
            ["generation"] = "all"
        };

        try
        {
            // GC 总内存
            var totalMemory = GC.GetTotalMemory(false);
            _metricsCollector.GetGauge("gc_total_memory_bytes", "GC total memory in bytes", tags)
                .Set(totalMemory);

            // GC 收集次数
            var gen0Count = GC.CollectionCount(0);
            var gen1Count = GC.CollectionCount(1);
            var gen2Count = GC.CollectionCount(2);

            var gen0Delta = gen0Count - _lastGcGen0Count;
            var gen1Delta = gen1Count - _lastGcGen1Count;
            var gen2Delta = gen2Count - _lastGcGen2Count;

            var gen0Tags = new Dictionary<string, string>(tags) { ["generation"] = "0" };
            var gen1Tags = new Dictionary<string, string>(tags) { ["generation"] = "1" };
            var gen2Tags = new Dictionary<string, string>(tags) { ["generation"] = "2" };

            _metricsCollector.GetCounter("gc_collections_total", "Total GC collections", gen0Tags)
                .Increment(gen0Delta);
            _metricsCollector.GetCounter("gc_collections_total", "Total GC collections", gen1Tags)
                .Increment(gen1Delta);
            _metricsCollector.GetCounter("gc_collections_total", "Total GC collections", gen2Tags)
                .Increment(gen2Delta);

            // 更新上次计数
            _lastGcGen0Count = gen0Count;
            _lastGcGen1Count = gen1Count;
            _lastGcGen2Count = gen2Count;

            // 堆大小信息
            if (_options.EnableDetailedMetrics)
            {
                try
                {
                    var memoryInfo = GC.GetGCMemoryInfo();

                    _metricsCollector.GetGauge("gc_heap_size_bytes", "GC heap size in bytes", tags)
                        .Set(memoryInfo.HeapSizeBytes);
                    _metricsCollector.GetGauge("gc_memory_load_bytes", "GC memory load in bytes", tags)
                        .Set(memoryInfo.MemoryLoadBytes);
                    _metricsCollector.GetGauge("gc_total_available_memory_bytes", "GC total available memory in bytes", tags)
                        .Set(memoryInfo.TotalAvailableMemoryBytes);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "无法获取详细GC信息");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "收集GC指标失败");
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// 收集线程池指标
    /// </summary>
    private async Task CollectThreadMetrics()
    {
        var tags = new Dictionary<string, string>
        {
            ["pool"] = "threadpool"
        };

        try
        {
            // 线程池信息
            ThreadPool.GetAvailableThreads(out var availableWorkerThreads, out var availableCompletionPortThreads);
            ThreadPool.GetMaxThreads(out var maxWorkerThreads, out var maxCompletionPortThreads);

            var busyWorkerThreads = maxWorkerThreads - availableWorkerThreads;
            var busyCompletionPortThreads = maxCompletionPortThreads - availableCompletionPortThreads;

            var workerTags = new Dictionary<string, string>(tags) { ["type"] = "worker" };
            var ioTags = new Dictionary<string, string>(tags) { ["type"] = "io" };

            _metricsCollector.GetGauge("threadpool_available_threads", "Available threadpool threads", workerTags)
                .Set(availableWorkerThreads);
            _metricsCollector.GetGauge("threadpool_available_threads", "Available threadpool threads", ioTags)
                .Set(availableCompletionPortThreads);

            _metricsCollector.GetGauge("threadpool_busy_threads", "Busy threadpool threads", workerTags)
                .Set(busyWorkerThreads);
            _metricsCollector.GetGauge("threadpool_busy_threads", "Busy threadpool threads", ioTags)
                .Set(busyCompletionPortThreads);

            _metricsCollector.GetGauge("threadpool_max_threads", "Max threadpool threads", workerTags)
                .Set(maxWorkerThreads);
            _metricsCollector.GetGauge("threadpool_max_threads", "Max threadpool threads", ioTags)
                .Set(maxCompletionPortThreads);

            // 队列长度
            var queueLength = ThreadPool.ThreadCount;
            _metricsCollector.GetGauge("threadpool_queue_length", "Threadpool queue length", tags)
                .Set(queueLength);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "收集线程池指标失败");
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// 收集详细系统指标
    /// </summary>
    private async Task CollectDetailedSystemMetrics()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return; // 详细指标目前仅支持 Windows
        }

        var tags = new Dictionary<string, string>
        {
            ["host"] = Environment.MachineName
        };

        try
        {
            // 使用 WMI 获取更详细的系统信息
            await CollectWmiMetrics(tags);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "收集详细系统指标失败");
        }
    }

    /// <summary>
    /// 通过 WMI 收集系统指标
    /// </summary>
    private async Task CollectWmiMetrics(Dictionary<string, string> tags)
    {
        try
        {
            // CPU 温度和频率
            using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Processor"))
            {
                foreach (ManagementObject obj in searcher.Get())
                {
                    var maxClockSpeed = Convert.ToDouble(obj["MaxClockSpeed"] ?? 0) * 1000000; // MHz to Hz
                    var currentClockSpeed = Convert.ToDouble(obj["CurrentClockSpeed"] ?? 0) * 1000000;

                    _metricsCollector.GetGauge("system_cpu_max_frequency_hz", "System CPU max frequency in Hz", tags)
                        .Set(maxClockSpeed);
                    _metricsCollector.GetGauge("system_cpu_current_frequency_hz", "System CPU current frequency in Hz", tags)
                        .Set(currentClockSpeed);
                }
            }

            // 磁盘空间
            var drives = DriveInfo.GetDrives().Where(d => d.IsReady);
            foreach (var drive in drives)
            {
                var driveTags = new Dictionary<string, string>(tags)
                {
                    ["drive"] = drive.Name.Replace("\\", "")
                };

                _metricsCollector.GetGauge("system_disk_total_bytes", "System disk total bytes", driveTags)
                    .Set(drive.TotalSize);
                _metricsCollector.GetGauge("system_disk_free_bytes", "System disk free bytes", driveTags)
                    .Set(drive.AvailableFreeSpace);
                _metricsCollector.GetGauge("system_disk_used_bytes", "System disk used bytes", driveTags)
                    .Set(drive.TotalSize - drive.AvailableFreeSpace);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "WMI 指标收集失败");
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// 获取性能摘要
    /// </summary>
    /// <returns>性能摘要</returns>
    public async Task<PerformanceSummary> GetPerformanceSummaryAsync()
    {
        var snapshot = _metricsCollector.GetSnapshot();

        return new PerformanceSummary
        {
            Timestamp = DateTime.UtcNow,
            ProcessId = _currentProcess.Id,
            ProcessName = _currentProcess.ProcessName,
            Uptime = DateTime.UtcNow - _currentProcess.StartTime.ToUniversalTime(),
            CpuUsage = GetGaugeValue(snapshot, "system_cpu_usage_percent"),
            MemoryUsage = GetGaugeValue(snapshot, "process_working_set_bytes"),
            ThreadCount = (int)GetGaugeValue(snapshot, "process_thread_count"),
            HandleCount = (int)GetGaugeValue(snapshot, "process_handle_count"),
            GcGen0Collections = GetCounterValue(snapshot, "gc_collections_total", new Dictionary<string, string> { ["generation"] = "0" }),
            GcGen1Collections = GetCounterValue(snapshot, "gc_collections_total", new Dictionary<string, string> { ["generation"] = "1" }),
            GcGen2Collections = GetCounterValue(snapshot, "gc_collections_total", new Dictionary<string, string> { ["generation"] = "2" }),
            TotalMemoryBytes = (long)GetGaugeValue(snapshot, "gc_total_memory_bytes")
        };
    }

    private double GetGaugeValue(MetricsSnapshot snapshot, string name)
    {
        return snapshot.Gauges.FirstOrDefault(g => g.Name == name)?.Value ?? 0;
    }

    private double GetCounterValue(MetricsSnapshot snapshot, string name, Dictionary<string, string> tags)
    {
        return snapshot.Counters
            .FirstOrDefault(c => c.Name == name &&
                                 tags.All(tag => c.Tags.ContainsKey(tag.Key) && c.Tags[tag.Key] == tag.Value))?.Value ?? 0;
    }

    public override void Dispose()
    {
        _cpuCounter?.Dispose();
        _memoryCounter?.Dispose();
        _diskCounter?.Dispose();
        _networkCounter?.Dispose();
        _currentProcess?.Dispose();
        base.Dispose();
    }
}

/// <summary>
/// 性能摘要
/// </summary>
public class PerformanceSummary
{
    public DateTime Timestamp { get; set; }
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public TimeSpan Uptime { get; set; }
    public double CpuUsage { get; set; }
    public double MemoryUsage { get; set; }
    public int ThreadCount { get; set; }
    public int HandleCount { get; set; }
    public double GcGen0Collections { get; set; }
    public double GcGen1Collections { get; set; }
    public double GcGen2Collections { get; set; }
    public long TotalMemoryBytes { get; set; }
}
