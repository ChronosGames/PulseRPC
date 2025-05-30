using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PulseRPC.Benchmark.Metrics.Abstractions;
using PulseRPC.Benchmark.Metrics.Models;

namespace PulseRPC.Benchmark.Metrics.Collectors;

/// <summary>
/// 实时指标收集器
/// </summary>
public class RealTimeMetricsCollector : IMetricsCollector
{
    private readonly ILogger<RealTimeMetricsCollector>? _logger;
    private readonly CollectorConfiguration _configuration;
    private readonly ConcurrentQueue<JsonOptimizedMetricsEvent> _metricsBuffer;
    private readonly ConcurrentQueue<JsonOptimizedMetricsSnapshot> _snapshotHistory;
    private readonly Timer? _samplingTimer;
    private readonly Timer? _snapshotTimer;
    private readonly object _lock = new();

    private PluginStatus _status = PluginStatus.NotInitialized;
    private long _sequenceNumber = 0;
    private CancellationTokenSource? _collectionCancellation;

    public RealTimeMetricsCollector(
        CollectorConfiguration? configuration = null,
        ILogger<RealTimeMetricsCollector>? logger = null)
    {
        _configuration = configuration ?? new CollectorConfiguration();
        _logger = logger;
        _metricsBuffer = new ConcurrentQueue<JsonOptimizedMetricsEvent>();
        _snapshotHistory = new ConcurrentQueue<JsonOptimizedMetricsSnapshot>();

        // 初始化定时器（但不启动）
        if (_configuration.EnableAutoSnapshot)
        {
            _snapshotTimer = new Timer(CreateSnapshotCallback, null, Timeout.Infinite, Timeout.Infinite);
        }
    }

    #region IMetricsPlugin Implementation

    public string Name => "RealTimeMetricsCollector";
    public string Version => "1.0.0";
    public string Description => "High-frequency real-time metrics collector";
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

        // 验证采样间隔
        if (config.SamplingIntervalMs <= 0)
            return Task.FromResult(false);

        // 验证缓冲区大小
        if (config.BufferSize <= 0)
            return Task.FromResult(false);

