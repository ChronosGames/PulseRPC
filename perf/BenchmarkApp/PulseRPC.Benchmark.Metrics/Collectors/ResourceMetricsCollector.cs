using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PulseRPC.Benchmark.Metrics.Abstractions;
using PulseRPC.Benchmark.Metrics.Models;

namespace PulseRPC.Benchmark.Metrics.Collectors;

/// <summary>
/// 资源监控收集器 - 专门收集系统资源指标
/// </summary>
public class ResourceMetricsCollector : IMetricsCollector
{
    private readonly ILogger<ResourceMetricsCollector>? _logger;
    private readonly CollectorConfiguration _configuration;
    private readonly ConcurrentQueue<JsonOptimizedMetricsSnapshot> _snapshotHistory;
    private readonly Timer? _resourceTimer;
    private readonly Process _currentProcess;

    private PluginStatus _status = PluginStatus.NotInitialized;
    private long _sequenceNumber = 0;
    private CancellationTokenSource? _collectionCancellation;

    // 资源监控状态
    private long _lastProcessorTime = 0;
    private DateTime _lastCpuMeasurement = DateTime.UtcNow;

    public ResourceMetricsCollector(
        CollectorConfiguration? configuration = null,
        ILogger<ResourceMetricsCollector>? logger = null)
    {
        _configuration = configuration ?? new CollectorConfiguration
        {
            SamplingIntervalMs = 2000,   // 2秒采样一次
            MaxHistorySnapshots = 500,   // 保留更多历史数据
            EnableAutoSnapshot = true,
            SnapshotIntervalMs = 2000    // 与采样间隔一致
        };

        _logger = logger;
        _snapshotHistory = new ConcurrentQueue<JsonOptimizedMetricsSnapshot>();
        _currentProcess = Process.GetCurrentProcess();

        // 初始化资源监控定时器
        _resourceTimer = new Timer(CollectResourceMetricsCallback, null, Timeout.Infinite, Timeout.Infinite);
    }

    #region IMetricsPlugin Implementation

    public string Name => "ResourceMetricsCollector";
    public string Version => "1.0.0";
    public string Description => "System resource metrics collector for CPU, memory, disk and network monitoring";
    public string Author => "PulseRPC";
    public bool IsInitialized => _status >= PluginStatus.Initialized;
    public bool IsRunning => _status == PluginStatus.Running;

    public CollectorConfiguration Configuration => _configuration;

    public event Action<PluginStatusChangedEventArgs>? StatusChanged;
    public event Action<PluginErrorEventArgs>? ErrorOccurred;
    public event Action<MetricsCollectedEventArgs>? MetricsCollected;
    public event Action<SnapshotCreatedEventArgs>? SnapshotCreated;

    public Task<bool> ValidateConfigurationAsync(object? configuration)
    {
        if (configuration is not CollectorConfiguration config)
            return Task.FromResult(false);

        // 资源监控不宜采样过频繁
        if (config.SamplingIntervalMs < 1000)
        {
            _logger?.LogWarning("资源监控建议采样间隔不少于1秒");
        }

        return Task.FromResult(true);
    }

    public Task InitializeAsync(object? configuration, CancellationToken cancellationToken = default)
    {
        try
        {
            // 初始化基线数据
            _lastProcessorTime = _currentProcess.TotalProcessorTime.Ticks;
            _lastCpuMeasurement = DateTime.UtcNow;

            _logger?.LogInformation("初始化资源监控收集器，采样间隔: {Interval}ms", _configuration.SamplingIntervalMs);
            ChangeStatus(PluginStatus.Initialized);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            ChangeStatus(PluginStatus.Error);
            OnError(ex, "初始化失败");
            throw;
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await StartCollectionAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await StopCollectionAsync();
    }

    public Task<PluginHealthStatus> GetHealthStatusAsync()
    {
        var status = _status == PluginStatus.Running
            ? PluginHealthStatus.Healthy($"资源监控运行正常，已收集 {_snapshotHistory.Count} 个快照")
            : PluginHealthStatus.Unhealthy("资源监控未运行", $"当前状态: {_status}");

        // 添加当前资源指标
        try
        {
            var workingSet = _currentProcess.WorkingSet64;
            var privateMemory = _currentProcess.PrivateMemorySize64;
            var cpuTime = _currentProcess.TotalProcessorTime;

            status.Metrics["working_set_mb"] = workingSet / 1024 / 1024;
            status.Metrics["private_memory_mb"] = privateMemory / 1024 / 1024;
            status.Metrics["cpu_time_ms"] = cpuTime.TotalMilliseconds;
            status.Metrics["thread_count"] = _currentProcess.Threads.Count;
            status.Metrics["handle_count"] = _currentProcess.HandleCount;
            status.Metrics["snapshots_count"] = _snapshotHistory.Count;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "获取健康状态指标失败");
        }

        return Task.FromResult(status);
    }

