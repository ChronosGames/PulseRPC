using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Abstractions;

namespace PulseRPC.Server.ServiceManagement;

/// <summary>
/// 统一服务管理器 - 管理所有 <see cref="IUnifiedPulseService"/> 实例的生命周期和调度
/// </summary>
/// <remarks>
/// <para>
/// 核心职责：
/// </para>
/// <list type="bullet">
/// <item><description>服务注册和发现</description></item>
/// <item><description>按需创建服务实例（OnDemand 服务）</description></item>
/// <item><description>自动启动服务实例（AutoStart 服务）</description></item>
/// <item><description>服务实例缓存和回收</description></item>
/// <item><description>请求路由到正确的服务实例</description></item>
/// </list>
/// </remarks>
public sealed class UnifiedServiceManager : IAsyncDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<UnifiedServiceManager> _logger;
    private readonly UnifiedServiceManagerOptions _options;

    // 服务注册表：ServiceType -> ServiceRegistration
    private readonly ConcurrentDictionary<string, ServiceTypeRegistration> _registrations = new();

    // 服务实例缓存：ServiceAddress -> Service Instance
    private readonly ConcurrentDictionary<string, IUnifiedPulseService> _instances = new();

    // 正在创建中的服务（用于避免竞态条件下的重复创建）
    // Key: ServiceAddress, Value: 创建任务
    private readonly ConcurrentDictionary<string, Task<IUnifiedPulseService>> _pendingCreations = new();

    // 自动启动服务的 ServiceAddress 列表
    private readonly List<string> _autoStartServices = new();

    // 统计信息
    private long _totalCreated;
    private long _totalDisposed;
    private long _totalRaceConditionsAvoided; // 统计避免的竞态创建次数

    public UnifiedServiceManager(
        IServiceProvider serviceProvider,
        ILogger<UnifiedServiceManager> logger,
        UnifiedServiceManagerOptions? options = null)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? new UnifiedServiceManagerOptions();
    }

    /// <summary>
    /// 注册服务类型
    /// </summary>
    /// <typeparam name="TService">服务实现类型</typeparam>
    /// <param name="factory">服务实例工厂</param>
    public void Register<TService>(Func<IServiceProvider, string, TService>? factory = null)
        where TService : class, IUnifiedPulseService
    {
        var serviceType = typeof(TService);
        var attribute = serviceType.GetCustomAttribute<PulseServiceAttribute>() ?? new PulseServiceAttribute();

        var serviceName = attribute.DisplayName ?? serviceType.Name;

        var registration = new ServiceTypeRegistration
        {
            ServiceType = serviceType,
            ServiceName = serviceName,
            Attribute = attribute,
            Factory = factory != null
                ? (sp, id) => factory(sp, id)
                : CreateDefaultFactory<TService>()
        };

        if (!_registrations.TryAdd(serviceName, registration))
        {
            throw new InvalidOperationException($"Service type '{serviceName}' is already registered");
        }

        _logger.LogInformation(
            "Registered service: {ServiceName}, StartupType={StartupType}, InstanceScope={InstanceScope}, SchedulingMode={SchedulingMode}",
            serviceName,
            attribute.StartupType,
            attribute.InstanceScope,
            attribute.SchedulingMode);

        // 记录自动启动服务
        if (attribute.StartupType == ServiceStartupType.AutoStart)
        {
            if (attribute.InstanceScope != ServiceInstanceScope.Singleton)
            {
                throw new InvalidOperationException(
                    $"AutoStart services must be Singleton, but {serviceName} is {attribute.InstanceScope}");
            }

            _autoStartServices.Add($"{serviceName}:default");
        }
    }

    /// <summary>
    /// 启动所有自动启动的服务
    /// </summary>
    public async Task StartAutoStartServicesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting {Count} auto-start services", _autoStartServices.Count);

        foreach (var serviceAddress in _autoStartServices)
        {
            try
            {
                var parts = serviceAddress.Split(':', 2);
                var serviceType = parts[0];
                var serviceId = parts[1];

                var service = await GetOrCreateServiceAsync(serviceType, serviceId, cancellationToken);
                await service.StartAsync(cancellationToken);

                _logger.LogInformation("Auto-started service: {ServiceAddress}", serviceAddress);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to auto-start service: {ServiceAddress}", serviceAddress);

                if (!_options.ContinueOnAutoStartFailure)
                {
                    throw;
                }
            }
        }
    }

    /// <summary>
    /// 获取或创建服务实例
    /// </summary>
    /// <param name="serviceType">服务类型名称</param>
    /// <param name="serviceId">服务实例 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>服务实例</returns>
    /// <remarks>
    /// <para>
    /// <strong>竞态条件处理</strong>：
    /// 当多个线程同时请求同一个 ServiceAddress 时，只会创建一个实例，
    /// 其他线程会等待创建完成后返回同一个实例。
    /// </para>
    /// <para>
    /// <strong>实现原理</strong>：
    /// 使用 <c>_pendingCreations</c> 字典存储正在创建中的任务，
    /// 确保同一时刻只有一个创建任务在执行。
    /// </para>
    /// </remarks>
    public async ValueTask<IUnifiedPulseService> GetOrCreateServiceAsync(
        string serviceType,
        string serviceId,
        CancellationToken cancellationToken = default)
    {
        var serviceAddress = $"{serviceType}:{serviceId}";

        // 快速路径 1：已存在的实例
        if (_instances.TryGetValue(serviceAddress, out var existingService))
        {
            return existingService;
        }

        // 快速路径 2：正在创建中，等待创建完成
        if (_pendingCreations.TryGetValue(serviceAddress, out var pendingTask))
        {
            Interlocked.Increment(ref _totalRaceConditionsAvoided);
            _logger.LogDebug(
                "Waiting for pending service creation: {ServiceAddress}",
                serviceAddress);
            return await pendingTask.ConfigureAwait(false);
        }

        // 慢速路径：需要创建新实例
        if (!_registrations.TryGetValue(serviceType, out var registration))
        {
            throw new InvalidOperationException($"Service type '{serviceType}' is not registered");
        }

        // 验证实例范围
        ValidateServiceId(registration, serviceId);

        // 使用 GetOrAdd 确保只有一个创建任务
        var creationTask = _pendingCreations.GetOrAdd(serviceAddress,
            _ => CreateAndRegisterServiceAsync(serviceAddress, registration, serviceId, cancellationToken));

        try
        {
            return await creationTask.ConfigureAwait(false);
        }
        finally
        {
            // 创建完成后移除 pending 状态
            _pendingCreations.TryRemove(serviceAddress, out _);
        }
    }

    /// <summary>
    /// 创建并注册服务实例（内部方法，仅被 GetOrCreateServiceAsync 调用）
    /// </summary>
    private async Task<IUnifiedPulseService> CreateAndRegisterServiceAsync(
        string serviceAddress,
        ServiceTypeRegistration registration,
        string serviceId,
        CancellationToken cancellationToken)
    {
        // 再次检查是否已存在（可能在等待获取创建任务时被其他线程创建）
        if (_instances.TryGetValue(serviceAddress, out var existingService))
        {
            Interlocked.Increment(ref _totalRaceConditionsAvoided);
            return existingService;
        }

        // 创建实例
        var service = registration.Factory(_serviceProvider, serviceId);

        // 添加到缓存
        if (!_instances.TryAdd(serviceAddress, service))
        {
            // 极少数情况：在创建过程中另一个实例被添加了
            // 这种情况不应该发生，因为我们使用了 _pendingCreations 控制
            await service.DisposeAsync().ConfigureAwait(false);
            Interlocked.Increment(ref _totalRaceConditionsAvoided);
            return _instances[serviceAddress];
        }

        Interlocked.Increment(ref _totalCreated);

        _logger.LogInformation(
            "Created service instance: {ServiceAddress}, Type={ServiceType}",
            serviceAddress, registration.ServiceType.Name);

        // 对于 OnDemand 服务，自动启动
        if (registration.Attribute.StartupType == ServiceStartupType.OnDemand &&
            service.State == ServiceLifecycleState.Created)
        {
            await service.StartAsync(cancellationToken).ConfigureAwait(false);
        }

        return service;
    }

    /// <summary>
    /// 获取服务实例（不创建）
    /// </summary>
    public IUnifiedPulseService? GetService(string serviceType, string serviceId)
    {
        var serviceAddress = $"{serviceType}:{serviceId}";
        _instances.TryGetValue(serviceAddress, out var service);
        return service;
    }

    /// <summary>
    /// 移除并释放服务实例
    /// </summary>
    public async ValueTask<bool> RemoveServiceAsync(
        string serviceType,
        string serviceId,
        CancellationToken cancellationToken = default)
    {
        var serviceAddress = $"{serviceType}:{serviceId}";

        if (!_instances.TryRemove(serviceAddress, out var service))
        {
            return false;
        }

        try
        {
            if (service.State == ServiceLifecycleState.Running)
            {
                await service.StopAsync(cancellationToken);
            }

            await service.DisposeAsync();
            Interlocked.Increment(ref _totalDisposed);

            _logger.LogInformation("Removed service instance: {ServiceAddress}", serviceAddress);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing service: {ServiceAddress}", serviceAddress);
            return false;
        }
    }

    /// <summary>
    /// 获取所有活跃的服务实例
    /// </summary>
    public IEnumerable<IUnifiedPulseService> GetAllServices()
        => _instances.Values;

    /// <summary>
    /// 获取指定类型的所有服务实例
    /// </summary>
    public IEnumerable<IUnifiedPulseService> GetServicesByType(string serviceType)
        => _instances
            .Where(kvp => kvp.Key.StartsWith($"{serviceType}:"))
            .Select(kvp => kvp.Value);

    /// <summary>
    /// 获取统计信息
    /// </summary>
    public ServiceManagerStatistics GetStatistics()
        => new()
        {
            RegisteredTypes = _registrations.Count,
            ActiveInstances = _instances.Count,
            PendingCreations = _pendingCreations.Count,
            TotalCreated = Interlocked.Read(ref _totalCreated),
            TotalDisposed = Interlocked.Read(ref _totalDisposed),
            RaceConditionsAvoided = Interlocked.Read(ref _totalRaceConditionsAvoided)
        };

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        _logger.LogInformation("Disposing UnifiedServiceManager with {Count} active instances", _instances.Count);

        var services = _instances.Values.ToList();
        _instances.Clear();

        foreach (var service in services)
        {
            try
            {
                if (service.State == ServiceLifecycleState.Running)
                {
                    await service.StopAsync();
                }

                await service.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing service: {ServiceAddress}",
                    ((IUnifiedPulseService)service).ServiceAddress);
            }
        }

        _registrations.Clear();
        _autoStartServices.Clear();

        _logger.LogInformation("UnifiedServiceManager disposed");
    }

    private void ValidateServiceId(ServiceTypeRegistration registration, string serviceId)
    {
        switch (registration.Attribute.InstanceScope)
        {
            case ServiceInstanceScope.Singleton:
                if (serviceId != "default")
                {
                    throw new InvalidOperationException(
                        $"Singleton service '{registration.ServiceName}' must have ServiceId 'default', got '{serviceId}'");
                }
                break;

            case ServiceInstanceScope.MultiInstance:
                if (string.IsNullOrWhiteSpace(serviceId) || serviceId == "default")
                {
                    throw new InvalidOperationException(
                        $"MultiInstance service '{registration.ServiceName}' must have a unique ServiceId, got '{serviceId}'");
                }
                break;
        }
    }

    /// <summary>
    /// 创建编译后的默认工厂（使用表达式树编译，避免反射开销）
    /// </summary>
    /// <remarks>
    /// <para>性能对比：</para>
    /// <list type="bullet">
    /// <item><description>编译后委托调用: ~10ns</description></item>
    /// <item><description>反射 ConstructorInfo.Invoke: ~500ns</description></item>
    /// <item><description>ActivatorUtilities.CreateInstance: ~800ns</description></item>
    /// </list>
    /// <para>性能提升约 50-80 倍</para>
    /// </remarks>
    private static Func<IServiceProvider, string, IUnifiedPulseService> CreateDefaultFactory<TService>()
        where TService : class, IUnifiedPulseService
    {
        // 使用编译后的工厂（首次调用编译并缓存，后续直接使用委托）
        var typedFactory = CompiledConstructorFactory.GetOrCreateFactory<TService>();
        return (sp, serviceId) => typedFactory(sp, serviceId);
    }
}

