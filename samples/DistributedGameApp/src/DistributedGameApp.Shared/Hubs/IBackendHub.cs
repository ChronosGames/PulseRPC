using System.Collections.Generic;
using System.Threading.Tasks;
using DistributedGameApp.Shared.Domain.Guilds;
using DistributedGameApp.Shared.Domain.Leaderboards;
using DistributedGameApp.Shared.Domain.Matchmaking;
using DistributedGameApp.Shared.Domain.Social;
using PulseRPC;

namespace DistributedGameApp.Shared.Hubs;

/// <summary>
/// 后台服务器 Hub 接口（社交、帮派、排行榜、匹配）
/// </summary>
public interface IBackendHub : IPulseHub
{
    // ========== 社交系统 ==========

    /// <summary>
    /// 添加好友
    /// </summary>
    Task<bool> AddFriendAsync(string friendUserId);

    /// <summary>
    /// 接受好友请求
    /// </summary>
    Task<bool> AcceptFriendAsync(string friendUserId);

    /// <summary>
    /// 拒绝好友请求
    /// </summary>
    Task<bool> RejectFriendAsync(string friendUserId);

    /// <summary>
    /// 删除好友
    /// </summary>
    Task<bool> RemoveFriendAsync(string friendUserId);

    /// <summary>
    /// 获取好友列表
    /// </summary>
    Task<Friend[]> GetFriendsAsync();

    /// <summary>
    /// 发送私聊消息
    /// </summary>
    Task<bool> SendPrivateMessageAsync(string receiverId, string content);

    /// <summary>
    /// 发送世界频道消息
    /// </summary>
    Task<bool> SendWorldMessageAsync(string content);

    // ========== 帮派系统 ==========

    /// <summary>
    /// 创建帮派
    /// </summary>
    Task<Guild?> CreateGuildAsync(CreateGuildRequest request);

    /// <summary>
    /// 加入帮派
    /// </summary>
    Task<bool> JoinGuildAsync(string guildId);

    /// <summary>
    /// 离开帮派
    /// </summary>
    Task<bool> LeaveGuildAsync();

    /// <summary>
    /// 踢出成员（需要权限）
    /// </summary>
    Task<bool> KickGuildMemberAsync(string userId);

    /// <summary>
    /// 获取帮派信息
    /// </summary>
    Task<Guild?> GetGuildAsync(string guildId);

    /// <summary>
    /// 获取帮派成员列表
    /// </summary>
    Task<GuildMember[]> GetGuildMembersAsync(string guildId);

    /// <summary>
    /// 发送帮派消息
    /// </summary>
    Task<bool> SendGuildMessageAsync(string content);

    // ========== 排行榜系统 ==========

    /// <summary>
    /// 获取排行榜
    /// </summary>
    Task<LeaderboardEntry[]> GetLeaderboardAsync(GetLeaderboardRequest request);

    /// <summary>
    /// 获取我的排名
    /// </summary>
    /// <param name="leaderboardType">排行榜类型</param>
    /// <param name="seasonId">赛季ID（空字符串表示当前赛季）</param>
    Task<LeaderboardEntry?> GetMyRankAsync(string leaderboardType, string seasonId = "");

    // ========== 匹配系统 ==========

    /// <summary>
    /// 开始匹配
    /// </summary>
    Task<MatchmakingResponse> StartMatchmakingAsync(MatchmakingRequest request);

    /// <summary>
    /// 取消匹配
    /// </summary>
    Task<bool> CancelMatchmakingAsync();
}
