using PulseRPC.Server.Abstractions;
using PulseRPC.Server.Services;

namespace PulseRPC.Server.ServiceManagement;

/// <summary>
/// Scoped 服务访问器 - 认证后绑定 Service 实例，后续直接使用
/// </summary>
/// <remarks>
/// <para><strong>使用场景</strong>：</para>
/// <list type="bullet">
/// <item><description>Hub 注册为 Scoped（每个连接一个实例）</description></item>
/// <item><description>认证成功后调用 <see cref="BindAsync"/> 绑定 Service</description></item>
/// <item><description>后续调用直接使用 <see cref="Service"/> 或 <see cref="ExecuteAsync"/></description></item>
/// </list>
/// <para><strong>使用示例</strong>：</para>
/// <code>
/// // 1. 认证成功后绑定
/// public async Task&lt;LoginResponse&gt; LoginAsync(string token)
/// {
///     var userId = ValidateToken(token);
///     await _playerService.BindAsync(userId);  // 绑定 Service
///     return new LoginResponse { Success = true };
/// }
///
/// // 2. 后续调用直接使用
/// public async Task&lt;PlayerInfo&gt; GetInfoAsync()
/// {
///     return await _playerService.ExecuteAsync(s =&gt; s.GetInfoAsync());
/// }
/// </code>
/// </remarks>
/// <typeparam name="TService">服务类型</typeparam>
public interface IScopedServiceAccessor<TService>
    where TService : class, IUnifiedPulseService
{
    /// <summary>
    /// 当前绑定的 Service 实例（未绑定时为 null）
    /// </summary>
    TService? Service { get; }

    /// <summary>
    /// 是否已绑定
    /// </summary>
    bool IsBound { get; }

    /// <summary>
    /// 绑定 Service 实例（通常在认证成功后调用）
    /// </summary>
    /// <param name="serviceId">服务实例 ID（如 playerId）</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task BindAsync(string serviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 解绑（登出时调用）
    /// </summary>
    void Unbind();

    /// <summary>
    /// 获取已绑定的 Service（未绑定时抛出异常）
    /// </summary>
    TService GetRequiredService();
}

/// <summary>
/// Scoped 服务访问器实现
/// </summary>
public sealed class ScopedServiceAccessor<TService> : IScopedServiceAccessor<TService>
    where TService : class, IUnifiedPulseService
{
    private readonly IServiceAccessor<TService> _accessor;
    private TService? _service;

    public ScopedServiceAccessor(IServiceAccessor<TService> accessor)
    {
        _accessor = accessor ?? throw new ArgumentNullException(nameof(accessor));
    }

    /// <inheritdoc/>
    public TService? Service => _service;

    /// <inheritdoc/>
    public bool IsBound => _service != null;

    /// <inheritdoc/>
    public async Task BindAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
            throw new ArgumentException("ServiceId cannot be null or whitespace", nameof(serviceId));

        _service = await _accessor.GetAsync(serviceId, cancellationToken);
    }

    /// <inheritdoc/>
    public void Unbind()
    {
        _service = null;
    }

    /// <inheritdoc/>
    public TService GetRequiredService()
    {
        return _service ?? throw new InvalidOperationException(
            $"Service {typeof(TService).Name} is not bound. Call BindAsync() after authentication.");
    }
}

/// <summary>
/// Scoped 服务访问器扩展方法
/// </summary>
public static class ScopedServiceAccessorExtensions
{
    /// <summary>
    /// 在已绑定的服务队列中执行操作
    /// </summary>
    public static async Task<TResult> ExecuteAsync<TService, TResult>(
        this IScopedServiceAccessor<TService> accessor,
        Func<TService, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
        where TService : UnifiedPulseServiceBase
    {
        var service = accessor.GetRequiredService();
        return await service.EnqueueAsync(() => operation(service), cancellationToken);
    }

    /// <summary>
    /// 在已绑定的服务队列中执行操作（无返回值）
    /// </summary>
    public static async Task ExecuteAsync<TService>(
        this IScopedServiceAccessor<TService> accessor,
        Func<TService, Task> operation,
        CancellationToken cancellationToken = default)
        where TService : UnifiedPulseServiceBase
    {
        var service = accessor.GetRequiredService();
        await service.EnqueueAsync(async () =>
        {
            await operation(service);
        }, cancellationToken);
    }

    /// <summary>
    /// 如果已绑定则执行，否则返回默认值
    /// </summary>
    public static async Task<TResult?> ExecuteIfBoundAsync<TService, TResult>(
        this IScopedServiceAccessor<TService> accessor,
        Func<TService, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
        where TService : UnifiedPulseServiceBase
        where TResult : class
    {
        if (!accessor.IsBound)
            return null;

        var service = accessor.Service!;
        return await service.EnqueueAsync(() => operation(service), cancellationToken);
    }
}

