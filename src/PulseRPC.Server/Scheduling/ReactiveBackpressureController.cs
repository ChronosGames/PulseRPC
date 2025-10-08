using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PulseRPC.Server.Scheduling;

/// <summary>
/// 系统负载指标
/// </summary>
public record SystemLoadMetrics(double CpuUsage, double MemoryUsage, long PendingRequests, double ResponseTime);

/// <summary>
/// 响应式背压控制器 - 基于系统负载动态调整并发度和限流
/// 支持优先级队列和自适应限流策略
/// </summary>
public sealed class ReactiveBackpressureController : IAsyncDisposable
{
    private readonly ILogger _logger;
    private readonly BackpressureOptions _options;

    // 并发控制
    private readonly SemaphoreSlim[] _prioritySemaphores;
    private readonly int[] _priorityLimits;
    private readonly int[] _currentConcurrency;

    // 令牌桶限流
    private readonly TokenBucket[] _priorityTokenBuckets;

    // 负载监控
    private readonly Timer _metricsTimer;
    private SystemLoadMetrics _currentMetrics;
    private readonly CircularBuffer<SystemLoadMetrics> _metricsHistory;

    // 自适应控制
    private readonly AdaptiveController _adaptiveController;

    // 统计信息
    private long _totalRequests;
    private long _totalRejections;
    private long _totalTimeouts;
    private readonly long[] _priorityRequests = new long[5];
    private readonly long[] _priorityRejections = new long[5];

    /// <summary>
    /// 背压控制选项
    /// </summary>
    public sealed class BackpressureOptions
    {
        /// <summary>各优先级的基础并发限制</summary>
        public int[] BaseConcurrencyLimits { get; set; } = new[] { 100, 80, 50, 30, 10 };

        /// <summary>各优先级的令牌桶容量</summary>
        public int[] TokenBucketCapacities { get; set; } = new[] { 1000, 800, 500, 300, 100 };

        /// <summary>各优先级的令牌补充速率（每秒）</summary>
        public double[] TokenRefillRates { get; set; } = new[] { 500.0, 400.0, 250.0, 150.0, 50.0 };

        /// <summary>负载采样间隔</summary>
        public TimeSpan MetricsSamplingInterval { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>历史指标保留数量</summary>
        public int MetricsHistorySize { get; set; } = 60; // 1分钟历史

        /// <summary>CPU使用率阈值</summary>
        public double CpuThreshold { get; set; } = 0.8; // 80%

        /// <summary>内存使用率阈值</summary>
        public double MemoryThreshold { get; set; } = 0.85; // 85%

        /// <summary>响应时间阈值（毫秒）</summary>
        public double ResponseTimeThreshold { get; set; } = 1000; // 1秒

        /// <summary>是否启用自适应调整</summary>
        public bool EnableAdaptiveControl { get; set; } = true;

        /// <summary>自适应调整因子</summary>
        public double AdaptiveFactor { get; set; } = 0.1;

        /// <summary>最小并发度</summary>
        public int MinConcurrency { get; set; } = 5;

        /// <summary>最大并发度</summary>
        public int MaxConcurrency { get; set; } = 1000;
    }

    /// <summary>
    /// 令牌桶实现
    /// </summary>
    private sealed class TokenBucket
    {
        private readonly double _capacity;
        private readonly double _refillRate;
        private double _tokens;
        private long _lastRefillTime;
        private readonly object _lock = new object();

        public TokenBucket(double capacity, double refillRate)
        {
            _capacity = capacity;
            _refillRate = refillRate;
            _tokens = capacity;
            _lastRefillTime = Environment.TickCount64;
        }

        public bool TryConsume(int tokens = 1)
        {
            lock (_lock)
            {
                RefillTokens();

                if (_tokens >= tokens)
                {
                    _tokens -= tokens;
                    return true;
                }

                return false;
            }
        }

        private void RefillTokens()
        {
            var now = Environment.TickCount64;
            var elapsed = (now - _lastRefillTime) / 1000.0; // 转换为秒

            if (elapsed > 0)
            {
                var tokensToAdd = elapsed * _refillRate;
                _tokens = Math.Min(_capacity, _tokens + tokensToAdd);
                _lastRefillTime = now;
            }
        }

        public double CurrentTokens => _tokens;
    }

    /// <summary>
    /// 循环缓冲区用于历史指标
    /// </summary>
    private sealed class CircularBuffer<T>
    {
        private readonly T[] _buffer;
        private readonly int _capacity;
        private volatile int _head;
        private volatile int _count;

