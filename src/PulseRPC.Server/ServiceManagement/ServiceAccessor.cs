using Microsoft.Extensions.Logging;
using PulseRPC.Server.Abstractions;
using PulseRPC.Server.Services;

namespace PulseRPC.Server.ServiceManagement;

/// <summary>
/// 服务访问器实现
/// </summary>
/// <typeparam name="TService">服务类型</typeparam>
public sealed class ServiceAccessor<TService> : IServiceAccessor<TService>
    where TService : class, IUnifiedPulseService
{
    private readonly UnifiedServiceManager _serviceManager;
    private readonly ILogger<ServiceAccessor<TService>> _logger;
    private readonly string _serviceTypeName;

    public ServiceAccessor(
        UnifiedServiceManager serviceManager,
        ILogger<ServiceAccessor<TService>> logger)
    {
        _serviceManager = serviceManager ?? throw new ArgumentNullException(nameof(serviceManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // 从 PulseServiceAttribute 获取服务类型名称
        var attr = typeof(TService).GetCustomAttributes(typeof(PulseServiceAttribute), false)
            .OfType<PulseServiceAttribute>()
            .FirstOrDefault();
        _serviceTypeName = attr?.DisplayName ?? typeof(TService).Name;
    }

    /// <inheritdoc/>
    public async ValueTask<TService> GetAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
            throw new ArgumentException("ServiceId cannot be null or whitespace", nameof(serviceId));

        var service = await _serviceManager.GetOrCreateServiceAsync(
            _serviceTypeName, serviceId, cancellationToken);

        if (service is not TService typedService)
        {
            throw new InvalidCastException(
                $"Service {_serviceTypeName}:{serviceId} is not of type {typeof(TService).Name}");
        }

        return typedService;
    }

    /// <inheritdoc/>
    public TService? TryGet(string serviceId)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
            return null;

        return _serviceManager.GetService(_serviceTypeName, serviceId) as TService;
    }

    /// <inheritdoc/>
    public IEnumerable<TService> GetAll()
    {
        return _serviceManager.GetServicesByType(_serviceTypeName).OfType<TService>();
    }
}

/// <summary>
/// 服务访问器扩展方法
/// </summary>
public static class ServiceAccessorExtensions
{
    /// <summary>
    /// 在服务队列中执行操作
    /// </summary>
    /// <typeparam name="TService">服务类型</typeparam>
    /// <typeparam name="TResult">返回类型</typeparam>
    /// <param name="accessor">服务访问器</param>
    /// <param name="serviceId">服务实例 ID</param>
    /// <param name="operation">要执行的操作</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>操作结果</returns>
    /// <remarks>
    /// <para>
    /// 此方法获取服务实例并在其队列中执行操作，确保线程安全。
    /// </para>
    /// <code>
    /// var playerInfo = await _playerService.ExecuteAsync(
    ///     playerId,
    ///     service => service.GetPlayerInfoAsync());
    /// </code>
    /// </remarks>
    public static async Task<TResult> ExecuteAsync<TService, TResult>(
        this IServiceAccessor<TService> accessor,
        string serviceId,
        Func<TService, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
        where TService : UnifiedPulseServiceBase
    {
        var service = await accessor.GetAsync(serviceId, cancellationToken);
        return await service.EnqueueAsync(() => operation(service), cancellationToken);
    }

    /// <summary>
    /// 在服务队列中执行操作（无返回值）
    /// </summary>
    public static async Task ExecuteAsync<TService>(
        this IServiceAccessor<TService> accessor,
        string serviceId,
        Func<TService, Task> operation,
        CancellationToken cancellationToken = default)
        where TService : UnifiedPulseServiceBase
    {
        var service = await accessor.GetAsync(serviceId, cancellationToken);
        await service.EnqueueAsync(async () =>
        {
            await operation(service);
        }, cancellationToken);
    }
}
