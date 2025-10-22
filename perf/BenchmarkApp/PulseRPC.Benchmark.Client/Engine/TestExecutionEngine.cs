using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PulseRPC.Benchmark.Client.Transport;
using PulseRPC.Benchmark.Core.Interfaces;
using PulseRPC.Benchmark.Core.Models;
using PulseRPC.Benchmark.Metrics.Collectors;
using PulseRPC.Benchmark.Shared.Models;

namespace PulseRPC.Benchmark.Client.Engine;

/// <summary>
/// 测试执行引擎
/// 负责编排和执行基准测试
/// </summary>
public class TestExecutionEngine
{
    private readonly ILogger<TestExecutionEngine> _logger;
    private readonly ClientConnectionManager _connectionManager;
    private readonly RealTimeMetricsCollector _metricsCollector;
    private readonly ConcurrentDictionary<string, IBenchmarkScenario> _scenarios;

    private CancellationTokenSource? _cancellationTokenSource;
    private volatile bool _isRunning;

    /// <summary>
    /// 测试进度更新事件
    /// </summary>
    public event Action<TestProgress>? ProgressUpdated;

    /// <summary>
    /// 测试状态变更事件
    /// </summary>
    public event Action<TestState>? StateChanged;

    public TestExecutionEngine(
        ILogger<TestExecutionEngine> logger,
        ClientConnectionManager connectionManager,
        RealTimeMetricsCollector metricsCollector)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));
        _scenarios = new ConcurrentDictionary<string, IBenchmarkScenario>();

        InitializeScenarios();
    }

    /// <summary>
    /// 执行测试
    /// </summary>
    public async Task<TestResults> ExecuteTestAsync(TestConfiguration config)
    {
        if (_isRunning)
        {
            throw new InvalidOperationException("测试引擎正在运行中");
        }

        _isRunning = true;
        _cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = _cancellationTokenSource.Token;

        try
        {
            _logger.LogInformation("开始执行测试: {Scenario}", config.ScenarioName);
            StateChanged?.Invoke(TestState.Starting);

            // 验证配置
            ValidateTestConfiguration(config);

            // 获取测试场景
            var scenario = GetScenario(config.ScenarioName);

            // 创建测试结果
            var results = new TestResults
            {
                TestName = config.ScenarioName,
                StartTime = DateTime.UtcNow,
                Configuration = config
            };

            // 建立连接
            StateChanged?.Invoke(TestState.Connecting);
            await EstablishConnectionsAsync(config, cancellationToken);

            // 预热阶段
            if (config.WarmupSeconds > 0)
            {
                StateChanged?.Invoke(TestState.WarmingUp);
                await ExecuteWarmupAsync(scenario, config, cancellationToken);

                // 重置统计数据的起始时间，确保不包含 warmup 阶段
                _logger.LogInformation("预热完成，重置统计起始时间");
                results = new TestResults
                {
                    TestName = config.ScenarioName,
                    StartTime = DateTime.UtcNow,
                    Configuration = config
                };
            }

            // 执行主测试
            StateChanged?.Invoke(TestState.Running);
            _logger.LogInformation("开始主测试阶段，持续 {Duration} 秒", config.DurationSeconds);
            await ExecuteMainTestAsync(scenario, config, results, cancellationToken);

            // 收集最终结果
            StateChanged?.Invoke(TestState.Collecting);
            await CollectFinalResultsAsync(results);

            results.EndTime = DateTime.UtcNow;
            results.TotalDuration = results.EndTime - results.StartTime;
            results.Success = true;

            StateChanged?.Invoke(TestState.Completed);
            _logger.LogInformation("测试执行完成: {Scenario}, 耗时: {Duration}",　config.ScenarioName, results.TotalDuration);

            return results;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("测试被取消");
            StateChanged?.Invoke(TestState.Cancelled);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "测试执行失败: {Scenario}", config.ScenarioName);
            StateChanged?.Invoke(TestState.Failed);
            throw;
        }
        finally
        {
            _isRunning = false;
            await CleanupAsync();
        }
    }

    /// <summary>
    /// 停止测试
    /// </summary>
    public async Task StopTestAsync()
    {
        if (_isRunning && _cancellationTokenSource != null)
        {
            _logger.LogInformation("正在停止测试...");
            await _cancellationTokenSource.CancelAsync();

            // 等待测试完全停止
            var timeout = TimeSpan.FromSeconds(30);
            var stopwatch = Stopwatch.StartNew();

            while (_isRunning && stopwatch.Elapsed < timeout)
            {
                await Task.Delay(100);
            }

            if (_isRunning)
            {
                _logger.LogWarning("测试停止超时，强制终止");
            }
        }
    }

    /// <summary>
    /// 初始化测试场景
    /// </summary>
    private void InitializeScenarios()
    {
        // 这里将注册所有可用的测试场景
        // 暂时使用模拟实现
        var dummyScenario = new DummyBenchmarkScenario();
        _scenarios.TryAdd("ping-pong", dummyScenario);
        _scenarios.TryAdd("echo-latency", dummyScenario);
        _scenarios.TryAdd("throughput", dummyScenario);
        _scenarios.TryAdd("latency-analysis", dummyScenario);

        _logger.LogInformation("已初始化 {Count} 个测试场景", _scenarios.Count);
    }

    /// <summary>
    /// 验证测试配置
    /// </summary>
    private void ValidateTestConfiguration(TestConfiguration config)
    {
        if (string.IsNullOrEmpty(config.ServerAddress))
            throw new ArgumentException("服务器地址不能为空");

        if (string.IsNullOrEmpty(config.ScenarioName))
            throw new ArgumentException("测试场景名称不能为空");

        if (config.DurationSeconds <= 0)
            throw new ArgumentException("测试持续时间必须大于0");

        if (config.ConcurrentConnections <= 0)
            throw new ArgumentException("并发连接数必须大于0");

        if (config.RequestRate <= 0)
            throw new ArgumentException("请求速率必须大于0");

        _logger.LogDebug("测试配置验证通过");
    }

    /// <summary>
    /// 获取测试场景
    /// </summary>
    private IBenchmarkScenario GetScenario(string scenarioName)
    {
        if (!_scenarios.TryGetValue(scenarioName, out var scenario))
        {
            throw new ArgumentException($"未找到测试场景: {scenarioName}");
        }

        return scenario;
    }

    /// <summary>
    /// 建立连接
    /// </summary>
    private async Task EstablishConnectionsAsync(TestConfiguration config, CancellationToken cancellationToken)
    {
        _logger.LogInformation("建立 {Count} 个并发连接到 {Server}",
            config.ConcurrentConnections, config.ServerAddress);

        var connectionTasks = new List<Task>();

        for (int i = 0; i < config.ConcurrentConnections; i++)
        {
            var task = _connectionManager.CreateConnectionAsync(
                config.ServerAddress,
                $"client_{i}",
                cancellationToken);
            connectionTasks.Add(task);
        }

        await Task.WhenAll(connectionTasks);
        _logger.LogInformation("所有连接已建立完成");
    }

    /// <summary>
    /// 执行预热
    /// </summary>
    private async Task ExecuteWarmupAsync(
        IBenchmarkScenario scenario,
        TestConfiguration config,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("开始预热阶段，持续 {Duration} 秒", config.WarmupSeconds);

        var warmupConfig = new TestConfiguration
        {
            ServerAddress = config.ServerAddress,
            ScenarioName = config.ScenarioName,
            DurationSeconds = config.WarmupSeconds,
            ConcurrentConnections = config.ConcurrentConnections,
            RequestRate = Math.Min(config.RequestRate, 50), // 预热阶段使用较低的速率
            WarmupSeconds = 0,
            Verbose = config.Verbose
        };

        var warmupResults = new TestResults
        {
            TestName = $"{config.ScenarioName}_warmup",
            StartTime = DateTime.UtcNow
        };

        await ExecuteMainTestAsync(scenario, warmupConfig, warmupResults, cancellationToken);

        _logger.LogInformation("预热阶段完成");
    }

    /// <summary>
    /// 执行主测试
    /// </summary>
    private async Task ExecuteMainTestAsync(
        IBenchmarkScenario scenario,
        TestConfiguration config,
        TestResults results,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("配置: 并发={Concurrency}, 目标速率={Rate} QPS",
            config.ConcurrentConnections, config.RequestRate);

        // 使用高性能计时器
        var totalStopwatch = Stopwatch.StartNew();
        var endTicks = totalStopwatch.ElapsedTicks + config.DurationSeconds * Stopwatch.Frequency;

        var concurrencyLimit = config.ConcurrentConnections;

        // 速率控制 - 使用令牌桶算法
        var requestIntervalTicks = config.RequestRate > 0
            ? Stopwatch.Frequency / config.RequestRate
            : 0; // 每个请求之间的最小间隔(ticks)

        _logger.LogInformation("速率控制: requestIntervalTicks={Interval}, 预期间隔={Ms}ms",
            requestIntervalTicks, (double)requestIntervalTicks * 1000 / Stopwatch.Frequency);

        // 自适应模式选择：
        // - 低速率场景 (RequestRate < ConcurrentConnections): 使用限流+并发模式，避免线程池浪费
        // - 高速率场景 (RequestRate >= ConcurrentConnections): 使用工作线程池模式
        var useThreadPool = config.RequestRate >= config.ConcurrentConnections;
        _logger.LogInformation("执行模式: {Mode}", useThreadPool ? "工作线程池(高速率)" : "限流+并发(低速率)");

        if (useThreadPool)
        {
            await ExecuteWithThreadPoolAsync(config, results, totalStopwatch, endTicks, concurrencyLimit, requestIntervalTicks, cancellationToken);
        }
        else
        {
            await ExecuteWithRateLimitAsync(config, results, totalStopwatch, endTicks, concurrencyLimit, requestIntervalTicks, cancellationToken);
        }

        _logger.LogInformation("主测试阶段完成，总请求数: {Count}", results.TotalRequests);
    }

    /// <summary>
    /// 工作线程池模式：适合高速率场景
    /// </summary>
    private async Task ExecuteWithThreadPoolAsync(
        TestConfiguration config,
        TestResults results,
        Stopwatch totalStopwatch,
        long endTicks,
        int concurrencyLimit,
        long requestIntervalTicks,
        CancellationToken cancellationToken)
    {
        var scenario = GetScenario(config.ScenarioName);
        var workerTasks = new Task[concurrencyLimit];
        var globalRequestId = 0;

        for (int workerId = 0; workerId < concurrencyLimit; workerId++)
        {
            var localWorkerId = workerId;
            workerTasks[workerId] = Task.Run(async () =>
            {
                var connectionId = $"client_{localWorkerId % config.ConcurrentConnections}";
                var localStopwatch = Stopwatch.StartNew();

                // 每个工作线程的目标速率（均分总速率）
                var workerTargetIntervalTicks = requestIntervalTicks > 0
                    ? requestIntervalTicks * concurrencyLimit
                    : 0;
                var workerRequestCount = 0;

                while (localStopwatch.ElapsedTicks < endTicks && !cancellationToken.IsCancellationRequested)
                {
                    // 每个工作线程独立发送请求
                    var requestId = Interlocked.Increment(ref globalRequestId);
                    workerRequestCount++;

                    // 速率控制：每个工作线程独立控制自己的速率
                    if (workerTargetIntervalTicks > 0)
                    {
                        var expectedTicks = workerRequestCount * workerTargetIntervalTicks;
                        var currentTicks = localStopwatch.ElapsedTicks;

                        if (currentTicks < expectedTicks)
                        {
                            // 使用高精度自旋等待
                            var spinWait = new SpinWait();
                            while (localStopwatch.ElapsedTicks < expectedTicks && !cancellationToken.IsCancellationRequested)
                            {
                                spinWait.SpinOnce();
                            }
                        }
                    }

                    try
                    {
                        await ExecuteRequestAsync(scenario, connectionId, requestId, results, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "工作线程 {WorkerId} 执行请求失败", localWorkerId);
                    }
                }
            }, cancellationToken);
        }

        // 定期更新进度
        var progressTask = Task.Run(async () =>
        {
            while (totalStopwatch.ElapsedTicks < endTicks && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(1000, cancellationToken);
                UpdateProgressInline(results, globalRequestId, totalStopwatch.Elapsed, config.ConcurrentConnections);
            }
        }, cancellationToken);

        // 等待所有工作线程完成
        try
        {
            await Task.WhenAll(workerTasks);
        }
        catch (OperationCanceledException)
        {
            // 正常取消
        }

        // 停止进度更新
        try
        {
            await progressTask;
        }
        catch (OperationCanceledException)
        {
            // 正常取消
        }

        results.TotalRequests = globalRequestId;
    }

    /// <summary>
    /// 限流+并发模式：适合低速率场景
    /// </summary>
    private async Task ExecuteWithRateLimitAsync(
        TestConfiguration config,
        TestResults results,
        Stopwatch totalStopwatch,
        long endTicks,
        int concurrencyLimit,
        long requestIntervalTicks,
        CancellationToken cancellationToken)
    {
        var scenario = GetScenario(config.ScenarioName);
        var connectionIndex = 0;
        var requestCount = 0;
        var lastProgressUpdateTicks = totalStopwatch.ElapsedTicks;
        var progressUpdateIntervalTicks = (long)(1.0 * Stopwatch.Frequency); // 每1秒更新一次

        // 使用信号量控制最大并发数
        using var semaphore = new SemaphoreSlim(concurrencyLimit, concurrencyLimit);
        var pendingTasks = new List<Task>(concurrencyLimit * 2);

        while (totalStopwatch.ElapsedTicks < endTicks && !cancellationToken.IsCancellationRequested)
        {
            // 清理已完成的任务
            pendingTasks.RemoveAll(t => t.IsCompleted);

            // 如果达到并发上限，等待一个任务完成
            if (pendingTasks.Count >= concurrencyLimit)
            {
                var completedTask = await Task.WhenAny(pendingTasks);
                pendingTasks.Remove(completedTask);
            }

            // 速率控制：非阻塞检查
            var currentTicks = totalStopwatch.ElapsedTicks;
            var expectedTicks = requestCount * requestIntervalTicks;
            if (requestIntervalTicks > 0 && currentTicks < expectedTicks)
            {
                // 还没到发送下一个请求的时间，短暂等待
                var waitMs = (int)Math.Min((expectedTicks - currentTicks) * 1000 / Stopwatch.Frequency, 10);
                if (waitMs > 0)
                {
                    await Task.Delay(waitMs, cancellationToken);
                }
                continue;
            }

            // 发送请求
            await semaphore.WaitAsync(cancellationToken);
            var connectionId = $"client_{connectionIndex % config.ConcurrentConnections}";
            connectionIndex++;
            var requestId = ++requestCount;

            var requestTask = Task.Run(async () =>
            {
                try
                {
                    await ExecuteRequestAsync(scenario, connectionId, requestId, results, cancellationToken);
                }
                finally
                {
                    semaphore.Release();
                }
            }, cancellationToken);

            pendingTasks.Add(requestTask);

            // 更新进度
            currentTicks = totalStopwatch.ElapsedTicks;
            if (currentTicks - lastProgressUpdateTicks >= progressUpdateIntervalTicks)
            {
                UpdateProgressInline(results, requestCount, totalStopwatch.Elapsed, config.ConcurrentConnections);
                lastProgressUpdateTicks = currentTicks;
            }
        }

        // 等待所有剩余任务完成
        if (pendingTasks.Count > 0)
        {
            await Task.WhenAll(pendingTasks);
        }

        results.TotalRequests = requestCount;
    }

    /// <summary>
    /// 内联更新进度,避免创建后台线程的开销
    /// </summary>
    private void UpdateProgressInline(TestResults results, int requestCount, TimeSpan elapsed, int activeConnections)
    {
        // 快速计算统计信息
        var totalQPS = requestCount / Math.Max(elapsed.TotalSeconds, 1);

        // 使用轻量级统计计算,避免完整的排序操作
        results.CalculateLightweightStatistics();

        var progress = new TestProgress
        {
            ElapsedTime = elapsed,
            TotalRequests = requestCount,
            RequestsPerSecond = totalQPS,
            ActiveConnections = activeConnections,
            SuccessfulRequests = results.SuccessfulRequests,
            FailedRequests = results.FailedRequests,
            AverageLatencyMs = results.AverageLatencyMs,
            RecentQPS = totalQPS,
            PeakQPS = Math.Max(totalQPS, 0),
            MinLatencyMs = results.MinLatencyMs,
            MaxLatencyMs = results.MaxLatencyMs,
            P95LatencyMs = results.P95LatencyMs,
            P99LatencyMs = results.P99LatencyMs
        };

        ProgressUpdated?.Invoke(progress);
    }

    private async Task UpdateProgressAsync(TestResults results, int requestCount, DateTime currentTime, int activeConnections)
    {
        // 在后台线程计算统计信息，避免阻塞主循环
        await Task.Run(() =>
        {
            results.CalculateStatistics();

            var elapsed = currentTime - results.StartTime;
            var totalQPS = requestCount / Math.Max(elapsed.TotalSeconds, 1);

            var progress = new TestProgress
            {
                ElapsedTime = elapsed,
                TotalRequests = requestCount,
                RequestsPerSecond = totalQPS,
                ActiveConnections = activeConnections,
                SuccessfulRequests = results.SuccessfulRequests,
                FailedRequests = results.FailedRequests,
                AverageLatencyMs = results.AverageLatencyMs,
                RecentQPS = totalQPS, // 这里可以改为计算最近几秒的QPS
                PeakQPS = Math.Max(totalQPS, 0),
                MinLatencyMs = results.MinLatencyMs,
                MaxLatencyMs = results.MaxLatencyMs,
                P95LatencyMs = results.P95LatencyMs,
                P99LatencyMs = results.P99LatencyMs
            };

            ProgressUpdated?.Invoke(progress);
        });
    }

    /// <summary>
    /// 执行单个请求
    /// </summary>
    private async Task ExecuteRequestAsync(
        IBenchmarkScenario scenario,
        string connectionId,
        int requestId,
        TestResults results,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            // 真实发送 Ping 请求并测量延迟
            var connection = _connectionManager.GetConnection(connectionId);
            if (connection == null)
            {
                throw new InvalidOperationException($"连接不存在或未连接: {connectionId}");
            }

            var ping = new PingRequest
            {
                RequestId = requestId,
                SequenceNumber = requestId,
                PayloadSize = 0
            };

            await connection.SendRequestAsync<PingRequest, PingResponse>(ping, cancellationToken);

            stopwatch.Stop();

            results.IncrementSuccessfulRequests(stopwatch.Elapsed.TotalMilliseconds);

            // 记录指标（模拟实现）
            // await _metricsCollector.CollectAsync("request_completed", new
            // {
            //     RequestId = requestId,
            //     ConnectionId = connectionId,
            //     LatencyMs = stopwatch.Elapsed.TotalMilliseconds,
            //     Success = true,
            //     Timestamp = DateTime.UtcNow
            // }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // 测试被取消，不记录为错误
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            // 记录失败请求
            results.IncrementFailedRequests();

            _logger.LogDebug(ex, "请求执行失败: RequestId={RequestId}, ConnectionId={ConnectionId}", requestId, connectionId);

            // 记录错误指标（模拟实现）
            // await _metricsCollector.CollectAsync("request_failed", new
            // {
            //     RequestId = requestId,
            //     ConnectionId = connectionId,
            //     Error = ex.Message,
            //     Timestamp = DateTime.UtcNow
            // }, cancellationToken);
        }
    }

    /// <summary>
    /// 收集最终结果
    /// </summary>
    private async Task CollectFinalResultsAsync(TestResults results)
    {
        _logger.LogInformation("收集最终测试结果...");

        // 计算统计信息
        results.CalculateStatistics();

        // 收集指标数据（模拟实现）
        // await _metricsCollector.CollectAsync("test_completed", new
        // {
        //     TestName = results.TestName,
        //     Duration = results.TotalDuration,
        //     TotalRequests = results.TotalRequests,
        //     SuccessfulRequests = results.SuccessfulRequests,
        //     FailedRequests = results.FailedRequests,
        //     AverageLatencyMs = results.AverageLatencyMs,
        //     P95LatencyMs = results.P95LatencyMs,
        //     P99LatencyMs = results.P99LatencyMs,
        //     RequestsPerSecond = results.RequestsPerSecond,
        //     Timestamp = DateTime.UtcNow
        // });

        _logger.LogInformation("测试结果收集完成");
    }

    /// <summary>
    /// 清理资源
    /// </summary>
    private async Task CleanupAsync()
    {
        try
        {
            await _connectionManager.CloseAllConnectionsAsync();
            _cancellationTokenSource?.Dispose();
            _logger.LogInformation("资源清理完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "资源清理时发生错误");
        }
    }
}

