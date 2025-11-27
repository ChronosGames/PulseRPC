namespace PulseRPC;

/// <summary>
/// PulseRPC 服务端推送接收器标记接口
/// </summary>
/// <remarks>
/// <para>
/// 用于标记服务端向客户端推送消息的契约接口。
/// 与 <see cref="IPulseHub"/> 不同，此接口用于定义单向的服务端推送。
/// </para>
/// <para>
/// <strong>设计理念</strong>：
/// </para>
/// <list type="bullet">
/// <item><description><see cref="IPulseHub"/> - 客户端调用服务端（请求/响应模式）</description></item>
/// <item><description><see cref="IPulseReceiver"/> - 服务端推送给客户端（事件推送模式）</description></item>
/// </list>
/// <para>
/// <strong>使用示例</strong>：
/// </para>
/// <code>
/// // 定义推送契约
/// public interface IGameReceiver : IPulseReceiver
/// {
///     Task OnMatchFoundAsync(MatchFoundNotification notification);
///     Task OnPlayerJoinedAsync(PlayerInfo player);
/// }
///
/// // 服务端推送
/// await _hubContext.Clients.User("player-123").OnMatchFoundAsync(notification);
///
/// // 客户端注册接收器
/// channel.RegisterReceiver(new MyGameReceiver());
/// </code>
/// </remarks>
public interface IPulseReceiver
{
    // 所有服务端推送接收器都应继承此接口
}

