using DistributedGameApp.Shared.Domain.Matchmaking;
using DistributedGameApp.Shared.Receivers;
using Microsoft.Extensions.Logging;
using PulseRPC.Server;

namespace DistributedGameApp.BackendServer.Services;

/// <summary>
/// 现代化客户端通知服务 - 使用 IHubContext&lt;TReceiver&gt; API
/// </summary>
/// <remarks>
/// <para>
/// 参考 <see href="https://cysharp.github.io/MagicOnion/streaminghub/call-client">MagicOnion</see> 设计风格。
/// </para>
/// <para>
/// <strong>API 示例</strong>：
/// </para>
/// <code>
/// // 向所有客户端发送
/// await Clients.All.OnMatchFoundAsync(notification);
///
/// // 向单个连接发送
/// await Clients.Single(connectionId).OnMatchFoundAsync(notification);
///
/// // 向多个连接发送
/// await Clients.Only(connectionIds).OnMatchFoundAsync(notification);
///
/// // 向除指定连接外的所有人发送
/// await Clients.Except(myConnectionId).OnPlayerMoved(position);
///
/// // 向用户发送（可能有多个连接）
/// await Clients.User(userId).OnMatchFoundAsync(notification);
///
/// // 向多个用户发送
/// await Clients.Users(userIds).OnMatchFoundAsync(notification);
///
/// // 向组发送
/// await Clients.Group("room-123").OnPlayerJoinedAsync(player);
/// </code>
/// </remarks>
public class ModernClientNotificationService
{
    private readonly IHubContext<IBackendReceiver> _backendReceiver;
    private readonly ILogger<ModernClientNotificationService> _logger;

    public ModernClientNotificationService(
        IHubContext<IBackendReceiver> backendReceiver,
        ILogger<ModernClientNotificationService> logger)
    {
        _backendReceiver = backendReceiver;
        _logger = logger;
    }

    /// <summary>
    /// 通知匹配成功
    /// </summary>
    /// <param name="playerIds">参与匹配的玩家ID列表</param>
    /// <param name="notification">匹配成功通知</param>
    public async Task NotifyMatchFoundAsync(
        IReadOnlyList<string> playerIds,
        MatchFoundNotification notification)
    {
        _logger.LogInformation("发送匹配成功通知给 {Count} 名玩家", playerIds.Count);

        // 使用强类型调用 - 编译时检查，无需手动指定协议号
        await _backendReceiver.Clients.Users(playerIds).OnMatchFoundAsync(notification);

        _logger.LogDebug("匹配成功通知已发送");
    }

    /// <summary>
    /// 通知匹配取消
    /// </summary>
    /// <param name="playerId">玩家ID</param>
    /// <param name="reason">取消原因</param>
    public async Task NotifyMatchCanceledAsync(string playerId, string reason)
    {
        _logger.LogInformation("发送匹配取消通知给玩家 {PlayerId}: {Reason}", playerId, reason);

        await _backendReceiver.Clients.User(playerId).OnMatchCanceledAsync(reason);
    }

    /// <summary>
    /// 发送好友上线通知
    /// </summary>
    /// <param name="userId">要通知的用户ID</param>
    /// <param name="friendUserId">上线的好友ID</param>
    /// <param name="friendUsername">上线的好友用户名</param>
    public async Task NotifyFriendOnlineAsync(
        string userId,
        string friendUserId,
        string friendUsername)
    {
        await _backendReceiver.Clients.User(userId)
            .OnFriendOnlineAsync(friendUserId, friendUsername);
    }

    /// <summary>
    /// 发送好友下线通知
    /// </summary>
    /// <param name="userId">要通知的用户ID</param>
    /// <param name="friendUserId">下线的好友ID</param>
    public async Task NotifyFriendOfflineAsync(string userId, string friendUserId)
    {
        await _backendReceiver.Clients.User(userId)
            .OnFriendOfflineAsync(friendUserId);
    }

    /// <summary>
    /// 广播系统公告（发送给所有在线用户）
    /// </summary>
    /// <param name="announcement">公告内容</param>
    public async Task BroadcastSystemAnnouncementAsync(string announcement)
    {
        _logger.LogInformation("广播系统公告: {Announcement}", announcement);

        // 使用 All 选择器广播给所有已认证客户端
        // 注意：IBackendReceiver 没有 OnSystemAnnouncementAsync 方法
        // 这里演示如何使用 IGameReceiver
        // await _gameReceiver.Clients.All.OnSystemAnnouncementAsync(announcement);
    }

    /// <summary>
    /// 向指定帮派发送消息
    /// </summary>
    /// <param name="guildId">帮派ID（作为组名）</param>
    /// <param name="message">聊天消息</param>
    public async Task NotifyGuildMessageAsync(
        string guildId,
        DistributedGameApp.Shared.Domain.Social.ChatMessage message)
    {
        _logger.LogDebug("发送帮派消息到帮派 {GuildId}", guildId);

        // 使用 Group 选择器向帮派组发送消息
        await _backendReceiver.Clients.Group($"guild:{guildId}")
            .OnGuildMessageReceivedAsync(message);
    }

    /// <summary>
    /// 向帮派成员发送成员加入通知（排除新成员自己）
    /// </summary>
    /// <param name="guildId">帮派ID</param>
    /// <param name="newMember">新成员信息</param>
    /// <param name="newMemberConnectionId">新成员的连接ID（要排除）</param>
    public async Task NotifyGuildMemberJoinedAsync(
        string guildId,
        DistributedGameApp.Shared.Domain.Guilds.GuildMember newMember,
        string newMemberConnectionId)
    {
        // 使用 GroupExcept 选择器，排除新成员自己
        await _backendReceiver.Clients
            .GroupExcept($"guild:{guildId}", newMemberConnectionId)
            .OnGuildMemberJoinedAsync(newMember);
    }
}