/// <summary>
/// 测试状态枚举
/// </summary>
public enum TestState
{
    Idle,
    Starting,
    Connecting,
    WarmingUp,
    Running,
    Collecting,
    Completed,
    Cancelled,
    Failed
}

/// <summary>
/// 测试进度
/// </summary>
public class TestProgress
{
    public TimeSpan ElapsedTime { get; set; }
    public int TotalRequests { get; set; }
    public double RequestsPerSecond { get; set; }
    public int ActiveConnections { get; set; }
    public int SuccessfulRequests { get; set; }
    public int FailedRequests { get; set; }
    public double AverageLatencyMs { get; set; }

    /// <summary>
    /// 当前成功率 (0-100)
    /// </summary>
    public double SuccessRate => TotalRequests > 0 ? (double)SuccessfulRequests / TotalRequests * 100 : 0;

    /// <summary>
    /// 当前失败率 (0-100)
    /// </summary>
    public double FailureRate => TotalRequests > 0 ? (double)FailedRequests / TotalRequests * 100 : 0;

    /// <summary>
    /// 最近一分钟的QPS
    /// </summary>
    public double RecentQPS { get; set; }

    /// <summary>
    /// 峰值QPS
    /// </summary>
    public double PeakQPS { get; set; }

    /// <summary>
    /// 最小延迟
    /// </summary>
    public double MinLatencyMs { get; set; }

