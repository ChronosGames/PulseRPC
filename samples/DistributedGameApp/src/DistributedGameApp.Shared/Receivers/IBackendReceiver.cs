using System.Threading.Tasks;
using DistributedGameApp.Shared.Domain.Guilds;
using DistributedGameApp.Shared.Domain.Matchmaking;
using DistributedGameApp.Shared.Domain.Social;
using PulseRPC;

namespace DistributedGameApp.Shared.Receivers;

/// <summary>
/// 后台服务器推送事件接口
/// </summary>
/// <remarks>
/// 标注 <see cref="ChannelAttribute"/>("CLIENT") 并继承 <see cref="IPulseHub"/> 表示这是客户端实现的接口
/// （服务器向客户端推送消息；统一 IPulseHub 架构，原 IPulseReceiver 已硬移除）。
/// 服务端使用 <c>IHubContext&lt;IBackendReceiver&gt;</c> 推送消息。
/// </remarks>
[Channel("CLIENT")]
public interface IBackendReceiver : IPulseHub
{
    // ========== 社交事件 ==========

    /// <summary>
    /// 收到好友请求
    /// </summary>
    Task OnFriendRequestReceivedAsync(Friend friend);

    /// <summary>
    /// 好友请求被接受
    /// </summary>
    Task OnFriendRequestAcceptedAsync(Friend friend);

    /// <summary>
    /// 收到私聊消息
    /// </summary>
    Task OnPrivateMessageReceivedAsync(ChatMessage message);

    /// <summary>
    /// 收到世界频道消息
    /// </summary>
    Task OnWorldMessageReceivedAsync(ChatMessage message);

    /// <summary>
    /// 好友上线通知
    /// </summary>
    Task OnFriendOnlineAsync(string friendUserId, string friendUsername);

    /// <summary>
    /// 好友下线通知
    /// </summary>
    Task OnFriendOfflineAsync(string friendUserId);

    // ========== 帮派事件 ==========

    /// <summary>
    /// 收到帮派邀请
    /// </summary>
    Task OnGuildInviteReceivedAsync(Guild guild);

    /// <summary>
    /// 加入帮派成功
    /// </summary>
    Task OnJoinedGuildAsync(Guild guild);

    /// <summary>
    /// 离开帮派
    /// </summary>
    Task OnLeftGuildAsync(string guildId);

    /// <summary>
    /// 帮派成员加入通知
    /// </summary>
    Task OnGuildMemberJoinedAsync(GuildMember member);

    /// <summary>
    /// 帮派成员离开通知
    /// </summary>
    Task OnGuildMemberLeftAsync(string userId);

    /// <summary>
    /// 收到帮派消息
    /// </summary>
    Task OnGuildMessageReceivedAsync(ChatMessage message);

    /// <summary>
    /// 帮派升级通知
    /// </summary>
    Task OnGuildLevelUpAsync(Guild guild);

    // ========== 匹配事件 ==========

    /// <summary>
    /// 匹配成功通知
    /// </summary>
    Task OnMatchFoundAsync(MatchFoundNotification notification);

    /// <summary>
    /// 匹配取消通知
    /// </summary>
    Task OnMatchCanceledAsync(string reason);
}
