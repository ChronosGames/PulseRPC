using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseRPC.Server.Processing;
using PulseRPC.Server.Sessions;
using PulseRPC.Server.Transport;

namespace PulseRPC.Server.Extensions;

/// <summary>
/// PulseRPC Server 依赖注入扩展方法 - 三层抽象架构增强版
/// </summary>
public static class ServerServiceCollectionExtensions
{
    /// <summary>
    /// 添加PulseRPC服务端核心服务（传统模式）
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddPulseServer(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // 注册传统的通道管理器
        services.TryAddSingleton<IServerChannelManager, ServerChannelManager>();

        return services;
    }

    /// <summary>
    /// 添加PulseRPC服务端增强服务（三层抽象架构）
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configure">配置选项</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddEnhancedPulseServer(this IServiceCollection services, Action<EnhancedServerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // 配置选项
        if (configure != null)
        {
            services.Configure(configure);
        }

        // 注册传统的通道管理器
        services.TryAddSingleton<IServerChannelManager, ServerChannelManager>();

        // 注册新的会话管理器
        services.TryAddSingleton<ServerSessionManager>();
        services.TryAddSingleton<IServerSessionManager>(provider => provider.GetRequiredService<ServerSessionManager>());
        services.TryAddSingleton<IClientSessionManager>(provider => provider.GetRequiredService<ServerSessionManager>());

        return services;
    }

    /// <summary>
    /// 添加客户端会话管理服务
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configure">配置选项</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddClientSessionManagement(this IServiceCollection services, Action<SessionManagementOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // 配置选项
        if (configure != null)
        {
            services.Configure(configure);
        }

        // 注册会话管理服务
        services.TryAddSingleton<IServerSessionManager, ServerSessionManager>();
        services.TryAddSingleton<IClientSessionManager>(provider => provider.GetRequiredService<IServerSessionManager>() as IClientSessionManager ??
            throw new InvalidOperationException("ServerSessionManager must implement IClientSessionManager"));

        return services;
    }

    /// <summary>
    /// 添加会话健康检查服务
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configure">配置选项</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddSessionHealthChecks(this IServiceCollection services, Action<SessionHealthCheckOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // 配置选项
        if (configure != null)
        {
            services.Configure(configure);
        }

        // 注册健康检查服务
        services.TryAddSingleton<ISessionHealthChecker, SessionHealthChecker>();

        return services;
    }

    /// <summary>
    /// 添加会话广播服务
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddSessionBroadcast(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // 注册广播服务
        services.TryAddSingleton<ISessionBroadcastService, SessionBroadcastService>();

        return services;
    }

    /// <summary>
    /// 注册核心服务端服务
    /// </summary>
    /// <param name="services">服务集合</param>
    private static void RegisterCoreServerServices(IServiceCollection services)
    {
        // 这里可以注册其他核心服务，如序列化器、消息处理器等
        // 具体实现依赖于现有的服务注册逻辑
    }
}

/// <summary>
/// 增强服务器配置选项
/// </summary>
public class EnhancedServerOptions
{
    /// <summary>
    /// 是否启用会话管理
    /// </summary>
    public bool EnableSessionManagement { get; set; } = true;

    /// <summary>
    /// 是否启用自动会话清理
    /// </summary>
    public bool EnableAutoSessionCleanup { get; set; } = true;

    /// <summary>
    /// 会话超时时间（毫秒）
    /// </summary>
    public int SessionTimeoutMs { get; set; } = 300000; // 5分钟

    /// <summary>
    /// 健康检查间隔（毫秒）
    /// </summary>
    public int HealthCheckIntervalMs { get; set; } = 30000; // 30秒

    /// <summary>
    /// 清理间隔（毫秒）
    /// </summary>
    public int CleanupIntervalMs { get; set; } = 60000; // 1分钟
}

/// <summary>
/// 会话管理配置选项
/// </summary>
public class SessionManagementOptions
{
    /// <summary>
    /// 会话超时时间（毫秒）
    /// </summary>
    public int SessionTimeoutMs { get; set; } = 300000; // 5分钟

    /// <summary>
    /// 最大并发会话数
    /// </summary>
    public int MaxConcurrentSessions { get; set; } = 10000;

    /// <summary>
    /// 是否启用会话统计
    /// </summary>
    public bool EnableSessionStatistics { get; set; } = true;

    /// <summary>
    /// 是否启用会话事件
    /// </summary>
    public bool EnableSessionEvents { get; set; } = true;
}

/// <summary>
/// 会话健康检查配置选项
/// </summary>
public class SessionHealthCheckOptions
{
    /// <summary>
    /// 健康检查间隔（毫秒）
    /// </summary>
    public int CheckIntervalMs { get; set; } = 30000; // 30秒

    /// <summary>
    /// 健康检查超时（毫秒）
    /// </summary>
    public int CheckTimeoutMs { get; set; } = 5000; // 5秒

    /// <summary>
    /// 最大不健康持续时间（毫秒）
    /// </summary>
    public int MaxUnhealthyDurationMs { get; set; } = 120000; // 2分钟

    /// <summary>
    /// 是否启用自动清理不健康会话
    /// </summary>
    public bool EnableAutoCleanup { get; set; } = true;
}