    /// <summary>
    /// 最大延迟
    /// </summary>
    public double MaxLatencyMs { get; set; }

    /// <summary>
    /// P95延迟
    /// </summary>
    public double P95LatencyMs { get; set; }

    /// <summary>
    /// P99延迟
    /// </summary>
    public double P99LatencyMs { get; set; }
}

/// <summary>
/// 测试结果
/// </summary>
public class TestResults
{
    public string TestName { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan TotalDuration { get; set; }
    public TestConfiguration? Configuration { get; set; }
    public bool Success { get; set; }

    private int _totalRequests;
    private int _successfulRequests;
    private int _failedRequests;

    public int TotalRequests
    {
        get => _totalRequests;
        set => _totalRequests = value;
    }

    public int SuccessfulRequests => _successfulRequests;
    public int FailedRequests => _failedRequests;

    public void IncrementFailedRequests() => Interlocked.Increment(ref _failedRequests);

    public double RequestsPerSecond => TotalRequests / Math.Max(TotalDuration.TotalSeconds, 1);
    public double SuccessRate => TotalRequests > 0 ? (double)SuccessfulRequests / TotalRequests : 0;

    public double AverageLatencyMs { get; private set; }
    public double MedianLatencyMs { get; private set; }
    public double P95LatencyMs { get; private set; }
    public double P99LatencyMs { get; private set; }
    public double MinLatencyMs { get; private set; }
    public double MaxLatencyMs { get; private set; }

    private readonly ConcurrentBag<double> _latencies = new();

    // 用于轻量级统计的累积值
    private double _sumLatency;
    private double _minLatency = double.MaxValue;
    private double _maxLatency = double.MinValue;

    public void IncrementSuccessfulRequests(double latencyMs)
    {
        Interlocked.Increment(ref _successfulRequests);
        _latencies.Add(latencyMs);

        // 更新累积统计值(无锁更新,允许轻微不准确)
        _sumLatency += latencyMs;

        // 更新最小/最大值
        var currentMin = _minLatency;
        while (latencyMs < currentMin)
        {
            var original = Interlocked.CompareExchange(ref _minLatency, latencyMs, currentMin);
            if (original == currentMin) break;
            currentMin = _minLatency;
        }

        var currentMax = _maxLatency;
        while (latencyMs > currentMax)
        {
            var original = Interlocked.CompareExchange(ref _maxLatency, latencyMs, currentMax);
            if (original == currentMax) break;
            currentMax = _maxLatency;
        }
    }

    /// <summary>
    /// 轻量级统计计算,仅使用累积值,不排序
    /// </summary>
    public void CalculateLightweightStatistics()
    {
        var count = _successfulRequests;
        if (count == 0) return;

        // 使用累积值计算平均值,避免完整遍历
        AverageLatencyMs = _sumLatency / count;
        MinLatencyMs = _minLatency == double.MaxValue ? 0 : _minLatency;
        MaxLatencyMs = _maxLatency == double.MinValue ? 0 : _maxLatency;

        // P95/P99 使用上次计算的缓存值或估算值
        // 这里不做精确计算以避免排序开销
    }

    /// <summary>
    /// 完整统计计算,包含百分位数(需要排序)
    /// </summary>
    public void CalculateStatistics()
    {
        if (_latencies.IsEmpty) return;

        var latencyArray = _latencies.ToArray();
        Array.Sort(latencyArray);

        AverageLatencyMs = latencyArray.Average();
        MedianLatencyMs = GetPercentile(latencyArray, 50);
        P95LatencyMs = GetPercentile(latencyArray, 95);
        P99LatencyMs = GetPercentile(latencyArray, 99);
        MinLatencyMs = latencyArray[0];
        MaxLatencyMs = latencyArray[^1];
    }

    private static double GetPercentile(double[] sortedArray, double percentile)
    {
        var index = (percentile / 100.0) * (sortedArray.Length - 1);
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);

        if (lower == upper)
            return sortedArray[lower];

        var weight = index - lower;
        return sortedArray[lower] * (1 - weight) + sortedArray[upper] * weight;
    }
}

