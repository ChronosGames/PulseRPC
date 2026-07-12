using System.Reflection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseRPC.Server.Hubs; using PulseRPC.Server.Services; using PulseRPC.Server.Transport;

namespace PulseRPC.Server.Services.Management;

/// <summary>
/// 服务实例清理器 - 定期清理空闲和过期的服务实例
/// </summary>
/// <remarks>
/// <para>
/// <strong>清理策略</strong>：
/// </para>
/// <list type="bullet">
/// <item><description><strong>空闲超时</strong>：根据 <see cref="PulseServiceAttribute.IdleTimeoutSeconds"/> 清理长时间未访问的实例</description></item>
/// <item><description><strong>实例上限</strong>：当实例数超过 <see cref="PulseServiceManagerOptions.MaxCachedInstances"/> 时，使用 LRU 策略清理</description></item>
/// <item><description><strong>排除条件</strong>：AutoStart 服务和 Singleton 服务不会被清理</description></item>
/// </list>
/// <para>
/// <strong>运行机制</strong>：
/// </para>
/// <list type="number">
/// <item><description>作为 <see cref="IHostedService"/> 运行，应用启动时自动启动</description></item>
/// <item><description>按 <see cref="PulseServiceManagerOptions.CleanupInterval"/> 间隔执行清理</description></item>
/// <item><description>清理过程是异步的，不会阻塞主线程</description></item>
/// </list>
/// </remarks>
public sealed class ServiceInstanceEvictor : IHostedService, IDisposable
{
    private readonly PulseServiceManager _serviceManager;
    private readonly PulseServiceManagerOptions _options;
    private readonly ILogger<ServiceInstanceEvictor> _logger;

    private readonly object _lifecycleLock = new();
    private readonly SemaphoreSlim _cleanupGate = new(1, 1);
    private CancellationTokenSource? _runCancellation;
    private Task? _runTask;
    private bool _isRunning;

    // 统计信息
    private long _totalEvicted;
    private long _totalCleanupRuns;
    private DateTime _lastCleanupTime;

    public ServiceInstanceEvictor(
        PulseServiceManager serviceManager,
        IOptions<PulseServiceManagerOptions> options,
        ILogger<ServiceInstanceEvictor> logger)
    {
        _serviceManager = serviceManager ?? throw new ArgumentNullException(nameof(serviceManager));
        _options = options?.Value ?? new PulseServiceManagerOptions();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 总清理实例数
    /// </summary>
    public long TotalEvicted => Interlocked.Read(ref _totalEvicted);

    /// <summary>
    /// 总清理运行次数
    /// </summary>
    public long TotalCleanupRuns => Interlocked.Read(ref _totalCleanupRuns);

    /// <summary>
    /// 最后清理时间
    /// </summary>
    public DateTime LastCleanupTime => _lastCleanupTime;

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_lifecycleLock)
        {
            if (_runTask is not null)
            {
                _logger.LogWarning("ServiceInstanceEvictor is already running or stopping");
                return Task.CompletedTask;
            }

            _logger.LogInformation(
                "Starting ServiceInstanceEvictor with cleanup interval: {Interval}, max instances: {MaxInstances}",
                _options.CleanupInterval,
                _options.MaxCachedInstances);

            _runCancellation = new CancellationTokenSource();
            _runTask = RunCleanupLoopAsync(_runCancellation.Token);
            _isRunning = true;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource? runCancellation;
        Task? runTask;
        lock (_lifecycleLock)
        {
            if (!_isRunning)
            {
                return;
            }

            _logger.LogInformation("Stopping ServiceInstanceEvictor");
            _isRunning = false;
            runCancellation = _runCancellation;
            runTask = _runTask;
            runCancellation?.Cancel();
        }

        if (runTask is not null)
        {
            await runTask.WaitAsync(cancellationToken);
        }

        lock (_lifecycleLock)
        {
            if (ReferenceEquals(_runTask, runTask))
            {
                _runTask = null;
                _runCancellation = null;
            }
        }

        runCancellation?.Dispose();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        CancellationTokenSource? runCancellation;
        Task? runTask;
        lock (_lifecycleLock)
        {
            _isRunning = false;
            runCancellation = _runCancellation;
            runTask = _runTask;
            _runCancellation = null;
            _runTask = null;
            runCancellation?.Cancel();
        }

        try
        {
            runTask?.GetAwaiter().GetResult();
        }
        finally
        {
            runCancellation?.Dispose();
            _cleanupGate.Dispose();
        }
    }

    /// <summary>
    /// 手动触发清理（用于测试或紧急清理）
    /// </summary>
    public async Task ForceCleanupAsync(CancellationToken cancellationToken = default)
    {
        await _cleanupGate.WaitAsync(cancellationToken);
        try
        {
            await PerformCleanupAsync(cancellationToken);
        }
        finally
        {
            _cleanupGate.Release();
        }
    }

    private async Task RunCleanupLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_options.CleanupInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (!await _cleanupGate.WaitAsync(0, cancellationToken))
                {
                    _logger.LogDebug("Cleanup already in progress, skipping this run");
                    continue;
                }