    public async ValueTask DisposeAsync()
    {
        await StopCollectionAsync();
        _resourceTimer?.Dispose();
        _currentProcess?.Dispose();
        _collectionCancellation?.Dispose();
        ChangeStatus(PluginStatus.Disposed);
    }

    #endregion

    #region IMetricsCollector Implementation

    public Task StartCollectionAsync(CancellationToken cancellationToken = default)
    {
        if (_status == PluginStatus.Running)
        {
            _logger?.LogWarning("资源监控收集器已在运行");
            return Task.CompletedTask;
        }

        try
        {
            ChangeStatus(PluginStatus.Starting);

            _collectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // 启动资源监控定时器
            _resourceTimer?.Change(
                TimeSpan.FromMilliseconds(100), // 首次延迟100ms
                TimeSpan.FromMilliseconds(_configuration.SamplingIntervalMs));

            ChangeStatus(PluginStatus.Running);
            _logger?.LogInformation("资源监控收集器已启动");

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            ChangeStatus(PluginStatus.Error);
            OnError(ex, "启动资源监控失败");
            throw;
        }
    }

    public Task StopCollectionAsync()
    {
        if (_status != PluginStatus.Running)
        {
            return Task.CompletedTask;
        }

        try
        {
            ChangeStatus(PluginStatus.Stopping);

            // 停止定时器
            _resourceTimer?.Change(Timeout.Infinite, Timeout.Infinite);

            // 取消收集任务
            _collectionCancellation?.Cancel();

            ChangeStatus(PluginStatus.Stopped);
            _logger?.LogInformation("资源监控收集器已停止");

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            ChangeStatus(PluginStatus.Error);
            OnError(ex, "停止资源监控失败");
            throw;
        }
    }

    public Task<JsonOptimizedMetricsSnapshot> GetSnapshotAsync()
    {
        var snapshot = CollectCurrentResourceSnapshot();
        return Task.FromResult(snapshot);
    }

    public Task<List<JsonOptimizedMetricsSnapshot>> GetSnapshotHistoryAsync(int count = 10)
    {
        var history = new List<JsonOptimizedMetricsSnapshot>();
        var snapshots = _snapshotHistory.ToArray();

        var takeCount = Math.Min(count, snapshots.Length);
        for (int i = snapshots.Length - takeCount; i < snapshots.Length; i++)
        {
            history.Add(snapshots[i]);
        }

        return Task.FromResult(history);
    }

    public Task ClearMetricsAsync()
    {
        // 清空历史快照
        while (_snapshotHistory.TryDequeue(out _)) { }

        _sequenceNumber = 0;

        _logger?.LogInformation("已清空资源监控历史数据");
        return Task.CompletedTask;
    }

