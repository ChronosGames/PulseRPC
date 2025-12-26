using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Abstractions;
using PulseRPC.Server.Contexts;
using PulseRPC.Server.Services;

namespace PulseRPC.Server.ServiceManagement;

/// <summary>
/// 统一的服务访问器接口 - 合并三种访问模式
/// </summary>
/// <remarks>
/// <para>
/// <strong>设计目标</strong>：
/// </para>
/// <list type="bullet">
/// <item><description>合并 <c>IServiceAccessor</c>、<c>IContextualServiceAccessor</c>、<c>IScopedServiceAccessor</c></description></item>
/// <item><description>提供简洁统一的 API</description></item>
/// <item><description>支持多种服务获取模式</description></item>
/// </list>
/// <para>
/// <strong>使用示例</strong>：
/// </para>
/// <code>
/// public class PlayerHub : IPlayerHub
/// {
///     private readonly IUnifiedServiceAccessor&lt;PlayerService&gt; _players;
///
///     public PlayerHub(IUnifiedServiceAccessor&lt;PlayerService&gt; players)
///     {
///         _players = players;
///     }
///
///     // 方式 1: 显式指定 ServiceId
///     public async Task&lt;PlayerInfo&gt; GetPlayerAsync(string playerId)
///     {
///         return await _players.ExecuteAsync(playerId, s =&gt; s.GetInfoAsync());
///     }
///
///     // 方式 2: 从上下文自动获取
///     public async Task&lt;PlayerInfo&gt; GetMyInfoAsync()
///     {
///         return await _players.ExecuteCurrentAsync(s =&gt; s.GetInfoAsync());
///     }
///
///     // 方式 3: Scoped 绑定模式（登录后）
///     public async Task LoginAsync(string token)
///     {
///         var playerId = ValidateToken(token);
///         await _players.BindAsync(playerId);  // 绑定到当前 Scope
///     }
///
///     public async Task&lt;PlayerInfo&gt; GetBoundPlayerInfoAsync()
///     {
///         return await _players.ExecuteBoundAsync(s =&gt; s.GetInfoAsync());
///     }
/// }
/// </code>
/// </remarks>
/// <typeparam name="TService">服务类型</typeparam>
public interface IUnifiedServiceAccessor<TService> where TService : class, IUnifiedPulseService
{
    // ═══════════════════════════════════════════════════════════════════════════
    // 显式访问模式
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 获取或创建服务实例
    /// </summary>
    /// <param name="serviceId">服务实例 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>服务实例</returns>
    ValueTask<TService> GetAsync(string serviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 尝试获取已存在的服务实例（不创建）
    /// </summary>
    /// <param name="serviceId">服务实例 ID</param>
    /// <returns>服务实例，如果不存在则返回 null</returns>
    TService? TryGet(string serviceId);

    /// <summary>
    /// 获取所有活跃的服务实例
    /// </summary>
    IEnumerable<TService> GetAll();

    /// <summary>
    /// 在服务队列中执行操作（显式指定 ServiceId）
    /// </summary>
    Task<TResult> ExecuteAsync<TResult>(
        string serviceId,
        Func<TService, Task<TResult>> operation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 在服务队列中执行操作（无返回值，显式指定 ServiceId）
    /// </summary>
    Task ExecuteAsync(
        string serviceId,
        Func<TService, Task> operation,
        CancellationToken cancellationToken = default);

    // ═══════════════════════════════════════════════════════════════════════════
    // 上下文自动解析模式
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 从当前请求上下文获取服务实例
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>服务实例</returns>
    /// <exception cref="InvalidOperationException">当上下文中无法获取 ServiceId 时抛出</exception>
    ValueTask<TService> GetCurrentAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 尝试从当前请求上下文获取服务实例
    /// </summary>
    /// <returns>服务实例，如果上下文无效或服务不存在则返回 null</returns>
    TService? TryGetCurrent();

    /// <summary>
    /// 在服务队列中执行操作（从上下文自动获取 ServiceId）
    /// </summary>
    Task<TResult> ExecuteCurrentAsync<TResult>(
        Func<TService, Task<TResult>> operation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 在服务队列中执行操作（无返回值，从上下文自动获取）
    /// </summary>
    Task ExecuteCurrentAsync(
        Func<TService, Task> operation,
        CancellationToken cancellationToken = default);

    // ═══════════════════════════════════════════════════════════════════════════
    // Scoped 绑定模式（适用于认证后场景）
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 当前绑定的服务实例（未绑定时为 null）
    /// </summary>
    TService? BoundService { get; }

    /// <summary>
    /// 是否已绑定服务
    /// </summary>
    bool IsBound { get; }

    /// <summary>
    /// 绑定服务实例（通常在认证成功后调用）
    /// </summary>
    /// <param name="serviceId">服务实例 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task BindAsync(string serviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 解绑服务实例（登出时调用）
    /// </summary>
    void Unbind();

    /// <summary>
    /// 获取已绑定的服务（未绑定时抛出异常）
    /// </summary>
    TService GetBoundService();

    /// <summary>
    /// 在绑定的服务队列中执行操作
    /// </summary>
    Task<TResult> ExecuteBoundAsync<TResult>(
        Func<TService, Task<TResult>> operation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 在绑定的服务队列中执行操作（无返回值）
    /// </summary>
    Task ExecuteBoundAsync(
        Func<TService, Task> operation,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 统一服务访问器实现
/// </summary>
public sealed class UnifiedServiceAccessor<TService> : IUnifiedServiceAccessor<TService>
    where TService : class, IUnifiedPulseService
{
    private readonly UnifiedServiceManager _serviceManager;
    private readonly IServiceIdResolver _serviceIdResolver;
    private readonly ILogger<UnifiedServiceAccessor<TService>> _logger;
    private readonly string _serviceTypeName;

    // Scoped 绑定状态
    private TService? _boundService;

    public UnifiedServiceAccessor(
        UnifiedServiceManager serviceManager,
        IServiceIdResolver serviceIdResolver,
        ILogger<UnifiedServiceAccessor<TService>> logger)
    {
        _serviceManager = serviceManager ?? throw new ArgumentNullException(nameof(serviceManager));
        _serviceIdResolver = serviceIdResolver ?? throw new ArgumentNullException(nameof(serviceIdResolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // 从 PulseServiceAttribute 获取服务类型名称
        var attr = typeof(TService).GetCustomAttributes(typeof(PulseServiceAttribute), false)
            .OfType<PulseServiceAttribute>()
            .FirstOrDefault();
        _serviceTypeName = attr?.DisplayName ?? typeof(TService).Name;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 显式访问模式实现
    // ═══════════════════════════════════════════════════════════════════════════

    /// <inheritdoc/>
    public async ValueTask<TService> GetAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
            throw new ArgumentException("ServiceId cannot be null or whitespace", nameof(serviceId));

        var service = await _serviceManager.GetOrCreateServiceAsync(_serviceTypeName, serviceId, cancellationToken);

        if (service is not TService typedService)
        {
            throw new InvalidCastException($"Service {_serviceTypeName}:{serviceId} is not of type {typeof(TService).Name}");
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

    /// <inheritdoc/>
    public async Task<TResult> ExecuteAsync<TResult>(
        string serviceId,
        Func<TService, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        var service = await GetAsync(serviceId, cancellationToken);

        if (service is UnifiedPulseServiceBase baseService)
        {
            return await baseService.EnqueueAsync(() => operation(service), cancellationToken);
        }

        // 如果不是 UnifiedPulseServiceBase，直接执行
        return await operation(service);
    }

    /// <inheritdoc/>
    public async Task ExecuteAsync(
        string serviceId,
        Func<TService, Task> operation,
        CancellationToken cancellationToken = default)
    {
        var service = await GetAsync(serviceId, cancellationToken);

        if (service is UnifiedPulseServiceBase baseService)
        {
            await baseService.EnqueueAsync(async () => await operation(service), cancellationToken);
        }
        else
        {
            await operation(service);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 上下文自动解析模式实现
    // ═══════════════════════════════════════════════════════════════════════════

    /// <inheritdoc/>
    public async ValueTask<TService> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var serviceId = ResolveServiceIdFromContext();

        if (string.IsNullOrWhiteSpace(serviceId))
        {
            throw new InvalidOperationException(
                $"Cannot resolve ServiceId from current context for service type {_serviceTypeName}. " +
                "Ensure the request context contains the required information (e.g., UserId, headers).");
        }

        return await GetAsync(serviceId, cancellationToken);
    }

    /// <inheritdoc/>
    public TService? TryGetCurrent()
    {
        var serviceId = ResolveServiceIdFromContext();

        if (string.IsNullOrWhiteSpace(serviceId))
            return null;

        return TryGet(serviceId);
    }

    /// <inheritdoc/>
    public async Task<TResult> ExecuteCurrentAsync<TResult>(
        Func<TService, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        var service = await GetCurrentAsync(cancellationToken);

        if (service is UnifiedPulseServiceBase baseService)
        {
            return await baseService.EnqueueAsync(() => operation(service), cancellationToken);
        }

        return await operation(service);
    }

    /// <inheritdoc/>
    public async Task ExecuteCurrentAsync(
        Func<TService, Task> operation,
        CancellationToken cancellationToken = default)
    {
        var service = await GetCurrentAsync(cancellationToken);

        if (service is UnifiedPulseServiceBase baseService)
        {
            await baseService.EnqueueAsync(async () => await operation(service), cancellationToken);
        }
        else
        {
            await operation(service);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Scoped 绑定模式实现
    // ═══════════════════════════════════════════════════════════════════════════

    /// <inheritdoc/>
    public TService? BoundService => _boundService;

    /// <inheritdoc/>
    public bool IsBound => _boundService != null;

    /// <inheritdoc/>
    public async Task BindAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
            throw new ArgumentException("ServiceId cannot be null or whitespace", nameof(serviceId));

        _boundService = await GetAsync(serviceId, cancellationToken);
        _logger.LogDebug("Bound service {ServiceType}:{ServiceId}", _serviceTypeName, serviceId);
    }

    /// <inheritdoc/>
    public void Unbind()
    {
        if (_boundService != null)
        {
            _logger.LogDebug("Unbound service {ServiceType}:{ServiceId}", _serviceTypeName, _boundService.ServiceId);
            _boundService = null;
        }
    }

    /// <inheritdoc/>
    public TService GetBoundService()
    {
        return _boundService ?? throw new InvalidOperationException(
            $"Service {typeof(TService).Name} is not bound. Call BindAsync() after authentication.");
    }

    /// <inheritdoc/>
    public async Task<TResult> ExecuteBoundAsync<TResult>(
        Func<TService, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        var service = GetBoundService();

        if (service is UnifiedPulseServiceBase baseService)
        {
            return await baseService.EnqueueAsync(() => operation(service), cancellationToken);
        }

        return await operation(service);
    }

    /// <inheritdoc/>
    public async Task ExecuteBoundAsync(
        Func<TService, Task> operation,
        CancellationToken cancellationToken = default)
    {
        var service = GetBoundService();

        if (service is UnifiedPulseServiceBase baseService)
        {
            await baseService.EnqueueAsync(async () => await operation(service), cancellationToken);
        }
        else
        {
            await operation(service);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 私有辅助方法
    // ═══════════════════════════════════════════════════════════════════════════

    private string? ResolveServiceIdFromContext()
    {
        // 1. 优先使用注入的 Resolver
        var resolved = _serviceIdResolver.ResolveServiceId();
        if (!string.IsNullOrWhiteSpace(resolved))
        {
            return resolved;
        }

        // 2. 尝试从 PulseContext 获取
        return PulseContext.CurrentUserId;
    }
}

/// <summary>
/// UnifiedServiceAccessor DI 注册扩展
/// </summary>
public static class UnifiedServiceAccessorExtensions
{
    /// <summary>
    /// 注册统一服务访问器
    /// </summary>
    /// <typeparam name="TService">服务类型</typeparam>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合，支持链式调用</returns>
    public static Microsoft.Extensions.DependencyInjection.IServiceCollection AddUnifiedServiceAccessor<TService>(
        this Microsoft.Extensions.DependencyInjection.IServiceCollection services)
        where TService : class, IUnifiedPulseService
    {
        // 注册为 Scoped（每个请求一个实例，支持绑定模式）
        services.AddScoped<IUnifiedServiceAccessor<TService>, UnifiedServiceAccessor<TService>>();

        // 同时注册旧接口的兼容实现（转发到新实现）
        services.AddScoped<IServiceAccessor<TService>>(sp => sp.GetRequiredService<IUnifiedServiceAccessor<TService>>() as IServiceAccessor<TService>
            ?? throw new InvalidOperationException("UnifiedServiceAccessor does not implement IServiceAccessor"));

        return services;
    }
}

