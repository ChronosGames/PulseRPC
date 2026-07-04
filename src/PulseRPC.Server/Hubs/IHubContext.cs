namespace PulseRPC.Server;

/// <summary>
/// 服务端推送上下文接口 - 类似 SignalR 的 IHubContext
/// </summary>
/// <typeparam name="TReceiver">接收器接口类型，必须继承 <see cref="IPulseHub"/></typeparam>
/// <remarks>
/// <para>
/// 通过依赖注入获取此接口，用于在服务端向客户端推送消息。
/// </para>
/// <para>
/// <strong>使用示例</strong>：
/// </para>
/// <code>
/// public class MatchmakingService
/// {
///     private readonly IHubContext&lt;IGameReceiver&gt; _hubContext;
///
///     public MatchmakingService(IHubContext&lt;IGameReceiver&gt; hubContext)
///     {
///         _hubContext = hubContext;
///     }
///
///     public async Task OnMatchComplete(string[] playerIds, MatchInfo match)
///     {
///         var notification = new MatchFoundNotification { Match = match };
///         await _hubContext.Clients.Users(playerIds).OnMatchFoundAsync(notification);
///     }
/// }
/// </code>
/// </remarks>
public interface IHubContext<TReceiver> where TReceiver : class, IPulseHub
{
    /// <summary>
    /// 获取客户端选择器
    /// </summary>
    /// <remarks>
    /// 通过此属性选择推送目标：
    /// <list type="bullet">
    /// <item><description><c>Clients.All</c> - 所有已认证客户端</description></item>
    /// <item><description><c>Clients.User(userId)</c> - 指定用户</description></item>
    /// <item><description><c>Clients.Group(groupName)</c> - 指定组</description></item>
    /// </list>
    /// </remarks>
    IHubClients<TReceiver> Clients { get; }

    /// <summary>
    /// 获取组管理器
    /// </summary>
    /// <remarks>
    /// 用于管理客户端分组，实现组播功能。
    /// </remarks>
    IGroupManager Groups { get; }
}

