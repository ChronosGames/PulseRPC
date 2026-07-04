using DistributedGameApp.Shared.Messages;
using PulseRPC;
using System.Threading.Tasks;

namespace DistributedGameApp.Shared.Receivers;

/// <summary>
/// 游戏事件接收器 - 服务器向客户端推送游戏相关事件
/// </summary>
/// <remarks>
/// 这个接口定义了服务器可以向客户端推送的所有游戏事件。
/// 客户端需要实现这个接口来接收服务器推送的通知。
/// <para>
/// 标注 <see cref="ChannelAttribute"/>("CLIENT") 并继承 <see cref="IPulseHub"/> 表示这是客户端实现的接口
/// （统一 IPulseHub 架构，原 IPulseReceiver 已硬移除）。
/// 服务端使用 <c>IHubContext&lt;IGameReceiver&gt;</c> 推送消息。
/// </para>
/// </remarks>
[Channel("CLIENT")]
public interface IGameReceiver : IPulseHub
{
    /// <summary>
    /// 匹配成功通知
    /// </summary>
    /// <param name="notification">匹配成功信息</param>
    Task OnMatchFoundAsync(MatchFoundNotification notification);

    /// <summary>
    /// 匹配取消通知
    /// </summary>
    /// <param name="reason">取消原因</param>
    Task OnMatchCancelledAsync(string reason);

    /// <summary>
    /// 匹配进度更新
    /// </summary>
    /// <param name="estimatedWaitTime">预估等待时间（秒）</param>
    /// <param name="queuePosition">队列位置</param>
    Task OnMatchProgressAsync(int estimatedWaitTime, int queuePosition);

    /// <summary>
    /// 角色升级通知
    /// </summary>
    /// <param name="characterInfo">升级后的角色信息</param>
    Task OnCharacterLevelUpAsync(CharacterInfo characterInfo);

    /// <summary>
    /// 踢出通知（被强制下线）
    /// </summary>
    /// <param name="reason">踢出原因</param>
    Task OnKickedAsync(string reason);

    /// <summary>
    /// 系统公告
    /// </summary>
    /// <param name="announcement">公告内容</param>
    Task OnSystemAnnouncementAsync(string announcement);

    /// <summary>
    /// 好友上线通知
    /// </summary>
    /// <param name="friendId">好友ID</param>
    /// <param name="friendName">好友名称</param>
    Task OnFriendOnlineAsync(string friendId, string friendName);

    /// <summary>
    /// 好友下线通知
    /// </summary>
    /// <param name="friendId">好友ID</param>
    /// <param name="friendName">好友名称</param>
    Task OnFriendOfflineAsync(string friendId, string friendName);

    /// <summary>
    /// 服务器通知消息
    /// </summary>
    /// <param name="message">通知消息</param>
    Task OnServerNotificationAsync(string message);
}