    public void RecordMetric(string name, object value, MetricType type = MetricType.Custom, Dictionary<string, string>? tags = null, string unit = "")
    {
        if (!IsRunning) return;

        try
        {
            var metricEvent = new JsonOptimizedMetricsEvent
            {
                MetricName = name,
                Type = type,
                Source = Name,
                Unit = unit,
                Tags = tags ?? new Dictionary<string, string>()
            };

            metricEvent.SetValue(value.ToString() ?? string.Empty);

            // 触发事件
            MetricsCollected?.Invoke(new MetricsCollectedEventArgs(metricEvent, Name));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "记录资源指标失败: {MetricName}", name);
        }
    }

    #endregion

    #region Resource Monitoring Methods

    private void CollectResourceMetricsCallback(object? state)
    {
        if (!IsRunning) return;

        try
        {
            var snapshot = CollectCurrentResourceSnapshot();

            // 添加到历史记录
            _snapshotHistory.Enqueue(snapshot);

            // 限制历史记录数量
            while (_snapshotHistory.Count > _configuration.MaxHistorySnapshots)
            {
                _snapshotHistory.TryDequeue(out _);
            }

            // 触发快照创建事件
            SnapshotCreated?.Invoke(new SnapshotCreatedEventArgs(snapshot));

            _logger?.LogDebug("收集资源快照: {Sequence}, 指标数: {Count}",
                snapshot.SequenceNumber, snapshot.MetricsCount);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "收集资源指标失败");
            OnError(ex, "收集资源指标失败");
        }
    }

    private JsonOptimizedMetricsSnapshot CollectCurrentResourceSnapshot()
    {
        var stopwatch = Stopwatch.StartNew();
        var snapshot = new JsonOptimizedMetricsSnapshot
        {
            CollectorName = Name,
            SequenceNumber = Interlocked.Increment(ref _sequenceNumber)
        };

        try
        {
            // 收集进程CPU指标
            CollectCpuMetrics(snapshot);

            // 收集内存指标
            CollectMemoryMetrics(snapshot);

            // 收集进程信息
            CollectProcessMetrics(snapshot);

            // 收集GC指标
            CollectGcMetrics(snapshot);

            // 收集线程指标
            CollectThreadMetrics(snapshot);

        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "收集资源指标时出现错误");
        }

        stopwatch.Stop();
        snapshot.CollectionDuration = stopwatch.Elapsed;

        return snapshot;
    }

    private void CollectCpuMetrics(JsonOptimizedMetricsSnapshot snapshot)
    {
        try
        {
            var now = DateTime.UtcNow;
            var currentProcessorTime = _currentProcess.TotalProcessorTime.Ticks;

            // 计算CPU使用率百分比
            if (_lastCpuMeasurement != DateTime.MinValue)
            {
                var elapsedTime = now - _lastCpuMeasurement;
                var elapsedProcessorTime = currentProcessorTime - _lastProcessorTime;

                if (elapsedTime.TotalMilliseconds > 0)
                {
                    var cpuUsagePercent = (elapsedProcessorTime / (double)elapsedTime.Ticks) * 100;
                    AddMetricToSnapshot(snapshot, "resource.cpu.usage_percent", cpuUsagePercent, MetricType.Gauge, "%");
                }
            }

            _lastProcessorTime = currentProcessorTime;
            _lastCpuMeasurement = now;

            // 总CPU时间
            AddMetricToSnapshot(snapshot, "resource.cpu.total_time", _currentProcess.TotalProcessorTime.TotalMilliseconds, MetricType.Counter, "ms");
            AddMetricToSnapshot(snapshot, "resource.cpu.user_time", _currentProcess.UserProcessorTime.TotalMilliseconds, MetricType.Counter, "ms");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "收集CPU指标失败");
        }
    }

    private void CollectMemoryMetrics(JsonOptimizedMetricsSnapshot snapshot)
    {
        try
        {
            // 进程内存使用
            AddMetricToSnapshot(snapshot, "resource.memory.working_set", _currentProcess.WorkingSet64, MetricType.Gauge, "bytes");
            AddMetricToSnapshot(snapshot, "resource.memory.private_bytes", _currentProcess.PrivateMemorySize64, MetricType.Gauge, "bytes");
            AddMetricToSnapshot(snapshot, "resource.memory.virtual_bytes", _currentProcess.VirtualMemorySize64, MetricType.Gauge, "bytes");
            AddMetricToSnapshot(snapshot, "resource.memory.paged_bytes", _currentProcess.PagedMemorySize64, MetricType.Gauge, "bytes");
            AddMetricToSnapshot(snapshot, "resource.memory.peak_working_set", _currentProcess.PeakWorkingSet64, MetricType.Gauge, "bytes");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "收集内存指标失败");
        }
    }

    private void CollectProcessMetrics(JsonOptimizedMetricsSnapshot snapshot)
    {
        try
        {
            AddMetricToSnapshot(snapshot, "resource.process.id", _currentProcess.Id, MetricType.Gauge);
            AddMetricToSnapshot(snapshot, "resource.process.handle_count", _currentProcess.HandleCount, MetricType.Gauge);

            // 启动时间
            var uptime = DateTime.UtcNow - _currentProcess.StartTime.ToUniversalTime();
            AddMetricToSnapshot(snapshot, "resource.process.uptime", uptime.TotalSeconds, MetricType.Gauge, "seconds");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "收集进程指标失败");
        }
    }

    private void CollectGcMetrics(JsonOptimizedMetricsSnapshot snapshot)
    {
        try
        {
            // GC统计
            for (int gen = 0; gen <= 2; gen++)
            {
                var collectionCount = GC.CollectionCount(gen);
                AddMetricToSnapshot(snapshot, $"resource.gc.collection_count_gen{gen}", collectionCount, MetricType.Counter,
                    tags: new Dictionary<string, string> { ["generation"] = gen.ToString() });
            }

            var totalMemory = GC.GetTotalMemory(false);
            AddMetricToSnapshot(snapshot, "resource.gc.total_memory", totalMemory, MetricType.Gauge, "bytes");

            // 尝试获取更详细的GC信息
            try
            {
                var gcInfo = GC.GetGCMemoryInfo();
                AddMetricToSnapshot(snapshot, "resource.gc.heap_size", gcInfo.HeapSizeBytes, MetricType.Gauge, "bytes");
                AddMetricToSnapshot(snapshot, "resource.gc.memory_load", gcInfo.MemoryLoadBytes, MetricType.Gauge, "bytes");
                AddMetricToSnapshot(snapshot, "resource.gc.total_available_memory", gcInfo.TotalAvailableMemoryBytes, MetricType.Gauge, "bytes");
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "获取详细GC信息失败");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "收集GC指标失败");
        }
    }

    private void CollectThreadMetrics(JsonOptimizedMetricsSnapshot snapshot)
    {
        try
        {
            var threadCount = _currentProcess.Threads.Count;
            AddMetricToSnapshot(snapshot, "resource.threads.count", threadCount, MetricType.Gauge);

            // 线程池信息
            ThreadPool.GetAvailableThreads(out int workerThreads, out int completionPortThreads);
            ThreadPool.GetMaxThreads(out int maxWorkerThreads, out int maxCompletionPortThreads);

            AddMetricToSnapshot(snapshot, "resource.threadpool.available_worker_threads", workerThreads, MetricType.Gauge);
            AddMetricToSnapshot(snapshot, "resource.threadpool.available_completion_port_threads", completionPortThreads, MetricType.Gauge);
            AddMetricToSnapshot(snapshot, "resource.threadpool.max_worker_threads", maxWorkerThreads, MetricType.Gauge);
            AddMetricToSnapshot(snapshot, "resource.threadpool.max_completion_port_threads", maxCompletionPortThreads, MetricType.Gauge);

            var usedWorkerThreads = maxWorkerThreads - workerThreads;
            var usedCompletionPortThreads = maxCompletionPortThreads - completionPortThreads;

            AddMetricToSnapshot(snapshot, "resource.threadpool.used_worker_threads", usedWorkerThreads, MetricType.Gauge);
            AddMetricToSnapshot(snapshot, "resource.threadpool.used_completion_port_threads", usedCompletionPortThreads, MetricType.Gauge);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "收集线程指标失败");
        }
    }

    private void AddMetricToSnapshot(JsonOptimizedMetricsSnapshot snapshot, string name, object value, MetricType type, string unit = "", Dictionary<string, string>? tags = null)
    {
        var metricEvent = new JsonOptimizedMetricsEvent
        {
            MetricName = name,
            Type = type,
            Source = Name,
            Unit = unit,
            Tags = tags ?? new Dictionary<string, string>()
        };

        metricEvent.SetValue(value.ToString() ?? string.Empty);

        // 使用时间戳确保唯一性
        var key = $"{name}_{metricEvent.Timestamp.Ticks}";
        snapshot.AddMetric(key, metricEvent);

        // 触发实时事件
        MetricsCollected?.Invoke(new MetricsCollectedEventArgs(metricEvent, Name));
    }

    #endregion

    #region Private Methods

    private void ChangeStatus(PluginStatus newStatus)
    {
        var oldStatus = _status;
        _status = newStatus;

        if (oldStatus != newStatus)
        {
            StatusChanged?.Invoke(new PluginStatusChangedEventArgs(Name, oldStatus, newStatus));
        }
    }

    private void OnError(Exception exception, string context)
    {
        ErrorOccurred?.Invoke(new PluginErrorEventArgs(Name, exception, ErrorLevel.Error, context));
    }

    #endregion
}