/// <summary>
/// 服务类型注册信息
/// </summary>
internal sealed class ServiceTypeRegistration
{
    public required Type ServiceType { get; init; }
    public required string ServiceName { get; init; }
    public required PulseServiceAttribute Attribute { get; init; }
    public required Func<IServiceProvider, string, IUnifiedPulseService> Factory { get; init; }
}

/// <summary>
/// UnifiedServiceManager 配置选项
/// </summary>
public sealed class UnifiedServiceManagerOptions
{
    /// <summary>
    /// 自动启动失败时是否继续启动其他服务
    /// </summary>
    public bool ContinueOnAutoStartFailure { get; set; } = false;

    /// <summary>
    /// 是否启用实例自动清理
    /// </summary>
    /// <remarks>
    /// 启用后，<see cref="ServiceInstanceEvictor"/> 会根据 <see cref="CleanupInterval"/> 定期清理空闲实例。
    /// </remarks>
    public bool EnableInstanceEviction { get; set; } = true;

    /// <summary>
    /// 空闲实例清理间隔
    /// </summary>
    /// <remarks>
    /// 默认每 5 分钟检查一次空闲实例。
    /// </remarks>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 最大缓存实例数
    /// </summary>
    /// <remarks>
    /// 当实例数超过此值时，会使用 LRU 策略清理最久未访问的实例。
    /// 设置为 0 表示不限制。
    /// </remarks>
    public int MaxCachedInstances { get; set; } = 10000;
}

/// <summary>
/// 服务管理器统计信息
/// </summary>
public sealed class ServiceManagerStatistics
{
    /// <summary>
    /// 已注册的服务类型数量
    /// </summary>
    public int RegisteredTypes { get; init; }

    /// <summary>
    /// 当前活跃的服务实例数量
    /// </summary>
    public int ActiveInstances { get; init; }

    /// <summary>
    /// 正在创建中的服务数量
    /// </summary>
    public int PendingCreations { get; init; }

    /// <summary>
    /// 总共创建的服务实例数量
    /// </summary>
    public long TotalCreated { get; init; }

    /// <summary>
    /// 总共释放的服务实例数量
    /// </summary>
    public long TotalDisposed { get; init; }

    /// <summary>
    /// 避免的竞态创建次数（性能优化指标）
    /// </summary>
    /// <remarks>
    /// 此值越高，说明 pending creations 机制越有效地避免了重复创建和丢弃实例的开销。
    /// </remarks>
    public long RaceConditionsAvoided { get; init; }
}

