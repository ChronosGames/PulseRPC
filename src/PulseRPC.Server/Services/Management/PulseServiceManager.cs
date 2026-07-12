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
    private readonly IServiceInstanceLeaseLifetime? _serviceInstanceLeaseLifetime;

    // 服务注册表：ServiceType -> ServiceRegistration
    private readonly ConcurrentDictionary<string, ServiceTypeRegistration> _registrations = new();

    // 服务实例缓存：ServiceAddress -> Service Instance
    private readonly ConcurrentDictionary<string, IPulseService> _instances = new();

    // 已从活跃实例表摘除、但尚未成功 Dispose 的实例。清理失败时必须继续由 Manager
    // 持有所有权，阻止同地址的新实例在旧实例释放完成前被激活。
    private readonly ConcurrentDictionary<string, IPulseService> _instancesPendingCleanup = new();

    // 正在创建中的服务（用于避免竞态条件下的重复创建）
    // Key: ServiceAddress, Value: 创建任务
    private readonly ConcurrentDictionary<string, Lazy<Task<IPulseService>>> _pendingCreations = new();

    // 同一地址上的创建、发布、移除必须串行化，避免移除错过尚未发布的创建任务，
    // 或新实例在旧实例完成 Dispose/lease release 前被重新激活。
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _serviceOperationGates = new();

    // 实例创建属于 Manager 生命周期；单个调用方取消只能停止自己的等待，不能取消其它调用方共享的激活。
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly object _disposeLock = new();
    private Task? _disposeTask;
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
        _serviceInstanceLeaseLifetime =
            serviceProvider.GetService(typeof(IServiceInstanceLeaseLifetime)) as IServiceInstanceLeaseLifetime;
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
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeCts.Token);
        var operationToken = combinedCts.Token;
        _logger.LogInformation("Starting {Count} auto-start services", _autoStartServices.Count);
        var startedServices = new List<string>();
        List<Exception>? cleanupFailures = null;

        foreach (var serviceAddress in _autoStartServices)
        {
            var parts = serviceAddress.Split(':', 2);
            var serviceType = parts[0];
            var serviceId = parts[1];
            try
            {
                var service = await GetOrCreateServiceAsync(
                        serviceType,
                        serviceId,
                        operationToken)
                    .ConfigureAwait(false);
                await service.StartAsync(operationToken).ConfigureAwait(false);
                startedServices.Add(serviceAddress);

                _logger.LogInformation("Auto-started service: {ServiceAddress}", serviceAddress);
            }
            catch (Exception ex)
            {
                var currentCleanupFailure = await CleanupAutoStartServiceAsync(
                        serviceType,
                        serviceId)
                    .ConfigureAwait(false);
                if (currentCleanupFailure is not null)
                {
                    (cleanupFailures ??= new List<Exception>()).Add(currentCleanupFailure);
                }

                _logger.LogError(ex, "Failed to auto-start service: {ServiceAddress}", serviceAddress);

                if (!_options.ContinueOnAutoStartFailure)
                {
                    for (var index = startedServices.Count - 1; index >= 0; index--)
                    {
                        var startedParts = startedServices[index].Split(':', 2);
                        var rollbackFailure = await CleanupAutoStartServiceAsync(
                                startedParts[0],
                                startedParts[1])
                            .ConfigureAwait(false);
                        if (rollbackFailure is not null)
                        {
                            (cleanupFailures ??= new List<Exception>()).Add(rollbackFailure);
                        }
                    }

                    if (cleanupFailures is { Count: > 0 })
                    {
                        cleanupFailures.Insert(0, ex);
                        throw new AggregateException(
                            "Auto-start failed and rollback cleanup was incomplete.",
                            cleanupFailures);
                    }

                    throw;
                }
            }
        }

        if (cleanupFailures is { Count: > 0 })
        {
            throw new AggregateException(
                "One or more failed auto-start services could not be cleaned up.",
                cleanupFailures);
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
        cancellationToken.ThrowIfCancellationRequested();
        var serviceAddress = $"{serviceType}:{serviceId}";
        SemaphoreSlim operationGate;
        lock (_disposeLock)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
            operationGate = _serviceOperationGates.GetOrAdd(
                serviceAddress,
                static _ => new SemaphoreSlim(1, 1));
        }
        Lazy<Task<IPulseService>>? creation = null;
        IPulseService? immediateService = null;
        var createdPendingEntry = false;
        var joinedPendingCreation = false;

        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Publishing the pending Lazy and transitioning Dispose state share one lock. Dispose
            // therefore observes every activation that passed this second check, including a Lazy
            // whose value has not started executing yet.
            lock (_disposeLock)
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);

                if (_instancesPendingCleanup.ContainsKey(serviceAddress))
                {
                    throw new InvalidOperationException(
                        $"Service '{serviceAddress}' is pending cleanup. Retry removal before activating a replacement.");
                }

                if (_instances.TryGetValue(serviceAddress, out var existingService))
                {
                    immediateService = existingService;
                }
                else if (_pendingCreations.TryGetValue(serviceAddress, out var pendingCreation))
                {
                    creation = pendingCreation;
                    joinedPendingCreation = true;
                }
                else
                {
                    if (!_registrations.TryGetValue(serviceType, out var registration))
                    {
                        throw new InvalidOperationException($"Service type '{serviceType}' is not registered");
                    }

                    ValidateServiceId(registration, serviceId);
                    creation = new Lazy<Task<IPulseService>>(
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

                    createdPendingEntry = true;
                }
            }
        }
        finally
        {
            operationGate.Release();
        }

        if (immediateService is not null)
        {
            ServiceActivationScope.MarkActivated();
            return immediateService;
        }

        if (joinedPendingCreation)
        {
            Interlocked.Increment(ref _totalRaceConditionsAvoided);
            _logger.LogDebug(
                "Waiting for pending service creation: {ServiceAddress}",
                serviceAddress);
        }

        var creationTask = creation!.Value;
        if (createdPendingEntry)
        {
            _ = RemovePendingCreationWhenCompleteAsync(serviceAddress, creation);
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
        var published = false;
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

            IPulseService? raceWinner = null;
            lock (_disposeLock)
            {
                // A synchronous factory can ignore cancellation and finish after DisposeAsync has
                // transitioned the Manager state. The final state check and cache publication must
                // be atomic with that transition.
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
                cancellationToken.ThrowIfCancellationRequested();
                if (!_instances.TryAdd(serviceAddress, service))
                {
                    raceWinner = _instances[serviceAddress];
                }
                else
                {
                    published = true;
                }
            }

            if (raceWinner is not null)
            {
                await service.DisposeAsync().ConfigureAwait(false);
                Interlocked.Increment(ref _totalRaceConditionsAvoided);
                ServiceActivationScope.MarkActivated();
                return raceWinner;
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
            if (service is not null)
            {
                if (published)
                {
                    _instances.TryRemove(
                        new KeyValuePair<string, IPulseService>(serviceAddress, service));
                }

                try
                {
                    await service.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception disposeException)
                {
                    _instancesPendingCleanup.TryAdd(serviceAddress, service);
                    _logger.LogError(
                        disposeException,
                        "Failed to dispose service after activation failure: {ServiceAddress}",
                        serviceAddress);
                }
            }

            await ServiceActivationScope.MarkFailedAsync().ConfigureAwait(false);
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
        var result = await RemoveServiceCoreAsync(
                serviceType,
                serviceId,
                cancellationToken)
            .ConfigureAwait(false);
        return result.Disposed && result.StopException is null;
    }

    private async ValueTask<ServiceRemovalResult> RemoveServiceCoreAsync(
        string serviceType,
        string serviceId,
        CancellationToken cancellationToken)
    {
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
                    // Activation failure may have retained an instance whose Dispose failed.
                    // Continue into the pending-cleanup lookup before deciding there is no owner.
                }
            }

            if (!_instancesPendingCleanup.TryGetValue(serviceAddress, out var service))
            {
                if (!_instances.TryRemove(serviceAddress, out service))
                {
                    return ServiceRemovalResult.NotFound;
                }

                if (!_instancesPendingCleanup.TryAdd(serviceAddress, service))
                {
                    // Same-address operations are serialized, so this indicates an invariant
                    // violation. Restore discoverability rather than losing cleanup ownership.
                    _instances.TryAdd(serviceAddress, service);
                    throw new InvalidOperationException(
                        $"Cleanup ownership already exists for service '{serviceAddress}'.");
                }
            }

            Exception? stopException = null;
            if (service.State == ServiceLifecycleState.Running)
            {
                try
                {
                    await service.StopAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    stopException = ex;
                    _logger.LogError(ex, "Error stopping service before removal: {ServiceAddress}", serviceAddress);
                }
            }

            try
            {
                await service.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing service during removal: {ServiceAddress}", serviceAddress);
                return new ServiceRemovalResult(
                    Found: true,
                    Disposed: false,
                    StopException: stopException,
                    DisposeException: ex);
            }

            _instancesPendingCleanup.TryRemove(
                new KeyValuePair<string, IPulseService>(serviceAddress, service));
            Interlocked.Increment(ref _totalDisposed);
            await ReleaseActorLeaseAsync(serviceType, serviceId).ConfigureAwait(false);

            _logger.LogInformation("Removed service instance: {ServiceAddress}", serviceAddress);
            return new ServiceRemovalResult(
                Found: true,
                Disposed: true,
                StopException: stopException,
                DisposeException: null);
        }
        finally
        {
            operationGate.Release();
        }
    }

    private async ValueTask<Exception?> CleanupAutoStartServiceAsync(
        string serviceType,
        string serviceId)
    {
        var firstAttempt = await RemoveServiceCoreAsync(
                serviceType,
                serviceId,
                CancellationToken.None)
            .ConfigureAwait(false);
        if (!firstAttempt.Found)
        {
            return null;
        }

        if (firstAttempt.Disposed)
        {
            return firstAttempt.StopException is null
                ? null
                : new InvalidOperationException(
                    $"Auto-start rollback stopped '{serviceType}:{serviceId}' with an error before disposal completed.",
                    firstAttempt.StopException);
        }

        // A failed Dispose remains in _instancesPendingCleanup. Retry once immediately so
        // transient cleanup failures do not leak ownership across a failed Host start.
        var secondAttempt = await RemoveServiceCoreAsync(
                serviceType,
                serviceId,
                CancellationToken.None)
            .ConfigureAwait(false);
        if (secondAttempt.Disposed)
        {
            var stopException = secondAttempt.StopException ?? firstAttempt.StopException;
            return stopException is null
                ? null
                : new InvalidOperationException(
                    $"Auto-start rollback stopped '{serviceType}:{serviceId}' with an error before disposal completed.",
                    stopException);
        }

        var failures = new List<Exception>();
        if (firstAttempt.StopException is not null)
        {
            failures.Add(firstAttempt.StopException);
        }

        if (firstAttempt.DisposeException is not null)
        {
            failures.Add(firstAttempt.DisposeException);
        }

        if (secondAttempt.StopException is not null &&
            !ReferenceEquals(secondAttempt.StopException, firstAttempt.StopException))
        {
            failures.Add(secondAttempt.StopException);
        }

        if (secondAttempt.DisposeException is not null)
        {
            failures.Add(secondAttempt.DisposeException);
        }

        return new AggregateException(
            $"Auto-start rollback could not dispose '{serviceType}:{serviceId}'; cleanup ownership was retained for retry.",
            failures);
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
    public ValueTask DisposeAsync()
    {
        lock (_disposeLock)
        {
            if (_disposeTask is null)
            {
                Volatile.Write(ref _disposeState, 1);
                _disposeTask = DisposeCoreAsync();
            }

            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        await Task.Yield();

        _logger.LogInformation(
            "Disposing PulseServiceManager with {ActiveCount} active and {CleanupCount} pending-cleanup instances",
            _instances.Count,
            _instancesPendingCleanup.Count);

        try
        {
            await _lifetimeCts.CancelAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manager lifetime cancellation callbacks failed during shutdown");
        }

        // DisposeAsync transitions _disposeState under _disposeLock, while GetOrCreate publishes
        // every Lazy under the same lock. Force all Lazies here: a creation that passed the second
        // state check can no longer escape the shutdown snapshot merely because Value had not run.
        var pendingTasks = _pendingCreations.Values
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

        List<Exception>? cleanupFailures = null;
        var serviceAddresses = _instances.Keys
            .Concat(_instancesPendingCleanup.Keys)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        foreach (var serviceAddress in serviceAddresses)
        {
            var separator = serviceAddress.IndexOf(':');
            if (separator <= 0)
            {
                (cleanupFailures ??= new List<Exception>()).Add(
                    new InvalidOperationException($"Invalid managed service address '{serviceAddress}'."));
                continue;
            }

            var serviceType = serviceAddress[..separator];
            var serviceId = serviceAddress[(separator + 1)..];
            var firstAttempt = await RemoveServiceCoreAsync(
                    serviceType,
                    serviceId,
                    CancellationToken.None)
                .ConfigureAwait(false);
            var finalAttempt = firstAttempt;
            if (firstAttempt.Found && !firstAttempt.Disposed)
            {
                finalAttempt = await RemoveServiceCoreAsync(
                        serviceType,
                        serviceId,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }

            if (finalAttempt.Found && !finalAttempt.Disposed)
            {
                var failures = new List<Exception>();
                if (firstAttempt.DisposeException is not null)
                {
                    failures.Add(firstAttempt.DisposeException);
                }

                if (finalAttempt.DisposeException is not null)
                {
                    failures.Add(finalAttempt.DisposeException);
                }

                (cleanupFailures ??= new List<Exception>()).Add(
                    new AggregateException(
                        $"PulseServiceManager could not dispose '{serviceAddress}'; cleanup ownership was retained.",
                        failures));
            }
        }

        if (cleanupFailures is { Count: > 0 })
        {
            throw new AggregateException(
                "PulseServiceManager shutdown left services pending cleanup.",
                cleanupFailures);
        }

        _registrations.Clear();
        _autoStartServices.Clear();
        _pendingCreations.Clear();
        _instances.Clear();
        _instancesPendingCleanup.Clear();
        _serviceOperationGates.Clear();
        _lifetimeCts.Dispose();

        _logger.LogInformation("PulseServiceManager disposed");
    }

    private async ValueTask ReleaseActorLeaseAsync(string serviceType, string serviceId)
    {
        if (_serviceInstanceLeaseLifetime is null)
        {
            return;
        }

        try
        {
            await _serviceInstanceLeaseLifetime
                .ReleaseAsync(serviceType, serviceId, CancellationToken.None)
                .ConfigureAwait(false);
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

    private readonly record struct ServiceRemovalResult(
        bool Found,
        bool Disposed,
        Exception? StopException,
        Exception? DisposeException)
    {
        public static ServiceRemovalResult NotFound { get; } = new(
            Found: false,
            Disposed: false,
            StopException: null,
            DisposeException: null);
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