        public CircularBuffer(int capacity)
        {
            _capacity = capacity;
            _buffer = new T[capacity];
        }

        public void Add(T item)
        {
            lock (_buffer)
            {
                _buffer[_head] = item;
                _head = (_head + 1) % _capacity;

                if (_count < _capacity)
                    _count++;
            }
        }

        public T[] GetSnapshot()
        {
            lock (_buffer)
            {
                var snapshot = new T[_count];

                if (_count == 0)
                    return snapshot;

                var start = (_head - _count + _capacity) % _capacity;

                for (var i = 0; i < _count; i++)
                {
                    snapshot[i] = _buffer[(start + i) % _capacity];
                }

                return snapshot;
            }
        }
    }

    /// <summary>
    /// 自适应控制器
    /// </summary>
    private sealed class AdaptiveController
    {
        private readonly BackpressureOptions _options;
        private readonly int[] _baseLimits;
        private readonly ILogger _logger;

        public AdaptiveController(BackpressureOptions options, ILogger logger)
        {
            _options = options;
            _baseLimits = (int[])options.BaseConcurrencyLimits.Clone();
            _logger = logger;
        }

        public void AdjustLimits(int[] currentLimits, SystemLoadMetrics metrics, SystemLoadMetrics[] history)
        {
            var loadFactor = CalculateLoadFactor(metrics, history);

            for (var i = 0; i < currentLimits.Length; i++)
            {
                var baseLimit = _baseLimits[i];
                var adjustment = (int)(baseLimit * loadFactor * _options.AdaptiveFactor);

                var newLimit = baseLimit - adjustment;
                newLimit = Math.Max(_options.MinConcurrency, Math.Min(_options.MaxConcurrency, newLimit));

                if (Math.Abs(currentLimits[i] - newLimit) > 1)
                {
                    _logger.LogDebug("调整优先级{Priority}并发限制: {Old} -> {New}, 负载因子: {LoadFactor:F2}",
                        (MessagePriority)i, currentLimits[i], newLimit, loadFactor);

                    currentLimits[i] = newLimit;
                }
            }
        }

        private static double CalculateLoadFactor(SystemLoadMetrics current, SystemLoadMetrics[] history)
        {
            // 综合多个指标计算负载因子
            var cpuFactor = Math.Max(0, current.CpuUsage - 0.5) * 2; // CPU > 50%时开始影响
            var memoryFactor = Math.Max(0, current.MemoryUsage - 0.7) * 3.33; // Memory > 70%时开始影响
            var responseFactor = Math.Max(0, (current.ResponseTime - 100) / 1000); // 响应时间 > 100ms时开始影响

            // 计算趋势因子
            var trendFactor = 0.0;
            if (history.Length >= 5)
            {
                var recentAvg = 0.0;
                var olderAvg = 0.0;
                var half = history.Length / 2;

                for (var i = 0; i < half; i++)
                {
                    olderAvg += history[i].CpuUsage;
                }

                for (var i = half; i < history.Length; i++)
                {
                    recentAvg += history[i].CpuUsage;
                }

                recentAvg /= (history.Length - half);
                olderAvg /= half;

                trendFactor = Math.Max(0, (recentAvg - olderAvg) * 2); // 上升趋势增加负载因子
            }

            return Math.Min(1.0, cpuFactor + memoryFactor + responseFactor + trendFactor);
        }
    }

    /// <summary>
    /// 构造函数
    /// </summary>
    public ReactiveBackpressureController(BackpressureOptions? options = null, ILogger? logger = null)
    {
        _options = options ?? new BackpressureOptions();
        _logger = logger ?? NullLogger.Instance;

        // 初始化并发控制
        _prioritySemaphores = new SemaphoreSlim[5];
        _priorityLimits = (int[])_options.BaseConcurrencyLimits.Clone();
        _currentConcurrency = new int[5];
        _currentMetrics = new SystemLoadMetrics(0, 0, 0, 0);

        for (var i = 0; i < 5; i++)
        {
            _prioritySemaphores[i] = new SemaphoreSlim(_priorityLimits[i], _priorityLimits[i]);
        }

        // 初始化令牌桶
        _priorityTokenBuckets = new TokenBucket[5];
        for (var i = 0; i < 5; i++)
        {
            _priorityTokenBuckets[i] = new TokenBucket(
                _options.TokenBucketCapacities[i],
                _options.TokenRefillRates[i]);
        }

        // 初始化监控
        _metricsHistory = new CircularBuffer<SystemLoadMetrics>(_options.MetricsHistorySize);
        _adaptiveController = new AdaptiveController(_options, _logger);

        // 启动指标采样定时器
        _metricsTimer = new Timer(CollectMetrics, null, TimeSpan.Zero, _options.MetricsSamplingInterval);

        _logger.LogInformation("ReactiveBackpressureController已初始化 - 选项: {Options}", _options);
    }

