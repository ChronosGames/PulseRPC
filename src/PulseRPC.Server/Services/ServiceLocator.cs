using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace PulseRPC.Server;

// ========================
// 1. 服务标识符扩展 [已废弃]
// ========================

/// <summary>
/// [已废弃] 服务标识符 - 用于区分同类型服务的不同实例
/// </summary>
/// <remarks>
/// <para><strong>⚠️ 此结构已废弃</strong>，请使用 IUnifiedPulseService.ServiceType + ServiceId 替代。</para>
/// </remarks>
[Obsolete("使用 IUnifiedPulseService.ServiceType + ServiceId 替代。")]
public readonly struct ServiceId : IEquatable<ServiceId>
{
    /// <summary>服务类型名称</summary>
    public string ServiceType { get; }

    /// <summary>实例标识（可选，单实例时为空）</summary>
    public string? InstanceId { get; }

    /// <summary>是否为单实例</summary>
    public bool IsSingleton => string.IsNullOrEmpty(InstanceId);

    private ServiceId(string serviceType, string? instanceId = null)
    {
        ServiceType = serviceType ?? throw new ArgumentNullException(nameof(serviceType));
        InstanceId = instanceId;
    }

    /// <summary>创建单实例服务标识</summary>
    public static ServiceId CreateSingleton<TService>() where TService : BaseService
        => new(typeof(TService).Name, null);

    /// <summary>创建单实例服务标识</summary>
    public static ServiceId CreateSingleton(string serviceType)
        => new(serviceType, null);

    /// <summary>创建多实例服务标识</summary>
    public static ServiceId CreateMultiInstance<TService>(string instanceId) where TService : BaseService
        => new(typeof(TService).Name, instanceId);

    /// <summary>创建多实例服务标识</summary>
    public static ServiceId CreateMultiInstance(string serviceType, string instanceId)
        => new(serviceType, instanceId);

    public override string ToString()
        => IsSingleton ? $"{ServiceType}" : $"{ServiceType}:{InstanceId}";

    public bool Equals(ServiceId other)
        => ServiceType == other.ServiceType && InstanceId == other.InstanceId;

    public override bool Equals(object? obj)
        => obj is ServiceId other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(ServiceType, InstanceId);

    public static bool operator ==(ServiceId left, ServiceId right) => left.Equals(right);
    public static bool operator !=(ServiceId left, ServiceId right) => !left.Equals(right);
}

// ========================
// 3. 增强的服务注册描述符
// ========================

/// <summary>
/// 服务注册配置
/// </summary>
public class ServiceRegistrationConfig
{
    /// <summary>服务类型</summary>
    public Type ServiceType { get; set; } = null!;

    /// <summary>实例管理策略</summary>
    public ServiceInstanceStrategy Strategy { get; set; }

    /// <summary>是否为多实例服务</summary>
    public bool IsMultiInstance { get; set; }

    /// <summary>对象池大小（仅对Pooled策略有效）</summary>
    public int PoolSize { get; set; } = 10;

    /// <summary>服务工厂</summary>
    public Func<IServiceProvider, string?, BaseService> ImplementationFactory { get; set; } = null!;
}

/// <summary>
/// 增强的服务注册描述符
/// </summary>
public class ServiceDescriptor
{
    public Type ServiceType { get; }
    public ServiceInstanceStrategy Strategy { get; }
    public bool IsMultiInstance { get; }
    public Func<IServiceProvider, string?, BaseService> ImplementationFactory { get; }
    public int PoolSize { get; }

    public ServiceDescriptor(ServiceRegistrationConfig config)
    {
        ServiceType = config.ServiceType ?? throw new ArgumentNullException(nameof(config.ServiceType));
        Strategy = config.Strategy;
        IsMultiInstance = config.IsMultiInstance;
        ImplementationFactory = config.ImplementationFactory ?? throw new ArgumentNullException(nameof(config.ImplementationFactory));
        PoolSize = config.PoolSize;
    }

