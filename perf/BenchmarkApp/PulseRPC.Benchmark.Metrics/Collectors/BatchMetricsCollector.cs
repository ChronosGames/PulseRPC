using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PulseRPC.Benchmark.Metrics.Abstractions;
using PulseRPC.Benchmark.Metrics.Models;

namespace PulseRPC.Benchmark.Metrics.Collectors;

/// <summary>
/// 批量指标收集器 - 优化大数据量处理
/// </summary>
public class BatchMetricsCollector : IMetricsCollector
{
    private readonly ILogger<BatchMetricsCollector>? _logger;
    private readonly CollectorConfiguration _configuration;
    private readonly ConcurrentQueue<JsonOptimizedMetricsEvent> _pendingMetrics;
    private readonly ConcurrentQueue<JsonOptimizedMetricsSnapshot> _snapshotHistory;
    private readonly Timer? _batchTimer;
    private readonly object _batchLock = new();

    private PluginStatus _status = PluginStatus.NotInitialized;
    private long _sequenceNumber = 0;
    private long _totalMetricsProcessed = 0;
    private CancellationTokenSource? _collectionCancellation;

    public BatchMetricsCollector(
        CollectorConfiguration? configuration = null,
        ILogger<BatchMetricsCollector>? logger = null)
    {
        _configuration = configuration ?? new CollectorConfiguration
        {
            SamplingIntervalMs = 5000, // 默认批处理间隔更长
            BufferSize = 50000,        // 更大的缓冲区
            SnapshotIntervalMs = 30000 // 30秒生成一次快照
        };

        _logger = logger;
        _pendingMetrics = new ConcurrentQueue<JsonOptimizedMetricsEvent>();
        _snapshotHistory = new ConcurrentQueue<JsonOptimizedMetricsSnapshot>();

        // 初始化批处理定时器
        _batchTimer = new Timer(ProcessBatchCallback, null, Timeout.Infinite, Timeout.Infinite);
    }

    #region IMetricsPlugin Implementation

    public string Name => "BatchMetricsCollector";
    public string Version => "1.0.0";
    public string Description => "High-throughput batch metrics collector for large-scale data processing";
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

        // 验证批处理配置
        if (config.SamplingIntervalMs <= 0 || config.BufferSize <= 0)
            return Task.FromResult(false);

        // 批量收集器需要更大的缓冲区
        if (config.BufferSize < 1000)
        {
            _logger?.LogWarning("批量收集器建议使用更大的缓冲区 (>= 1000)");
        }