    /// <summary>
    /// 尝试获取处理许可 - 主要API
    /// </summary>
    public async ValueTask<IAsyncDisposable?> TryAcquireAsync(MessagePriority priority,
        CancellationToken cancellationToken = default, TimeSpan timeout = default)
    {
        var priorityIndex = (int)priority;
        Interlocked.Increment(ref _priorityRequests[priorityIndex]);
        Interlocked.Increment(ref _totalRequests);

        // 首先检查令牌桶
        if (!_priorityTokenBuckets[priorityIndex].TryConsume())
        {
            Interlocked.Increment(ref _priorityRejections[priorityIndex]);
            Interlocked.Increment(ref _totalRejections);

            _logger.LogDebug("令牌桶限流拒绝请求，优先级: {Priority}", priority);
            return null;
        }

        // 尝试获取信号量
        var timeoutToUse = timeout == default ? TimeSpan.FromSeconds(5) : timeout;

        try
        {
            var acquired = await _prioritySemaphores[priorityIndex].WaitAsync(timeoutToUse, cancellationToken);

            if (!acquired)
            {
                Interlocked.Increment(ref _totalTimeouts);
                _logger.LogDebug("信号量超时，优先级: {Priority}, 超时时间: {Timeout}", priority, timeoutToUse);
                return null;
            }

            Interlocked.Increment(ref _currentConcurrency[priorityIndex]);

            _logger.LogTrace("获取处理许可成功，优先级: {Priority}, 当前并发: {Concurrency}",
                priority, _currentConcurrency[priorityIndex]);

            return new BackpressurePermit(this, priorityIndex);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取处理许可时异常，优先级: {Priority}", priority);
            return null;
        }
    }

    /// <summary>
    /// 背压许可证
    /// </summary>
    private sealed class BackpressurePermit : IAsyncDisposable
    {
        private readonly ReactiveBackpressureController _controller;
        private readonly int _priorityIndex;
        private volatile bool _disposed;

        public BackpressurePermit(ReactiveBackpressureController controller, int priorityIndex)
        {
            _controller = controller;
            _priorityIndex = priorityIndex;
        }

        public ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;

                Interlocked.Decrement(ref _controller._currentConcurrency[_priorityIndex]);
                _controller._prioritySemaphores[_priorityIndex].Release();

                _controller._logger.LogTrace("释放处理许可，优先级: {Priority}, 当前并发: {Concurrency}",
                    (MessagePriority)_priorityIndex, _controller._currentConcurrency[_priorityIndex]);
            }

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// 收集系统指标
    /// </summary>
    private void CollectMetrics(object? state)
    {
        try
        {
            // 简化的指标收集 - 实际实现中应该使用性能计数器或其他监控工具
            var process = System.Diagnostics.Process.GetCurrentProcess();

            var cpuUsage = GetCpuUsage();
            var memoryUsage = (double)process.WorkingSet64 / (1024 * 1024 * 1024); // GB
            var pendingRequests = 0L;

            for (var i = 0; i < _currentConcurrency.Length; i++)
            {
                pendingRequests += _currentConcurrency[i];
            }

            var responseTime = CalculateAverageResponseTime();

            var metrics = new SystemLoadMetrics(cpuUsage, memoryUsage, pendingRequests, responseTime);
            _currentMetrics = metrics;
            _metricsHistory.Add(metrics);

            // 自适应调整
            if (_options.EnableAdaptiveControl)
            {
                var history = _metricsHistory.GetSnapshot();
                _adaptiveController.AdjustLimits(_priorityLimits, metrics, history);

                // 更新信号量限制
                UpdateSemaphoreLimits();
            }

            _logger.LogTrace("系统指标更新: CPU={CpuUsage:P2}, Memory={MemoryUsage:F2}GB, " +
                            "PendingRequests={PendingRequests}, ResponseTime={ResponseTime:F2}ms",
                metrics.CpuUsage, metrics.MemoryUsage, metrics.PendingRequests, metrics.ResponseTime);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "收集系统指标时异常");
        }
    }

