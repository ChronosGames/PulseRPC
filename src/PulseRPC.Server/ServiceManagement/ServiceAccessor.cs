using Microsoft.Extensions.Logging;
using PulseRPC.Server.Abstractions;
using PulseRPC.Server.Contexts;
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

    // ═══════════════════════════════════════════════════════════════════════
    // Singleton 服务的简化 API
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 在 Singleton 服务队列中执行操作（自动使用默认 ServiceId）
    /// </summary>
    /// <remarks>
    /// <para>仅适用于 <see cref="ServiceInstanceScope.Singleton"/> 服务。</para>
    /// <para>对于 MultiInstance 服务，此方法将抛出 <see cref="InvalidOperationException"/>。</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // 之前（冗余）
    /// await _guildService.ExecuteAsync("default", s => s.CreateGuildAsync(userId, request));
    ///
    /// // 之后（简洁）
    /// await _guildService.Execute(s => s.CreateGuildAsync(userId, request));
    /// </code>
    /// </example>
    public static async Task<TResult> Execute<TService, TResult>(
        this IServiceAccessor<TService> accessor,
        Func<TService, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
        where TService : UnifiedPulseServiceBase
    {
        var metadata = ServiceMetadataCache.Get<TService>();

        if (!metadata.IsSingleton || metadata.DefaultServiceId == null)
        {
            throw new InvalidOperationException(
                $"Service {typeof(TService).Name} is not a Singleton service. " +
                $"Use ExecuteAsync(serviceId, operation) for MultiInstance services.");
        }

        return await accessor.ExecuteAsync(metadata.DefaultServiceId, operation, cancellationToken);
    }

    /// <summary>
    /// 在 Singleton 服务队列中执行操作（无返回值，自动使用默认 ServiceId）
    /// </summary>
    /// <remarks>
    /// <para>仅适用于 <see cref="ServiceInstanceScope.Singleton"/> 服务。</para>
    /// </remarks>
    public static async Task Execute<TService>(
        this IServiceAccessor<TService> accessor,
        Func<TService, Task> operation,
        CancellationToken cancellationToken = default)
        where TService : UnifiedPulseServiceBase
    {
        var metadata = ServiceMetadataCache.Get<TService>();

        if (!metadata.IsSingleton || metadata.DefaultServiceId == null)
        {
            throw new InvalidOperationException(
                $"Service {typeof(TService).Name} is not a Singleton service. " +
                $"Use ExecuteAsync(serviceId, operation) for MultiInstance services.");
        }

        await accessor.ExecuteAsync(metadata.DefaultServiceId, operation, cancellationToken);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 使用当前用户 ID 作为 ServiceId
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 使用当前用户 ID 作为 ServiceId 执行操作
    /// </summary>
    /// <remarks>
    /// <para>适用于 <see cref="ServiceInstanceScope.MultiInstance"/> 服务，
    /// 其中每个用户对应一个服务实例。</para>
    /// <para>自动从 <see cref="PulseContext.CurrentUserId"/> 获取用户 ID。</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // 之前（冗余）
    /// var userId = GetCurrentUserId();
    /// await _playerService.ExecuteAsync(userId, s => s.GetInfoAsync());
    ///
    /// // 之后（简洁）
    /// await _playerService.ExecuteForCurrentUser(s => s.GetInfoAsync());
    /// </code>
    /// </example>
    public static async Task<TResult> ExecuteForCurrentUser<TService, TResult>(
        this IServiceAccessor<TService> accessor,
        Func<TService, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
        where TService : UnifiedPulseServiceBase
    {
        var userId = PulseContext.CurrentUserId
            ?? throw new InvalidOperationException(
                "No authenticated user in current context. " +
                "Ensure the request has been authenticated.");

        return await accessor.ExecuteAsync(userId, operation, cancellationToken);
    }

    /// <summary>
    /// 使用当前用户 ID 作为 ServiceId 执行操作（无返回值）
    /// </summary>
    public static async Task ExecuteForCurrentUser<TService>(
        this IServiceAccessor<TService> accessor,
        Func<TService, Task> operation,
        CancellationToken cancellationToken = default)
        where TService : UnifiedPulseServiceBase
    {
        var userId = PulseContext.CurrentUserId
            ?? throw new InvalidOperationException(
                "No authenticated user in current context. " +
                "Ensure the request has been authenticated.");

        await accessor.ExecuteAsync(userId, operation, cancellationToken);
    }

    /// <summary>
    /// 使用当前用户 ID 作为 ServiceId 执行操作，并自动传递 UserId 给操作
    /// </summary>
    /// <remarks>
    /// <para>适用于服务方法需要 UserId 作为参数的场景。</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // 之前（冗余）
    /// var userId = GetCurrentUserId();
    /// await _socialService.ExecuteAsync("local", s => s.AddFriendAsync(userId, friendId));
    ///
    /// // 之后（简洁）- 适用于 Singleton 服务需要 userId 参数
    /// await _socialService.ExecuteWithUserId((s, userId) => s.AddFriendAsync(userId, friendId));
    /// </code>
    /// </example>
    public static async Task<TResult> ExecuteWithUserId<TService, TResult>(
        this IServiceAccessor<TService> accessor,
        Func<TService, string, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
        where TService : UnifiedPulseServiceBase
    {
        var userId = PulseContext.CurrentUserId
            ?? throw new InvalidOperationException(
                "No authenticated user in current context.");

        var metadata = ServiceMetadataCache.Get<TService>();

        // 对于 Singleton 服务，使用默认 ServiceId；否则使用 UserId
        var serviceId = metadata.IsSingleton && metadata.DefaultServiceId != null
            ? metadata.DefaultServiceId
            : userId;

        var service = await accessor.GetAsync(serviceId, cancellationToken);
        return await service.EnqueueAsync(() => operation(service, userId), cancellationToken);
    }

    /// <summary>
    /// 使用当前用户 ID 作为 ServiceId 执行操作，并自动传递 UserId 给操作（无返回值）
    /// </summary>
    public static async Task ExecuteWithUserId<TService>(
        this IServiceAccessor<TService> accessor,
        Func<TService, string, Task> operation,
        CancellationToken cancellationToken = default)
        where TService : UnifiedPulseServiceBase
    {
        var userId = PulseContext.CurrentUserId
            ?? throw new InvalidOperationException(
                "No authenticated user in current context.");

        var metadata = ServiceMetadataCache.Get<TService>();

        // 对于 Singleton 服务，使用默认 ServiceId；否则使用 UserId
        var serviceId = metadata.IsSingleton && metadata.DefaultServiceId != null
            ? metadata.DefaultServiceId
            : userId;

        var service = await accessor.GetAsync(serviceId, cancellationToken);
        await service.EnqueueAsync(async () => await operation(service, userId), cancellationToken);
    }
}
