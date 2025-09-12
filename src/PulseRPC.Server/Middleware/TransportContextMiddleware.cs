using Microsoft.Extensions.Logging;
using PulseRPC.Server.Builder;
using PulseRPC.Transport;

namespace PulseRPC.Server.Middleware;

/// <summary>
/// 传输上下文中间件 - 在处理RPC请求时设置当前传输上下文
/// 允许IPulseService实现类通过DI获取ITransportContextAccessor来访问传输上下文
/// </summary>
internal sealed class TransportContextMiddleware : IPulseRpcMiddleware
{
    private readonly ILogger<TransportContextMiddleware> _logger;
    private readonly ITransportContextAccessor _contextAccessor;
    private readonly ITransportManager _transportManager;

    public TransportContextMiddleware(
        ILogger<TransportContextMiddleware> logger,
        ITransportContextAccessor contextAccessor,
        ITransportManager transportManager)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _contextAccessor = contextAccessor ?? throw new ArgumentNullException(nameof(contextAccessor));
        _transportManager = transportManager ?? throw new ArgumentNullException(nameof(transportManager));
    }

    /// <summary>
    /// 执行中间件逻辑
    /// </summary>
    /// <param name="context">请求上下文</param>
    /// <param name="next">下一个中间件</param>
    /// <returns>处理任务</returns>
    public async Task InvokeAsync(IPulseRpcContext context, Func<Task> next)
    {
        // 尝试从请求上下文中获取连接ID
        var connectionId = ExtractConnectionId(context);

        TransportContext? transportContext = null;
        if (!string.IsNullOrEmpty(connectionId))
        {
            transportContext = _transportManager.GetTransportContext(connectionId);
            if (transportContext != null)
            {
                _logger.LogDebug("设置传输上下文：{ConnectionId} ({TransportType})",
                    transportContext.ConnectionId, transportContext.TransportType);

                // 更新最后活动时间
                transportContext.UpdateLastActiveTime();
            }
            else
            {
                _logger.LogWarning("未找到连接ID对应的传输上下文：{ConnectionId}", connectionId);
            }
        }
        else
        {
            _logger.LogDebug("请求上下文中未找到连接ID");
        }

        // 设置当前传输上下文
        var previousContext = _contextAccessor.Current;
        try
        {
            _contextAccessor.SetCurrent(transportContext);

            // 执行下一个中间件
            await next();
        }
        finally
        {
            // 恢复之前的上下文
            _contextAccessor.SetCurrent(previousContext);
        }
    }

    /// <summary>
    /// 从请求上下文中提取连接ID
    /// </summary>
    /// <param name="context">请求上下文</param>
    /// <returns>连接ID，如果不存在则返回null</returns>
    private static string? ExtractConnectionId(IPulseRpcContext context)
    {
        // 尝试从上下文数据中获取连接ID
        if (context.Items.TryGetValue("ConnectionId", out var connectionIdObj) &&
            connectionIdObj is string connectionId &&
            !string.IsNullOrEmpty(connectionId))
        {
            return connectionId;
        }

        // 尝试从请求ID中提取连接ID（如果请求ID包含连接信息）
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
/// 传输上下文中间件配置扩展
/// </summary>
public static class TransportContextMiddlewareExtensions
{
    /// <summary>
    /// 添加传输上下文中间件
    /// </summary>
    /// <param name="builder">服务器构建器</param>
    /// <returns>服务器构建器</returns>
    public static IPulseRPCServerBuilder UseTransportContextMiddleware(this IPulseRPCServerBuilder builder)
    {
        return builder.UseMiddleware<TransportContextMiddleware>();
    }
}