    public ServiceDescriptor(
        Type serviceType,
        ServiceInstanceStrategy strategy,
        bool isMultiInstance,
        Func<IServiceProvider, string?, BaseService> implementationFactory,
        int poolSize = 10)
    {
        ServiceType = serviceType ?? throw new ArgumentNullException(nameof(serviceType));
        Strategy = strategy;
        IsMultiInstance = isMultiInstance;
        ImplementationFactory = implementationFactory ?? throw new ArgumentNullException(nameof(implementationFactory));
        PoolSize = poolSize;
    }
}

// ========================
// 4. 增强的ServiceLocator
// ========================

/// <summary>
/// 增强的服务定位器 - 支持单实例和多实例管理
/// </summary>
public sealed class ServiceLocator : IAsyncDisposable
{
    private static readonly Lazy<ServiceLocator> _instance = new(() => new ServiceLocator());
    public static ServiceLocator Instance => _instance.Value;

    // 服务注册表：ServiceType -> ServiceDescriptor
    private readonly ConcurrentDictionary<Type, ServiceDescriptor> _registrations = new();

    // 单实例服务：ServiceId -> IService
    private readonly ConcurrentDictionary<ServiceId, IService> _singletonInstances = new();

    // 多实例服务：ServiceId -> IService（包含instanceId）
    private readonly ConcurrentDictionary<ServiceId, IService> _multiInstances = new();

    // 对象池实例：ServiceId -> Pool
    private readonly ConcurrentDictionary<ServiceId, object> _pooledInstances = new();

    // PID索引：PID -> ServiceId
    private readonly ConcurrentDictionary<PID, ServiceId> _pidIndex = new();

    // 广播接收器列表
    private readonly ConcurrentBag<IPulseReceiver> _receivers = new();

    // 序列号生成器
    private int _sequenceCounter;

    // 当前节点ID
    private ushort _nodeId;

    private ILogger<ServiceLocator> _logger = null!;
    private IServiceProvider _serviceProvider = null!;

    private ServiceLocator() { }

    public void Initialize(ushort nodeId, IServiceProvider serviceProvider)
    {
        if (serviceProvider == null)
            throw new ArgumentNullException(nameof(serviceProvider));

        _nodeId = nodeId;
        _serviceProvider = serviceProvider;
        _logger = serviceProvider.GetRequiredService<ILogger<ServiceLocator>>();

        _logger.LogInformation("ServiceLocator initialized with NodeId: {NodeId}", nodeId);
    }

    // ========================
    // 注册方法
    // ========================

    /// <summary>
    /// 注册单实例服务
    /// </summary>
    public void RegisterSingleton<TService>(
        ServiceInstanceStrategy strategy,
        Func<IServiceProvider, TService> implementationFactory)
        where TService : BaseService
    {
        var descriptor = new ServiceDescriptor(
            typeof(TService),
            strategy,
            isMultiInstance: false,
            (sp, _) => implementationFactory(sp));

        RegisterInternal(descriptor);
    }

    /// <summary>
    /// 注册多实例服务
    /// </summary>
    public void RegisterMultiInstance<TService>(
        ServiceInstanceStrategy strategy,
        Func<IServiceProvider, string, TService> implementationFactory,
        int poolSize = 10)
        where TService : BaseService
    {
        var descriptor = new ServiceDescriptor(
            typeof(TService),
            strategy,
            isMultiInstance: true,
            (sp, instanceId) => implementationFactory(sp, instanceId ?? throw new ArgumentNullException(nameof(instanceId))),
            poolSize);

        RegisterInternal(descriptor);
    }

    private void RegisterInternal(ServiceDescriptor descriptor)
    {
        if (!_registrations.TryAdd(descriptor.ServiceType, descriptor))
        {
            _logger.LogWarning("Service {ServiceType} is already registered", descriptor.ServiceType.Name);
        }
        else
        {
            _logger.LogInformation(
                "Registered service {ServiceType} - Strategy: {Strategy}, MultiInstance: {MultiInstance}",
                descriptor.ServiceType.Name,
                descriptor.Strategy,
                descriptor.IsMultiInstance);
        }
    }

    // ========================
    // 获取服务方法
    // ========================

