using Microsoft.Extensions.Logging;
using PulseRPC.Server.Builder;
using PulseRPC.Sessions;

namespace PulseRPC.Server.Middleware;

/// <summary>
/// 会话上下文中间件 - 在处理RPC请求时设置当前会话上下文
/// 允许IPulseService实现类通过DI获取ISessionContextAccessor来访问会话上下文
/// 符合三层抽象架构的应用层设计
/// </summary>
internal sealed class SessionContextMiddleware : IPulseRpcMiddleware
{
    private readonly ILogger<SessionContextMiddleware> _logger;
    private readonly ISessionContextAccessor _contextAccessor;
    private readonly IClientSessionManager _sessionManager;

    public SessionContextMiddleware(
        ILogger<SessionContextMiddleware> logger,
        ISessionContextAccessor contextAccessor,
        IClientSessionManager sessionManager)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _contextAccessor = contextAccessor ?? throw new ArgumentNullException(nameof(contextAccessor));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
    }

    /// <summary>
    /// 执行中间件逻辑
    /// </summary>
    /// <param name="context">请求上下文</param>
    /// <param name="next">下一个中间件</param>
    /// <returns>处理任务</returns>
    public async Task InvokeAsync(IPulseRpcContext context, Func<Task> next)
    {
        // 尝试从请求上下文中获取会话ID（连接ID）
        var sessionId = ExtractSessionId(context);

        IClientSession? clientSession = null;
        if (!string.IsNullOrEmpty(sessionId))
        {
            clientSession = _sessionManager.GetSession(sessionId);
            if (clientSession != null)
            {
                _logger.LogDebug("设置会话上下文：{SessionId} (认证状态: {IsAuthenticated})",
                    clientSession.SessionId, clientSession.IsAuthenticated);

                // 更新最后活动时间（如果会话支持此操作）
                if (clientSession.Statistics != null)
                {
                    clientSession.Statistics.LastActivityAt = DateTime.UtcNow;
                }
            }
            else
            {
                _logger.LogWarning("未找到会话ID对应的客户端会话：{SessionId}", sessionId);
            }
        }
        else
        {
            _logger.LogDebug("请求上下文中未找到会话ID");
        }

        // 设置当前会话上下文
        var previousSession = _contextAccessor.Current;
        try
        {
            _contextAccessor.SetCurrent(clientSession);

            // 执行下一个中间件
            await next();
        }
        finally
        {
            // 恢复之前的会话上下文
            _contextAccessor.SetCurrent(previousSession);
        }
    }

    /// <summary>
    /// 从请求上下文中提取会话ID
    /// </summary>
    /// <param name="context">请求上下文</param>
    /// <returns>会话ID，如果不存在则返回null</returns>
    private static string? ExtractSessionId(IPulseRpcContext context)
    {
        // 尝试从上下文数据中获取会话ID（连接ID）
        if (context.Items.TryGetValue("SessionId", out var sessionIdObj) &&
            sessionIdObj is string sessionId &&
            !string.IsNullOrEmpty(sessionId))
        {
            return sessionId;
        }

        // 兼容性：尝试从ConnectionId获取
        if (context.Items.TryGetValue("ConnectionId", out var connectionIdObj) &&
            connectionIdObj is string connectionId &&
            !string.IsNullOrEmpty(connectionId))
        {
            return connectionId;
        }

        // 尝试从请求ID中提取会话ID（如果请求ID包含连接信息）
        if (!string.IsNullOrEmpty(context.RequestId) &&
            context.RequestId.Contains("-sess-"))
        {
            var parts = context.RequestId.Split("-sess-");
            if (parts.Length > 1)
            {
                return parts[0]; // 会话ID通常在请求ID的前半部分
            }
        }

        // 兼容性：尝试从旧的格式提取
        if (!string.IsNullOrEmpty(context.RequestId) &&
            context.RequestId.Contains("-conn-"))
        {
            var parts = context.RequestId.Split("-conn-");
            if (parts.Length > 1)
            {
                return parts[0]; // 连接ID通常在请求ID的前半部分
            }
        }

        return null;
    }
}

/// <summary>
/// 会话上下文中间件配置扩展
/// </summary>
public static class SessionContextMiddlewareExtensions
{
    /// <summary>
    /// 添加会话上下文中间件
    /// </summary>
    /// <param name="builder">服务器构建器</param>
    /// <returns>服务器构建器</returns>
    public static IPulseRPCServerBuilder UseSessionContextMiddleware(this IPulseRPCServerBuilder builder)
    {
        return builder.UseMiddleware<SessionContextMiddleware>();
    }

    /// <summary>
    /// 添加传输上下文中间件（向后兼容的别名）
    /// </summary>
    /// <param name="builder">服务器构建器</param>
    /// <returns>服务器构建器</returns>
    [Obsolete("Use UseSessionContextMiddleware instead")]
    public static IPulseRPCServerBuilder UseTransportContextMiddleware(this IPulseRPCServerBuilder builder)
    {
        return builder.UseMiddleware<SessionContextMiddleware>();
    }
}