        return Task.FromResult(true);
    }

    public Task InitializeAsync(object? configuration, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger?.LogInformation("初始化批量指标收集器，缓冲区大小: {BufferSize}", _configuration.BufferSize);
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
        var pendingCount = _pendingMetrics.Count;
        var isHealthy = _status == PluginStatus.Running && pendingCount < _configuration.BufferSize * 0.9;

        var status = isHealthy
            ? PluginHealthStatus.Healthy($"批量收集器运行正常，待处理: {pendingCount}")
            : PluginHealthStatus.Unhealthy($"缓冲区接近满载或收集器未运行", $"状态: {_status}, 待处理: {pendingCount}");

        // 添加详细指标
        status.Metrics["pending_metrics"] = pendingCount;
        status.Metrics["total_processed"] = _totalMetricsProcessed;
        status.Metrics["history_snapshots"] = _snapshotHistory.Count;
        status.Metrics["sequence_number"] = _sequenceNumber;
        status.Metrics["buffer_utilization"] = (double)pendingCount / _configuration.BufferSize;

        return Task.FromResult(status);
    }

    public async ValueTask DisposeAsync()
    {
        await StopCollectionAsync();

        // 处理剩余的指标
        await ProcessPendingMetricsAsync();

        _batchTimer?.Dispose();
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

            // 启动批处理定时器
            _batchTimer?.Change(
                TimeSpan.FromMilliseconds(_configuration.SamplingIntervalMs),
                TimeSpan.FromMilliseconds(_configuration.SamplingIntervalMs));

            ChangeStatus(PluginStatus.Running);
            _logger?.LogInformation("批量指标收集器已启动，批处理间隔: {Interval}ms", _configuration.SamplingIntervalMs);

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            ChangeStatus(PluginStatus.Error);
            OnError(ex, "启动收集失败");
            throw;
        }
    }

    public async Task StopCollectionAsync()
    {
        if (_status != PluginStatus.Running)
        {
            return;
        }

        try
        {
            ChangeStatus(PluginStatus.Stopping);

            // 停止定时器
            _batchTimer?.Change(Timeout.Infinite, Timeout.Infinite);

            // 处理剩余的指标
            await ProcessPendingMetricsAsync();

            // 取消收集任务
            _collectionCancellation?.Cancel();

            ChangeStatus(PluginStatus.Stopped);
            _logger?.LogInformation("批量指标收集器已停止，总处理指标数: {Total}", _totalMetricsProcessed);
        }
        catch (Exception ex)
        {
            ChangeStatus(PluginStatus.Error);
            OnError(ex, "停止收集失败");
            throw;
        }
    }

    public async Task<JsonOptimizedMetricsSnapshot> GetSnapshotAsync()
    {
        // 先处理待处理的指标
        await ProcessPendingMetricsAsync();

        var snapshot = CreateSnapshot();
        return snapshot;
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

    public async Task ClearMetricsAsync()
    {
        lock (_batchLock)
        {
            // 清空待处理指标
            while (_pendingMetrics.TryDequeue(out _)) { }

            // 清空历史快照
            while (_snapshotHistory.TryDequeue(out _)) { }

            _sequenceNumber = 0;
            _totalMetricsProcessed = 0;
        }

        _logger?.LogInformation("已清空所有批量指标数据");
        await Task.CompletedTask;
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

            // 添加到待处理队列
            _pendingMetrics.Enqueue(metricEvent);

            // 检查是否需要立即处理（缓冲区接近满载）
            if (_pendingMetrics.Count > _configuration.BufferSize * 0.8)
            {
                _ = Task.Run(ProcessPendingMetricsAsync);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "记录批量指标失败: {MetricName}", name);
        }
    }

    #endregion

    #region Batch Processing Methods

    private void ProcessBatchCallback(object? state)
    {
        if (!IsRunning) return;

        try
        {
            _ = Task.Run(ProcessPendingMetricsAsync);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "批处理回调失败");
            OnError(ex, "批处理回调失败");
        }
    }

    private async Task ProcessPendingMetricsAsync()
    {
        if (_pendingMetrics.IsEmpty) return;

        var stopwatch = Stopwatch.StartNew();
        var processedCount = 0;
        var batchMetrics = new List<JsonOptimizedMetricsEvent>();

        try
        {
            lock (_batchLock)
            {
                // 批量提取待处理的指标
                while (_pendingMetrics.TryDequeue(out var metric) && batchMetrics.Count < _configuration.BufferSize)
                {
                    batchMetrics.Add(metric);
                }
            }

            if (batchMetrics.Count == 0) return;

            // 批量处理指标
            foreach (var metric in batchMetrics)
            {
                try
                {
                    // 触发单个指标收集事件
                    MetricsCollected?.Invoke(new MetricsCollectedEventArgs(metric, Name));
                    processedCount++;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "处理指标事件失败: {MetricName}", metric.MetricName);
                }
            }

            // 更新统计
            Interlocked.Add(ref _totalMetricsProcessed, processedCount);

            // 如果启用自动快照且处理了足够多的指标，创建快照
            if (_configuration.EnableAutoSnapshot && processedCount > 0)
            {
                var snapshot = CreateSnapshotFromBatch(batchMetrics);

                _snapshotHistory.Enqueue(snapshot);

                // 限制历史记录数量
                while (_snapshotHistory.Count > _configuration.MaxHistorySnapshots)
                {
                    _snapshotHistory.TryDequeue(out _);
                }

                SnapshotCreated?.Invoke(new SnapshotCreatedEventArgs(snapshot));

                _logger?.LogDebug("批处理完成: 处理 {Count} 个指标, 耗时: {Duration}ms, 快照: {Sequence}",
                    processedCount, stopwatch.Elapsed.TotalMilliseconds, snapshot.SequenceNumber);
            }

            stopwatch.Stop();

            if (processedCount > 100) // 只记录大批量处理
            {
                _logger?.LogInformation("批量处理指标: {Count} 个, 耗时: {Duration}ms",
                    processedCount, stopwatch.Elapsed.TotalMilliseconds);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "批量处理指标失败");
            OnError(ex, "批量处理指标失败");
        }

        await Task.CompletedTask;
    }

    private JsonOptimizedMetricsSnapshot CreateSnapshot()
    {
        var stopwatch = Stopwatch.StartNew();
        var snapshot = new JsonOptimizedMetricsSnapshot
        {
            CollectorName = Name,
            SequenceNumber = Interlocked.Increment(ref _sequenceNumber)
        };

        // 为批量收集器，快照主要包含统计信息
        var totalProcessedEvent = new JsonOptimizedMetricsEvent
        {
            MetricName = "collector.total_processed",
            Type = MetricType.Counter,
            Source = Name,
            Unit = "count"
        };
        totalProcessedEvent.SetValue(_totalMetricsProcessed.ToString());
        snapshot.AddMetric("collector.total_processed", totalProcessedEvent);

        var pendingCountEvent = new JsonOptimizedMetricsEvent
        {
            MetricName = "collector.pending_count",
            Type = MetricType.Gauge,
            Source = Name,
            Unit = "count"
        };
        pendingCountEvent.SetValue(_pendingMetrics.Count.ToString());
        snapshot.AddMetric("collector.pending_count", pendingCountEvent);

        stopwatch.Stop();
        snapshot.CollectionDuration = stopwatch.Elapsed;

        return snapshot;
    }

    private JsonOptimizedMetricsSnapshot CreateSnapshotFromBatch(IList<JsonOptimizedMetricsEvent> batchMetrics)
    {
        var stopwatch = Stopwatch.StartNew();
        var snapshot = new JsonOptimizedMetricsSnapshot
        {
            CollectorName = Name,
            SequenceNumber = Interlocked.Increment(ref _sequenceNumber)
        };

        // 添加批次中的所有指标
        for (int i = 0; i < batchMetrics.Count; i++)
        {
            var metric = batchMetrics[i];
            var key = $"{metric.MetricName}_{i}_{metric.Timestamp.Ticks}";
            snapshot.AddMetric(key, metric);
        }

        stopwatch.Stop();
        snapshot.CollectionDuration = stopwatch.Elapsed;

        return snapshot;
    }

    #endregion

    #region Private Methods

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