    /// <summary>
    /// 更新信号量限制
    /// </summary>
    private void UpdateSemaphoreLimits()
    {
        for (var i = 0; i < _prioritySemaphores.Length; i++)
        {
            var currentLimit = _prioritySemaphores[i].CurrentCount + _currentConcurrency[i];
            var targetLimit = _priorityLimits[i];

            if (currentLimit != targetLimit)
            {
                // 需要重新创建信号量以调整限制
                var oldSemaphore = _prioritySemaphores[i];
                _prioritySemaphores[i] = new SemaphoreSlim(
                    Math.Max(0, targetLimit - _currentConcurrency[i]), targetLimit);

                // 注意：这里有一个小的竞态条件，在生产环境中可能需要更复杂的处理
                oldSemaphore.Dispose();

                _logger.LogDebug("更新信号量限制，优先级: {Priority}, 新限制: {Limit}",
                    (MessagePriority)i, targetLimit);
            }
        }
    }

    /// <summary>
    /// 获取CPU使用率（简化实现）
    /// </summary>
    private static double GetCpuUsage()
    {
        // 简化实现 - 实际应该使用PerformanceCounter或其他方法
        return Math.Min(1.0, Environment.ProcessorCount > 4 ? 0.3 : 0.5);
    }

    /// <summary>
    /// 计算平均响应时间（简化实现）
    /// </summary>
    private static double CalculateAverageResponseTime()
    {
        // 简化实现 - 实际应该基于真实的响应时间统计
        return 100 + (new Random().NextDouble() * 200);
    }

    /// <summary>
    /// 获取当前负载状态
    /// </summary>
    public SystemLoadMetrics GetCurrentMetrics() => _currentMetrics;

    /// <summary>
    /// 获取统计信息
    /// </summary>
    public BackpressureStatistics GetStatistics()
    {
        var priorityStats = new PriorityStatistics[5];

        for (var i = 0; i < 5; i++)
        {
            priorityStats[i] = new PriorityStatistics
            {
                Priority = (MessagePriority)i,
                TotalRequests = Interlocked.Read(ref _priorityRequests[i]),
                TotalRejections = Interlocked.Read(ref _priorityRejections[i]),
                CurrentConcurrency = _currentConcurrency[i],
                ConcurrencyLimit = _priorityLimits[i],
                AvailableTokens = _priorityTokenBuckets[i].CurrentTokens
            };
        }

        return new BackpressureStatistics
        {
            TotalRequests = Interlocked.Read(ref _totalRequests),
            TotalRejections = Interlocked.Read(ref _totalRejections),
            TotalTimeouts = Interlocked.Read(ref _totalTimeouts),
            CurrentMetrics = _currentMetrics,
            PriorityStatistics = priorityStats
        };
    }

    /// <summary>
    /// 统计信息类
    /// </summary>
    public sealed class BackpressureStatistics
    {
        public long TotalRequests { get; set; }
        public long TotalRejections { get; set; }
        public long TotalTimeouts { get; set; }
        public required SystemLoadMetrics CurrentMetrics { get; init; }
        public PriorityStatistics[] PriorityStatistics { get; set; } = Array.Empty<PriorityStatistics>();
        public double OverallRejectionRate => TotalRequests > 0 ? (double)TotalRejections / TotalRequests : 0;
    }

    /// <summary>
    /// 优先级统计信息
    /// </summary>
    public sealed class PriorityStatistics
    {
        public MessagePriority Priority { get; set; }
        public long TotalRequests { get; set; }
        public long TotalRejections { get; set; }
        public int CurrentConcurrency { get; set; }
        public int ConcurrencyLimit { get; set; }
        public double AvailableTokens { get; set; }
        public double RejectionRate => TotalRequests > 0 ? (double)TotalRejections / TotalRequests : 0;
    }

    /// <summary>
    /// 异步释放资源
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            _metricsTimer?.Dispose();

            for (var i = 0; i < _prioritySemaphores.Length; i++)
            {
                _prioritySemaphores[i]?.Dispose();
            }

            var stats = GetStatistics();
            _logger.LogInformation(
                "ReactiveBackpressureController已释放 - 统计信息: 总请求数: {Total}, 总拒绝数: {Rejections}, " +
                "拒绝率: {RejectionRate:P2}, 总超时数: {Timeouts}",
                stats.TotalRequests, stats.TotalRejections, stats.OverallRejectionRate, stats.TotalTimeouts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "释放ReactiveBackpressureController时异常");
        }

        await ValueTask.CompletedTask;
    }
}