    /// <summary>
    /// 获取单实例服务
    /// </summary>
    public TService GetService<TService>() where TService : BaseService
    {
        var serviceType = typeof(TService);

        if (!_registrations.TryGetValue(serviceType, out var descriptor))
            throw new InvalidOperationException($"Service {serviceType.Name} is not registered");

        if (descriptor.IsMultiInstance)
            throw new InvalidOperationException($"Service {serviceType.Name} is registered as multi-instance. Use GetService<TService>(instanceId) instead.");

        return GetOrCreateService<TService>(descriptor, null);
    }

    /// <summary>
    /// 获取多实例服务
    /// </summary>
    public TService GetService<TService>(string instanceId) where TService : BaseService
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            throw new ArgumentException("InstanceId cannot be null or empty for multi-instance services", nameof(instanceId));

        var serviceType = typeof(TService);

        if (!_registrations.TryGetValue(serviceType, out var descriptor))
            throw new InvalidOperationException($"Service {serviceType.Name} is not registered");

        if (!descriptor.IsMultiInstance)
            throw new InvalidOperationException($"Service {serviceType.Name} is registered as single-instance. Use GetService<TService>() instead.");

        return GetOrCreateService<TService>(descriptor, instanceId);
    }

    /// <summary>
    /// 获取所有指定类型的服务实例
    /// </summary>
    public IEnumerable<TService> GetAllServices<TService>() where TService : BaseService
    {
        var serviceType = typeof(TService);

        // 从单实例中查找
        var singletons = _singletonInstances
            .Where(kvp => kvp.Key.ServiceType == serviceType.Name)
            .Select(kvp => kvp.Value)
            .OfType<TService>();

        // 从多实例中查找
        var multiInstances = _multiInstances
            .Where(kvp => kvp.Key.ServiceType == serviceType.Name)
            .Select(kvp => kvp.Value)
            .OfType<TService>();

        return singletons.Concat(multiInstances);
    }

    /// <summary>
    /// 根据ServiceId获取服务
    /// </summary>
    public IService? GetServiceByServiceId(ServiceId serviceId)
    {
        if (_singletonInstances.TryGetValue(serviceId, out var singleton))
            return singleton;

        if (_multiInstances.TryGetValue(serviceId, out var multi))
            return multi;

        return null;
    }

    /// <summary>
    /// 根据PID获取服务
    /// </summary>
    public IService? GetServiceByPID(PID pid)
    {
        if (_pidIndex.TryGetValue(pid, out var serviceId))
        {
            return GetServiceByServiceId(serviceId);
        }
        return null;
    }

    /// <summary>
    /// 归还池化服务
    /// </summary>
    public void ReturnPooledService<TService>(TService service, string? instanceId = null)
        where TService : BaseService
    {
        var serviceId = instanceId == null
            ? ServiceId.CreateSingleton<TService>()
            : ServiceId.CreateMultiInstance<TService>(instanceId);

        if (_pooledInstances.TryGetValue(serviceId, out var poolObj) &&
            poolObj is ServiceObjectPool<TService> pool)
        {
            pool.Return(service);
        }
    }

    // ========================
    // 内部实现方法
    // ========================

    private TService GetOrCreateService<TService>(ServiceDescriptor descriptor, string? instanceId)
        where TService : BaseService
    {
        return descriptor.Strategy switch
        {
            ServiceInstanceStrategy.Singleton => GetOrCreateSingleton<TService>(descriptor, instanceId),
            ServiceInstanceStrategy.Transient => CreateTransient<TService>(descriptor, instanceId),
            ServiceInstanceStrategy.Pooled => GetFromPool<TService>(descriptor, instanceId),
            ServiceInstanceStrategy.Global => GetOrCreateGlobal<TService>(descriptor, instanceId),
            _ => throw new NotSupportedException($"Strategy {descriptor.Strategy} is not supported")
        };
    }

    private TService GetOrCreateSingleton<TService>(ServiceDescriptor descriptor, string? instanceId)
        where TService : BaseService
    {
        var serviceId = instanceId == null
            ? ServiceId.CreateSingleton<TService>()
            : ServiceId.CreateMultiInstance<TService>(instanceId);

        var storage = descriptor.IsMultiInstance ? _multiInstances : _singletonInstances;

        var service = storage.GetOrAdd(serviceId, _ =>
        {
            var pid = PID.CreateSingleton<TService>(_nodeId, GenerateSequenceId());
            var instance = (TService)descriptor.ImplementationFactory(_serviceProvider, instanceId);
            instance.SetPID(pid);

            _pidIndex.TryAdd(pid, serviceId);

            _logger.LogDebug("Created Singleton service: {ServiceId}, PID: {PID}", serviceId, pid);
            return instance;
        });

        return (TService)service;
    }

    private TService CreateTransient<TService>(ServiceDescriptor descriptor, string? instanceId)
        where TService : BaseService
    {
        var serviceId = instanceId == null
            ? ServiceId.CreateSingleton<TService>()
            : ServiceId.CreateMultiInstance<TService>(instanceId);

        var pid = PID.CreateTransient<TService>(_nodeId, GenerateSequenceId(), instanceId ?? string.Empty);
        var service = (TService)descriptor.ImplementationFactory(_serviceProvider, instanceId);
        service.SetPID(pid);

        _logger.LogDebug("Created Transient service: {ServiceId}, PID: {PID}", serviceId, pid);
        return service;
    }

    private TService GetFromPool<TService>(ServiceDescriptor descriptor, string? instanceId)
        where TService : BaseService
    {
        var serviceId = instanceId == null
            ? ServiceId.CreateSingleton<TService>()
            : ServiceId.CreateMultiInstance<TService>(instanceId);

        var pool = (ServiceObjectPool<TService>)_pooledInstances.GetOrAdd(
            serviceId,
            _ => new ServiceObjectPool<TService>(
                () =>
                {
                    var pid = PID.CreatePooled<TService>(_nodeId, GenerateSequenceId(), instanceId ?? string.Empty);
                    var service = (TService)descriptor.ImplementationFactory(_serviceProvider, instanceId);
                    service.SetPID(pid);

                    _pidIndex.TryAdd(pid, serviceId);

                    return service;
                },
                descriptor.PoolSize,
                _logger));

        return pool.Rent();
    }

    private TService GetOrCreateGlobal<TService>(ServiceDescriptor descriptor, string? instanceId)
        where TService : BaseService
    {
        // Global策略通常是单实例的
        var serviceId = ServiceId.CreateSingleton<TService>();

        var service = _singletonInstances.GetOrAdd(serviceId, _ =>
        {
            var pid = PID.CreateGlobal<TService>(_nodeId, GenerateSequenceId());
            var instance = (TService)descriptor.ImplementationFactory(_serviceProvider, null);
            instance.SetPID(pid);

            _pidIndex.TryAdd(pid, serviceId);

            _logger.LogDebug("Created Global service: {ServiceId}, PID: {PID}", serviceId, pid);
            return instance;
        });

        return (TService)service;
    }

    private ushort GenerateSequenceId()
    {
        var next = Interlocked.Increment(ref _sequenceCounter);
        return (ushort)(next % 65536);
    }

    public void RegisterReceiver(IPulseReceiver receiver)
    {
        _receivers.Add(receiver);
    }

    internal IEnumerable<IPulseReceiver> GetAllReceivers() => _receivers;

    public async ValueTask DisposeAsync()
    {
        _logger.LogInformation("Disposing ServiceLocator...");

        // 释放所有单实例服务
        foreach (var service in _singletonInstances.Values)
        {
            await service.DisposeAsync();
        }

        // 释放所有多实例服务
        foreach (var service in _multiInstances.Values)
        {
            await service.DisposeAsync();
        }

        // 释放所有对象池
        foreach (var pool in _pooledInstances.Values)
        {
            if (pool is IAsyncDisposable disposable)
                await disposable.DisposeAsync();
        }

        _singletonInstances.Clear();
        _multiInstances.Clear();
        _pooledInstances.Clear();
        _registrations.Clear();
        _pidIndex.Clear();

        _logger.LogInformation("ServiceLocator disposed successfully");
    }
}
