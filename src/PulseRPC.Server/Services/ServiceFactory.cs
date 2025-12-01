using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Configuration;

namespace PulseRPC.Server;

/// <summary>
/// [已废弃] Service 工厂 - 动态创建和管理 Service 实例
/// </summary>
/// <remarks>
/// <para><strong>⚠️ 此类已废弃</strong>，请使用 <see cref="PulseRPC.Server.ServiceManagement.UnifiedServiceManager"/> 替代。</para>
/// </remarks>
[Obsolete("使用 UnifiedServiceManager 替代。")]
public sealed class ServiceFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ServiceFactory> _logger;
    private readonly ServiceLifecycleManager _lifecycleManager;
    private readonly ServiceLifecycleOptions _options;
    private readonly ConcurrentDictionary<Type, int> _instanceCounters = new();

    /// <summary>
    /// 构造函数
    /// </summary>
    public ServiceFactory(
        IServiceProvider serviceProvider,
        ILogger<ServiceFactory> logger,
        ServiceLifecycleManager lifecycleManager,
        ServiceLifecycleOptions? options = null)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _lifecycleManager = lifecycleManager ?? throw new ArgumentNullException(nameof(lifecycleManager));
        _options = options ?? ServiceLifecycleOptions.Default;
        _options.Validate();
    }

    /// <summary>
    /// 创建 Service 实例
    /// </summary>
    /// <typeparam name="TService">Service 类型</typeparam>
    /// <param name="instanceId">实例 ID（可选）</param>
    /// <returns>创建的 Service 实例</returns>
    public async Task<TService> CreateServiceAsync<TService>(string? instanceId = null)
        where TService : BaseService
    {
        var serviceType = typeof(TService);

        // 检查实例数量限制
        if (_options.MaxInstancesPerType > 0)
        {
            var currentCount = _instanceCounters.GetOrAdd(serviceType, 0);
            if (currentCount >= _options.MaxInstancesPerType)
            {
                throw new InvalidOperationException(
                    $"Maximum instance limit reached for {serviceType.Name} " +
                    $"(limit: {_options.MaxInstancesPerType}, current: {currentCount})");
            }
        }

        // 重试创建逻辑
        Exception? lastException = null;
        for (int attempt = 0; attempt <= _options.CreateRetryCount; attempt++)
        {
            try
            {
                if (attempt > 0)
                {
                    _logger.LogWarning(
                        "Retrying service creation - ServiceType: {ServiceType}, Attempt: {Attempt}/{MaxAttempts}",
                        serviceType.Name, attempt, _options.CreateRetryCount);

                    await Task.Delay(_options.CreateRetryInterval);
                }

                // 创建实例
                var service = CreateServiceInstance<TService>(instanceId);

                // 注册到生命周期管理器
                var serviceId = instanceId == null
                    ? ServiceId.CreateSingleton<TService>()
                    : ServiceId.CreateMultiInstance<TService>(instanceId);

                _lifecycleManager.RegisterInstance(serviceId, service);

                // 更新计数器
                _instanceCounters.AddOrUpdate(serviceType, 1, (_, count) => count + 1);

                // 启动 Service
                await service.StartAsync();

                _logger.LogInformation(
                    "Service created successfully - ServiceType: {ServiceType}, InstanceId: {InstanceId}, PID: {PID}",
                    serviceType.Name, instanceId, service.ServicePID);

                return service;
            }
            catch (Exception ex)
            {
                lastException = ex;
                _logger.LogError(ex,
                    "Failed to create service - ServiceType: {ServiceType}, InstanceId: {InstanceId}, Attempt: {Attempt}",
                    serviceType.Name, instanceId, attempt);
            }
        }

        // 所有重试都失败
        throw new InvalidOperationException(
            $"Failed to create service after {_options.CreateRetryCount} retries: {serviceType.Name}",
            lastException);
    }

    /// <summary>
    /// 获取或创建 Service 实例
    /// </summary>
    /// <typeparam name="TService">Service 类型</typeparam>
    /// <param name="instanceId">实例 ID</param>
    /// <param name="createIfNotExists">如果不存在则创建</param>
    /// <returns>Service 实例，如果不存在且不创建则返回 null</returns>
    public async Task<TService?> GetOrCreateServiceAsync<TService>(
        string instanceId,
        bool createIfNotExists = true)
        where TService : BaseService
    {
        var serviceId = ServiceId.CreateMultiInstance<TService>(instanceId);

        // 尝试从生命周期管理器获取
        var metadata = _lifecycleManager.GetMetadata(serviceId);
        if (metadata != null)
        {
            // 更新活跃时间
            _lifecycleManager.UpdateActivity(serviceId);

            // 增加引用计数
            if (_options.EnableReferenceCount)
            {
                _lifecycleManager.AddReference(serviceId);
            }

            return (TService)metadata.Service;
        }

        // 不存在，根据参数决定是否创建
        if (!createIfNotExists)
            return null;

        // 创建新实例
        return await CreateServiceAsync<TService>(instanceId);
    }

    /// <summary>
    /// 销毁 Service 实例
    /// </summary>
    /// <typeparam name="TService">Service 类型</typeparam>
    /// <param name="instanceId">实例 ID（可选）</param>
    /// <returns>是否成功销毁</returns>
    public async Task<bool> DestroyServiceAsync<TService>(string? instanceId = null)
        where TService : BaseService
    {
        var serviceType = typeof(TService);
        var serviceId = instanceId == null
            ? ServiceId.CreateSingleton<TService>()
            : ServiceId.CreateMultiInstance<TService>(instanceId);

        var result = await _lifecycleManager.DestroyInstanceAsync(serviceId);

        if (result)
        {
            // 更新计数器
            _instanceCounters.AddOrUpdate(serviceType, 0, (_, count) => Math.Max(0, count - 1));

            _logger.LogInformation(
                "Service destroyed - ServiceType: {ServiceType}, InstanceId: {InstanceId}",
                serviceType.Name, instanceId);
        }

        return result;
    }

    /// <summary>
    /// 释放 Service 引用
    /// </summary>
    public void ReleaseService<TService>(string? instanceId = null)
        where TService : BaseService
    {
        var serviceId = instanceId == null
            ? ServiceId.CreateSingleton<TService>()
            : ServiceId.CreateMultiInstance<TService>(instanceId);

        _lifecycleManager.ReleaseReference(serviceId);
    }

    /// <summary>
    /// 获取指定类型的实例数量
    /// </summary>
    public int GetInstanceCount<TService>()
        where TService : BaseService
    {
        var serviceType = typeof(TService);
        return _instanceCounters.TryGetValue(serviceType, out var count) ? count : 0;
    }

    /// <summary>
    /// 获取所有 Service 类型的实例数量
    /// </summary>
    public IReadOnlyDictionary<Type, int> GetAllInstanceCounts()
    {
        return _instanceCounters.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    // ========== 内部实现 ==========

    /// <summary>
    /// 创建 Service 实例（使用 DI 容器）
    /// </summary>
    private TService CreateServiceInstance<TService>(string? instanceId)
        where TService : BaseService
    {
        try
        {
            // 使用 DI 容器创建实例
            var service = ActivatorUtilities.CreateInstance<TService>(_serviceProvider);

            // 生成 PID
            var nodeId = GetNodeId();
            var sequenceId = GenerateSequenceId();
            var pid = instanceId == null
                ? PID.CreateSingleton<TService>(nodeId, sequenceId)
                : PID.CreateTransient<TService>(nodeId, sequenceId, instanceId);

            // 设置 PID（使用反射调用内部方法）
            var setMethod = typeof(TService).GetMethod("SetPID",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (setMethod != null)
            {
                setMethod.Invoke(service, new object[] { pid });
            }
            else
            {
                // 尝试 BaseService 的 SetPID
                var baseMethod = typeof(BaseService).GetMethod("SetPID",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                baseMethod?.Invoke(service, new object[] { pid });
            }

            return service;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to create service instance - ServiceType: {ServiceType}, InstanceId: {InstanceId}",
                typeof(TService).Name, instanceId);
            throw;
        }
    }

    /// <summary>
    /// 获取节点 ID
    /// </summary>
    private ushort GetNodeId()
    {
        // 从配置或环境变量获取节点 ID
        // 这里简化处理，实际应该从配置中读取
        return 1;
    }

    private int _sequenceCounter;

    /// <summary>
    /// 生成序列号
    /// </summary>
    private ushort GenerateSequenceId()
    {
        var seq = Interlocked.Increment(ref _sequenceCounter);
        return (ushort)(seq % ushort.MaxValue);
    }
}

/// <summary>
/// Service 引用包装器 - 自动管理引用计数
/// </summary>
/// <typeparam name="TService">Service 类型</typeparam>
public sealed class ServiceReference<TService> : IAsyncDisposable
    where TService : BaseService
{
    private readonly TService _service;
    private readonly ServiceFactory _factory;
    private readonly string? _instanceId;
    private bool _disposed;

    internal ServiceReference(TService service, ServiceFactory factory, string? instanceId)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _instanceId = instanceId;
    }

    /// <summary>
    /// Service 实例
    /// </summary>
    public TService Service => _service;

    /// <summary>
    /// 释放引用
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _factory.ReleaseService<TService>(_instanceId);
            _disposed = true;
        }

        await ValueTask.CompletedTask;
    }
}
