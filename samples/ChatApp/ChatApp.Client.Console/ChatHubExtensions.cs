using PulseRPC.Client;
using System.Threading;
using System.Threading.Tasks;

namespace ChatApp.Client.Console;

/// <summary>
/// IChatHub 客户端扩展方法 - 手动实现版本
/// </summary>
/// <remarks>
/// 这是一个临时的手动实现，用于在源代码生成器修复之前提供功能支持。
/// 未来应该由 PulseRPC.Client.SourceGenerator 自动生成此类代码。
/// </remarks>
public static class ChatHubExtensions
{
    /// <summary>
    /// 获取 IChatHub 服务代理
    /// </summary>
    /// <param name="client">PulseRPC 客户端</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>ChatHub 服务代理</returns>
    public static async Task<IChatHub> GetChatHubAsync(
        this IPulseClient client,
        CancellationToken cancellationToken = default)
    {
        if (client == null)
            throw new ArgumentNullException(nameof(client));

        // 通过路由器选择连接
        var routeResult = await client.Router.RouteAsync(
            "IChatHub",
            null,
            cancellationToken);

        // 从注册表获取连接
        var channel = client.Registry.GetConnection(routeResult.Id);
        if (channel == null)
            throw new InvalidOperationException($"Connection {routeResult.Id} not found in registry");

        // 创建代理
        return new ChatHubProxy(channel);
    }
}
