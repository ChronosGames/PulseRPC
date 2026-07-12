using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Hubs; using PulseRPC.Server.Services; using PulseRPC.Server.Transport;

namespace PulseRPC.Server.Services.Management;

/// <summary>
/// 统一服务管理器 - 管理所有 <see cref="IPulseService"/> 实例的生命周期和调度
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
public sealed class PulseServiceManager : IAsyncDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PulseServiceManager> _logger;
    private readonly PulseServiceManagerOptions _options;

    // 服务注册表：ServiceType -> ServiceRegistration
    private readonly ConcurrentDictionary<string, ServiceTypeRegistration> _registrations = new();

    // 服务实例缓存：ServiceAddress -> Service Instance
    private readonly ConcurrentDictionary<string, IPulseService> _instances = new();

    // 正在创建中的服务（用于避免竞态条件下的重复创建）
    // Key: ServiceAddress, Value: 创建任务
    private readonly ConcurrentDictionary<string, Lazy<Task<IPulseService>>> _pendingCreations = new();

    // 同一地址上的创建、发布、移除必须串行化，避免移除错过尚未发布的创建任务，
    // 或新实例在旧实例完成 Dispose/lease release 前被重新激活。
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _serviceOperationGates = new();

    // 实例创建属于 Manager 生命周期；单个调用方取消只能停止自己的等待，不能取消其它调用方共享的激活。
    private readonly CancellationTokenSource _lifetimeCts = new();
    private int _disposeState;

    // 自动启动服务的 ServiceAddress 列表
    private readonly List<string> _autoStartServices = new();

    // 统计信息
    private long _totalCreated;
    private long _totalDisposed;
    private long _totalRaceConditionsAvoided; // 统计避免的竞态创建次数

    public PulseServiceManager(
        IServiceProvider serviceProvider,
        ILogger<PulseServiceManager> logger,
        PulseServiceManagerOptions? options = null)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? new PulseServiceManagerOptions();
    }

    /// <summary>
    /// 注册服务类型
    /// </summary>
    /// <typeparam name="TService">服务实现类型</typeparam>
    /// <param name="factory">服务实例工厂</param>
    public void Register<TService>(Func<IServiceProvider, string, TService>? factory = null)
        where TService : class, IPulseService
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
            var parts = serviceAddress.Split(':', 2);
            var serviceType = parts[0];
            var serviceId = parts[1];
            try
            {
                var service = await GetOrCreateServiceAsync(serviceType, serviceId, cancellationToken);
                await service.StartAsync(cancellationToken);

                _logger.LogInformation("Auto-started service: {ServiceAddress}", serviceAddress);
            }
            catch (Exception ex)
            {
                await RemoveServiceAsync(serviceType, serviceId, CancellationToken.None).ConfigureAwait(false);
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
    public async ValueTask<IPulseService> GetOrCreateServiceAsync(
        string serviceType,
        string serviceId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();
        var serviceAddress = $"{serviceType}:{serviceId}";
        var operationGate = _serviceOperationGates.GetOrAdd(
            serviceAddress,
            static _ => new SemaphoreSlim(1, 1));
        Task<IPulseService> creationTask;
        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);

            if (_instances.TryGetValue(serviceAddress, out var existingService))
            {
                ServiceActivationScope.MarkActivated();
                return existingService;
            }

            if (_pendingCreations.TryGetValue(serviceAddress, out var pendingCreation))
            {
                Interlocked.Increment(ref _totalRaceConditionsAvoided);
                _logger.LogDebug(
                    "Waiting for pending service creation: {ServiceAddress}",
                    serviceAddress);
                creationTask = pendingCreation.Value;
            }
            else
            {
                if (!_registrations.TryGetValue(serviceType, out var registration))
                {
                    throw new InvalidOperationException($"Service type '{serviceType}' is not registered");
                }

                ValidateServiceId(registration, serviceId);
                var creation = new Lazy<Task<IPulseService>>(
                    () => CreateAndRegisterServiceAsync(
                        serviceAddress,
                        registration,
                        serviceId,
                        _lifetimeCts.Token),
                    LazyThreadSafetyMode.ExecutionAndPublication);
                if (!_pendingCreations.TryAdd(serviceAddress, creation))
                {
                    throw new InvalidOperationException(
                        $"Service operation serialization failed for '{serviceAddress}'.");
                }

                creationTask = creation.Value;
                _ = RemovePendingCreationWhenCompleteAsync(serviceAddress, creation);
            }
        }
        finally
        {
            operationGate.Release();
        }

        ServiceActivationScope.Observe(creationTask);
        var createdService = await creationTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        ServiceActivationScope.MarkActivated();
        return createdService;
    }

    /// <summary>
    /// 创建并注册服务实例（内部方法，仅被 GetOrCreateServiceAsync 调用）
    /// </summary>
    private async Task<IPulseService> CreateAndRegisterServiceAsync(
        string serviceAddress,
        ServiceTypeRegistration registration,
        string serviceId,
        CancellationToken cancellationToken)
    {
        // 再次检查是否已存在（可能在等待获取创建任务时被其他线程创建）
        if (_instances.TryGetValue(serviceAddress, out var existingService))
        {
            Interlocked.Increment(ref _totalRaceConditionsAvoided);
            ServiceActivationScope.MarkActivated();
            return existingService;
        }

        // 创建实例。按需服务必须完整启动后才能发布到缓存，避免其它请求观察到 Starting/Faulted 实例。
        IPulseService? service = null;
        try
        {
            service = registration.Factory(_serviceProvider, serviceId)
                ?? throw new InvalidOperationException(
                    $"Service factory for '{serviceAddress}' returned null.");
            if (registration.Attribute.StartupType == ServiceStartupType.OnDemand &&
                service.State == ServiceLifecycleState.Created)
            {
                await service.StartAsync(cancellationToken).ConfigureAwait(false);
            }

            if (!_instances.TryAdd(serviceAddress, service))
            {
                await service.DisposeAsync().ConfigureAwait(false);
                Interlocked.Increment(ref _totalRaceConditionsAvoided);
                ServiceActivationScope.MarkActivated();
                return _instances[serviceAddress];
            }

            Interlocked.Increment(ref _totalCreated);
            ServiceActivationScope.MarkActivated();
            _logger.LogInformation(
                "Created service instance: {ServiceAddress}, Type={ServiceType}",
                serviceAddress, registration.ServiceType.Name);
            return service;
        }
        catch
        {
            await ServiceActivationScope.MarkFailedAsync().ConfigureAwait(false);
            if (service is not null)
            {
                try
                {
                    await service.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception disposeException)
                {
                    _logger.LogError(
                        disposeException,
                        "Failed to dispose service after activation failure: {ServiceAddress}",
                        serviceAddress);
                }
            }

            throw;
        }
    }

    /// <summary>
    /// 获取服务实例（不创建）
    /// </summary>
    public IPulseService? GetService(string serviceType, string serviceId)
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
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();
        var serviceAddress = $"{serviceType}:{serviceId}";
        var operationGate = _serviceOperationGates.GetOrAdd(
            serviceAddress,
            static _ => new SemaphoreSlim(1, 1));
        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_pendingCreations.TryGetValue(serviceAddress, out var pendingCreation))
            {
                try
                {
                    await pendingCreation.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    return false;
                }
            }

            if (!_instances.TryRemove(serviceAddress, out var service))
            {
                return false;
            }

            var stopSucceeded = true;
            var disposeSucceeded = false;
            if (service.State == ServiceLifecycleState.Running)
            {
                try
                {
                    await service.StopAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    stopSucceeded = false;
                    _logger.LogError(ex, "Error stopping service before removal: {ServiceAddress}", serviceAddress);
                }
            }

            try
            {
                await service.DisposeAsync().ConfigureAwait(false);
                disposeSucceeded = true;
                Interlocked.Increment(ref _totalDisposed);

                _logger.LogInformation("Removed service instance: {ServiceAddress}", serviceAddress);
                return stopSucceeded;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing service during removal: {ServiceAddress}", serviceAddress);
                return false;
            }
            finally
            {
                if (disposeSucceeded)
                {
                    await ReleaseActorLeaseAsync(serviceType, serviceId).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            operationGate.Release();
        }
    }

    /// <summary>
    /// 获取所有活跃的服务实例
    /// </summary>
    public IEnumerable<IPulseService> GetAllServices()
        => _instances.Values;

    /// <summary>
    /// 获取指定类型的所有服务实例
    /// </summary>
    public IEnumerable<IPulseService> GetServicesByType(string serviceType)
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
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        _logger.LogInformation("Disposing PulseServiceManager with {Count} active instances", _instances.Count);

        _lifetimeCts.Cancel();
        var pendingTasks = _pendingCreations.Values
            .Where(static creation => creation.IsValueCreated)
            .Select(static creation => creation.Value)
            .ToArray();
        try
        {
            await Task.WhenAll(pendingTasks).ConfigureAwait(false);
        }
        catch
        {
            // 单个创建任务会自行释放未发布实例；Manager 关闭继续回收已发布实例。
        }

        var services = _instances.ToArray();
        _instances.Clear();

        foreach (var entry in services)
        {
            var service = entry.Value;
            var disposeSucceeded = false;
            if (service.State == ServiceLifecycleState.Running)
            {
                try
                {
                    await service.StopAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error stopping service during manager shutdown: {ServiceAddress}",
                        service.ServiceAddress);
                }
            }

            try
            {
                await service.DisposeAsync().ConfigureAwait(false);
                disposeSucceeded = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing service: {ServiceAddress}",
                    service.ServiceAddress);
            }

            if (disposeSucceeded)
            {
                var separator = entry.Key.IndexOf(':');
                if (separator > 0)
                {
                    await ReleaseActorLeaseAsync(
                        entry.Key[..separator],
                        entry.Key[(separator + 1)..]).ConfigureAwait(false);
                }
            }
        }

        _registrations.Clear();
        _autoStartServices.Clear();
        _pendingCreations.Clear();
        _serviceOperationGates.Clear();
        _lifetimeCts.Dispose();

        _logger.LogInformation("PulseServiceManager disposed");
    }

    private async ValueTask ReleaseActorLeaseAsync(string serviceType, string serviceId)
    {
        var lifetime = _serviceProvider.GetService(typeof(IServiceInstanceLeaseLifetime)) as IServiceInstanceLeaseLifetime;
        if (lifetime is null)
        {
            return;
        }

        try
        {
            await lifetime.ReleaseAsync(serviceType, serviceId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to release Actor lease after removing service: {ServiceAddress}",
                $"{serviceType}:{serviceId}");
        }
    }

    private async Task RemovePendingCreationWhenCompleteAsync(
        string serviceAddress,
        Lazy<Task<IPulseService>> creation)
    {
        try
        {
            await creation.Value.ConfigureAwait(false);
        }
        catch
        {
            // 调用方观察创建异常；此处只负责移除共享 pending 状态。
        }

        _pendingCreations.TryRemove(
            new KeyValuePair<string, Lazy<Task<IPulseService>>>(serviceAddress, creation));
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
    private static Func<IServiceProvider, string, IPulseService> CreateDefaultFactory<TService>()
        where TService : class, IPulseService
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
    public required Func<IServiceProvider, string, IPulseService> Factory { get; init; }
}

/// <summary>
/// PulseServiceManager 配置选项
/// </summary>
public sealed class PulseServiceManagerOptions
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