        return Task.FromResult(true);
    }

    public Task InitializeAsync(object? configuration, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger?.LogInformation("初始化实时指标收集器");
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
            ? PluginHealthStatus.Healthy($"收集器运行正常，缓冲区: {_metricsBuffer.Count}")
            : PluginHealthStatus.Unhealthy("收集器未运行", $"当前状态: {_status}");

        // 添加性能指标
        status.Metrics["buffer_size"] = _metricsBuffer.Count;
        status.Metrics["history_snapshots"] = _snapshotHistory.Count;
        status.Metrics["sequence_number"] = _sequenceNumber;

        return Task.FromResult(status);
    }

    public async ValueTask DisposeAsync()
    {
        await StopCollectionAsync();
        _samplingTimer?.Dispose();
        _snapshotTimer?.Dispose();
        _collectionCancellation?.Dispose();
        ChangeStatus(PluginStatus.Disposed);
    }

    #endregion

    #region IMetricsCollector Implementation

    public Task StartCollectionAsync(CancellationToken cancellationToken = default)
    {
        if (_status == PluginStatus.Running)
        {
            _logger?.LogWarning("收集器已在运行");
            return Task.CompletedTask;
        }

        try
        {
            ChangeStatus(PluginStatus.Starting);

            _collectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // 启动自动快照定时器
            if (_configuration.EnableAutoSnapshot && _snapshotTimer != null)
            {
                _snapshotTimer.Change(
                    TimeSpan.FromMilliseconds(_configuration.SnapshotIntervalMs),
                    TimeSpan.FromMilliseconds(_configuration.SnapshotIntervalMs));
            }

            // 如果配置了收集系统指标，启动系统监控
            if (_configuration.CollectSystemMetrics)
            {
                _ = Task.Run(CollectSystemMetricsAsync, _collectionCancellation.Token);
            }

            ChangeStatus(PluginStatus.Running);
            _logger?.LogInformation("实时指标收集器已启动");

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            ChangeStatus(PluginStatus.Error);
            OnError(ex, "启动收集失败");
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
            _snapshotTimer?.Change(Timeout.Infinite, Timeout.Infinite);

            // 取消收集任务
            _collectionCancellation?.Cancel();

            ChangeStatus(PluginStatus.Stopped);
            _logger?.LogInformation("实时指标收集器已停止");

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            ChangeStatus(PluginStatus.Error);
            OnError(ex, "停止收集失败");
            throw;
        }
    }

    public Task<JsonOptimizedMetricsSnapshot> GetSnapshotAsync()
    {
        var snapshot = CreateSnapshot();
        return Task.FromResult(snapshot);
    }

    public Task<List<JsonOptimizedMetricsSnapshot>> GetSnapshotHistoryAsync(int count = 10)
    {
        var history = new List<JsonOptimizedMetricsSnapshot>();
        var snapshots = _snapshotHistory.ToArray();

        // 获取最新的指定数量快照
        var takeCount = Math.Min(count, snapshots.Length);
        for (int i = snapshots.Length - takeCount; i < snapshots.Length; i++)
        {
            history.Add(snapshots[i]);
        }

        return Task.FromResult(history);
    }

    public Task ClearMetricsAsync()
    {
        lock (_lock)
        {
            // 清空缓冲区
            while (_metricsBuffer.TryDequeue(out _)) { }

            // 清空历史快照
            while (_snapshotHistory.TryDequeue(out _)) { }

            _sequenceNumber = 0;
        }

        _logger?.LogInformation("已清空所有指标数据");
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

            // 应用过滤器
            if (ShouldFilterMetric(metricEvent))
            {
                return;
            }

            // 添加到缓冲区
            _metricsBuffer.Enqueue(metricEvent);

            // 限制缓冲区大小
            while (_metricsBuffer.Count > _configuration.BufferSize)
            {
                _metricsBuffer.TryDequeue(out _);
            }

            // 触发事件
            MetricsCollected?.Invoke(new MetricsCollectedEventArgs(metricEvent, Name));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "记录指标失败: {MetricName}", name);
        }
    }

    #endregion

    #region Private Methods

    private JsonOptimizedMetricsSnapshot CreateSnapshot()
    {
        var stopwatch = Stopwatch.StartNew();
        var snapshot = new JsonOptimizedMetricsSnapshot
        {
            CollectorName = Name,
            SequenceNumber = Interlocked.Increment(ref _sequenceNumber)
        };

        // 将缓冲区中的指标添加到快照
        var currentMetrics = _metricsBuffer.ToArray();
        foreach (var metric in currentMetrics)
        {
            var key = $"{metric.MetricName}_{metric.Timestamp.Ticks}";
            snapshot.AddMetric(key, metric);
        }

        stopwatch.Stop();
        snapshot.CollectionDuration = stopwatch.Elapsed;

        return snapshot;
    }

    private void CreateSnapshotCallback(object? state)
    {
        if (!IsRunning) return;

        try
        {
            var snapshot = CreateSnapshot();

            // 添加到历史记录
            _snapshotHistory.Enqueue(snapshot);

            // 限制历史记录数量
            while (_snapshotHistory.Count > _configuration.MaxHistorySnapshots)
            {
                _snapshotHistory.TryDequeue(out _);
            }

            // 触发事件
            SnapshotCreated?.Invoke(new SnapshotCreatedEventArgs(snapshot));

            _logger?.LogDebug("创建快照: {Sequence}, 指标数: {Count}",
                snapshot.SequenceNumber, snapshot.MetricsCount);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "创建快照失败");
            OnError(ex, "创建快照失败");
        }
    }

    private async Task CollectSystemMetricsAsync()
    {
        var process = Process.GetCurrentProcess();

        while (!_collectionCancellation?.Token.IsCancellationRequested == true)
        {
            try
            {
                // CPU使用率（简化版本）
                var cpuTime = process.TotalProcessorTime;
                RecordMetric("system.cpu.process_time", cpuTime.TotalMilliseconds, MetricType.Gauge,
                    new Dictionary<string, string> { ["process"] = process.ProcessName }, "ms");

                // 内存使用
                var workingSet = process.WorkingSet64;
                RecordMetric("system.memory.working_set", workingSet, MetricType.Gauge,
                    new Dictionary<string, string> { ["process"] = process.ProcessName }, "bytes");

                var privateMemory = process.PrivateMemorySize64;
                RecordMetric("system.memory.private", privateMemory, MetricType.Gauge,
                    new Dictionary<string, string> { ["process"] = process.ProcessName }, "bytes");

                // GC统计
                for (int gen = 0; gen <= 2; gen++)
                {
                    var gcCount = GC.CollectionCount(gen);
                    RecordMetric($"system.gc.collection_count_gen{gen}", gcCount, MetricType.Counter,
                        new Dictionary<string, string> { ["generation"] = gen.ToString() });
                }

                var totalMemory = GC.GetTotalMemory(false);
                RecordMetric("system.gc.total_memory", totalMemory, MetricType.Gauge, unit: "bytes");

                // 线程数
                var threadCount = process.Threads.Count;
                RecordMetric("system.threads.count", threadCount, MetricType.Gauge,
                    new Dictionary<string, string> { ["process"] = process.ProcessName });

                await Task.Delay(_configuration.SamplingIntervalMs, _collectionCancellation!.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "收集系统指标失败");
                await Task.Delay(1000, _collectionCancellation?.Token ?? CancellationToken.None);
            }
        }
    }

    private bool ShouldFilterMetric(JsonOptimizedMetricsEvent metricEvent)
    {
        // 应用指标名称过滤器
        if (_configuration.MetricFilters.Count > 0)
        {
            var nameMatches = _configuration.MetricFilters.Any(filter =>
                metricEvent.MetricName.Contains(filter, StringComparison.OrdinalIgnoreCase));
            if (!nameMatches) return true;
        }

        // 应用标签过滤器
        if (_configuration.TagFilters.Count > 0)
        {
            var tagMatches = _configuration.TagFilters.All(filter =>
                metricEvent.Tags.TryGetValue(filter.Key, out var value) &&
                value.Equals(filter.Value, StringComparison.OrdinalIgnoreCase));
            if (!tagMatches) return true;
        }

        return false;
    }

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