/// <summary>
/// 临时的虚拟基准测试场景（用于编译）
/// </summary>
internal class DummyBenchmarkScenario : IBenchmarkScenario
{
    public string Name => "dummy";
    public string Description => "Dummy scenario for compilation";
    public string Version => "1.0.0";
    public string Category => "Test";
    public bool RequiresServiceDependencies => false;

    public bool SupportsTransport(string transportName) => true;

    public ScenarioRequirements GetRequirements() => new ScenarioRequirements();

    public Task InitializeAsync(IBenchmarkTransport transport, BenchmarkConfiguration config, IServiceProvider? serviceProvider = null, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task WarmupAsync(BenchmarkConfiguration config, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<BenchmarkResult> ExecuteAsync(BenchmarkConfiguration config, Action<ExecutionProgress>? progressCallback = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new BenchmarkResult { IsSuccessful = true });
    }

    public Task CleanupAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public IEnumerable<Type> GetRequiredServiceTypes() => Enumerable.Empty<Type>();

    public void ConfigureServices(IServiceProvider serviceProvider) { }

    public BenchmarkConfiguration GetDefaultConfiguration() => new BenchmarkConfiguration();

    public BenchmarkConfiguration[] GetRecommendedConfigurations() => Array.Empty<BenchmarkConfiguration>();

    public byte[] GenerateTestData(int size) => new byte[size];
}