                try
                {
                    await PerformCleanupAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during service instance cleanup");
                }
                finally
                {
                    _cleanupGate.Release();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task PerformCleanupAsync(CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        _lastCleanupTime = startTime;
        Interlocked.Increment(ref _totalCleanupRuns);

        _logger.LogDebug("Starting cleanup run #{RunNumber}", _totalCleanupRuns);

        var stats = _serviceManager.GetStatistics();
        var evictedCount = 0;

        // 获取所有服务实例
        var allServices = _serviceManager.GetAllServices().ToList();

        // 1. 清理空闲超时的实例
        evictedCount += await EvictIdleServicesAsync(allServices, cancellationToken);

        // 2. 如果超过最大实例数，使用 LRU 清理
        if (stats.ActiveInstances > _options.MaxCachedInstances)
        {
            var overLimit = stats.ActiveInstances - _options.MaxCachedInstances;
            evictedCount += await EvictLRUServicesAsync(allServices, overLimit, cancellationToken);
        }

        if (evictedCount > 0)
        {
            Interlocked.Add(ref _totalEvicted, evictedCount);
            _logger.LogInformation(
                "Cleanup completed: evicted {EvictedCount} instances in {Duration}ms",
                evictedCount,
                (DateTime.UtcNow - startTime).TotalMilliseconds);
        }
        else
        {
            _logger.LogDebug("Cleanup completed: no instances evicted");
        }
    }

    /// <summary>
    /// 清理空闲超时的服务实例
    /// </summary>
    private async Task<int> EvictIdleServicesAsync(
        List<IPulseService> services,
        CancellationToken cancellationToken)
    {
        var evictedCount = 0;

        foreach (var service in services)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            // 检查是否可以清理
            if (!CanEvict(service))
                continue;

            // 获取服务的空闲超时配置
            var idleTimeout = GetIdleTimeout(service);
            if (idleTimeout == TimeSpan.Zero)
                continue; // 不清理

            // 检查空闲时间
            if (service is PulseServiceBase baseService)
            {
                if (baseService.IdleDuration > idleTimeout)
                {
                    _logger.LogDebug(
                        "Evicting idle service: {ServiceAddress}, idle for {IdleDuration}",
                        ((IPulseService)baseService).ServiceAddress,
                        baseService.IdleDuration);

                    var removed = await _serviceManager.RemoveServiceAsync(
                        service.ServiceType,
                        service.ServiceId,
                        cancellationToken);

                    if (removed)
                    {
                        evictedCount++;
                    }
                }
            }
        }

        return evictedCount;
    }

    /// <summary>
    /// 使用 LRU 策略清理服务实例
    /// </summary>
    private async Task<int> EvictLRUServicesAsync(
        List<IPulseService> services,
        int countToEvict,
        CancellationToken cancellationToken)
    {
        var evictedCount = 0;

        // 按最后访问时间排序（最旧的在前）
        var candidateServices = services
            .Where(s => CanEvict(s))
            .OfType<PulseServiceBase>()
            .OrderBy(s => s.LastAccessTime)
            .Take(countToEvict)
            .ToList();

        foreach (var service in candidateServices)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            _logger.LogDebug(
                "Evicting LRU service: {ServiceAddress}, last accessed at {LastAccessTime}",
                ((IPulseService)service).ServiceAddress,
                service.LastAccessTime);

            var removed = await _serviceManager.RemoveServiceAsync(
                service.ServiceType,
                service.ServiceId,
                cancellationToken);

            if (removed)
            {
                evictedCount++;
            }
        }

        return evictedCount;
    }

    /// <summary>
    /// 检查服务是否可以被清理
    /// </summary>
    private static bool CanEvict(IPulseService service)
    {
        var serviceType = service.GetType();
        var attribute = serviceType.GetCustomAttribute<PulseServiceAttribute>();

        if (attribute == null)
            return true; // 没有 Attribute 的服务默认可清理

        // AutoStart 服务不清理
        if (attribute.StartupType == ServiceStartupType.AutoStart)
            return false;

        // Singleton 服务不清理
        if (attribute.InstanceScope == ServiceInstanceScope.Singleton)
            return false;

        return true;
    }

    /// <summary>
    /// 获取服务的空闲超时时间
    /// </summary>
    private static TimeSpan GetIdleTimeout(IPulseService service)
    {
        var serviceType = service.GetType();
        var attribute = serviceType.GetCustomAttribute<PulseServiceAttribute>();

        if (attribute == null || attribute.IdleTimeoutSeconds <= 0)
            return TimeSpan.Zero; // 不设置空闲超时

        return TimeSpan.FromSeconds(attribute.IdleTimeoutSeconds);
    }

    /// <summary>
    /// 获取清理统计信息
    /// </summary>
    public EvictorStatistics GetStatistics()
    {
        return new EvictorStatistics
        {
            TotalEvicted = TotalEvicted,
            TotalCleanupRuns = TotalCleanupRuns,
            LastCleanupTime = LastCleanupTime,
            IsRunning = _isRunning,
            CleanupInterval = _options.CleanupInterval,
            MaxCachedInstances = _options.MaxCachedInstances
        };
    }
}

/// <summary>
/// 清理器统计信息
/// </summary>
public sealed class EvictorStatistics
{
    public long TotalEvicted { get; init; }
    public long TotalCleanupRuns { get; init; }
    public DateTime LastCleanupTime { get; init; }
    public bool IsRunning { get; init; }
    public TimeSpan CleanupInterval { get; init; }
    public int MaxCachedInstances { get; init; }
}