/// <summary>
/// 会话健康检查器接口
/// </summary>
public interface ISessionHealthChecker
{
    /// <summary>
    /// 检查会话健康状态
    /// </summary>
    /// <param name="session">客户端会话</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>健康检查结果</returns>
    Task<SessionHealthCheckResult> CheckSessionHealthAsync(IClientSession session, CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查所有会话健康状态
    /// </summary>
    /// <param name="sessions">会话集合</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>健康检查结果集合</returns>
    Task<IEnumerable<SessionHealthCheckResult>> CheckSessionsHealthAsync(IEnumerable<IClientSession> sessions, CancellationToken cancellationToken = default);
}

/// <summary>
/// 会话健康检查器实现
/// </summary>
internal class SessionHealthChecker : ISessionHealthChecker
{
    private readonly IOptions<SessionHealthCheckOptions> _options;
    private readonly ILogger<SessionHealthChecker> _logger;

    public SessionHealthChecker(IOptions<SessionHealthCheckOptions> options, ILogger<SessionHealthChecker> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 检查会话健康状态
    /// </summary>
    public async Task<SessionHealthCheckResult> CheckSessionHealthAsync(IClientSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        try
        {
            using var timeoutCts = new CancellationTokenSource(_options.Value.CheckTimeoutMs);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            return await session.CheckHealthAsync(linkedCts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "检查会话健康状态失败: SessionId={SessionId}", session.Descriptor.Id);

            return new SessionHealthCheckResult
            {
                SessionId = session.Descriptor.Id,
                Health = SessionHealth.Failed,
                ResponseTime = TimeSpan.Zero,
                Message = $"健康检查异常: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// 检查所有会话健康状态
    /// </summary>
    public async Task<IEnumerable<SessionHealthCheckResult>> CheckSessionsHealthAsync(IEnumerable<IClientSession> sessions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        var sessionList = sessions.ToList();
        var tasks = sessionList.Select(session => CheckSessionHealthAsync(session, cancellationToken));

        return await Task.WhenAll(tasks);
    }
}

/// <summary>
/// 会话广播服务接口
/// </summary>
public interface ISessionBroadcastService
{
    /// <summary>
    /// 向指定会话组广播消息
    /// </summary>
    /// <typeparam name="THub">Hub类型</typeparam>
    /// <param name="groupName">组名</param>
    /// <param name="methodName">方法名</param>
    /// <param name="args">参数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>成功广播的会话数量</returns>
    Task<int> BroadcastToGroupAsync<THub>(string groupName, string methodName, object?[] args, CancellationToken cancellationToken = default) where THub : class, IPulseHub;

    /// <summary>
    /// 向所有已认证会话广播消息
    /// </summary>
    /// <typeparam name="THub">Hub类型</typeparam>
    /// <param name="methodName">方法名</param>
    /// <param name="args">参数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>成功广播的会话数量</returns>
    Task<int> BroadcastToAllAsync<THub>(string methodName, object?[] args, CancellationToken cancellationToken = default) where THub : class, IPulseHub;

    /// <summary>
    /// 向指定用户广播消息
    /// </summary>
    /// <typeparam name="THub">Hub类型</typeparam>
    /// <param name="username">用户名</param>
    /// <param name="methodName">方法名</param>
    /// <param name="args">参数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>成功广播的会话数量</returns>
    Task<int> BroadcastToUserAsync<THub>(string username, string methodName, object?[] args, CancellationToken cancellationToken = default) where THub : class, IPulseHub;
}

/// <summary>
/// 会话广播服务实现
/// </summary>
internal class SessionBroadcastService : ISessionBroadcastService
{
    private readonly IServerSessionManager _sessionManager;
    private readonly ILogger<SessionBroadcastService> _logger;

    public SessionBroadcastService(IServerSessionManager sessionManager, ILogger<SessionBroadcastService> logger)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 向指定会话组广播消息
    /// </summary>
    public Task<int> BroadcastToGroupAsync<THub>(string groupName, string methodName, object?[] args, CancellationToken cancellationToken = default) where THub : class, IPulseHub
    {
        return _sessionManager.BroadcastToGroupAsync<THub>(groupName, methodName, args, cancellationToken);
    }

    /// <summary>
    /// 向所有已认证会话广播消息
    /// </summary>
    public Task<int> BroadcastToAllAsync<THub>(string methodName, object?[] args, CancellationToken cancellationToken = default) where THub : class, IPulseHub
    {
        return _sessionManager.BroadcastToAllAsync<THub>(methodName, args, cancellationToken);
    }

    /// <summary>
    /// 向指定用户广播消息
    /// </summary>
    public async Task<int> BroadcastToUserAsync<THub>(string username, string methodName, object?[] args, CancellationToken cancellationToken = default) where THub : class, IPulseHub
    {
        var userSessions = _sessionManager.GetSessionsByUser(username).ToList();
        var tasks = userSessions.Select(async session =>
        {
            try
            {
                await session.InvokeAsync<THub>(methodName, args, cancellationToken);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "向用户会话 {SessionId} 广播消息失败: {HubType}.{MethodName}",
                    session.Descriptor.Id, typeof(THub).Name, methodName);
                return false;
            }
        });

        var results = await Task.WhenAll(tasks);
        return results.Count(success => success);
    }
}
