using System.Threading.Tasks;
using DistributedGameApp.Shared.Domain.Guilds;
using PulseRPC;

namespace DistributedGameApp.Shared.Hubs;

/// <summary>
/// 公会服务器 Hub 接口
/// </summary>
public interface IGuildHub : IPulseHub
{
    // ========== 公会管理 ==========

    /// <summary>
    /// 创建公会
    /// </summary>
    Task<CreateGuildResponse> CreateGuildAsync(CreateGuildRequest request);

    /// <summary>
    /// 解散公会（仅会长）
    /// </summary>
    Task<bool> DisbandGuildAsync(string guildId);

    /// <summary>
    /// 获取公会信息
    /// </summary>
    Task<Guild?> GetGuildAsync(string guildId);

    /// <summary>
    /// 获取公会列表（分页）
    /// </summary>
    Task<GuildListResponse> GetGuildsAsync(GetGuildListRequest request);

    /// <summary>
    /// 搜索公会
    /// </summary>
    Task<Guild[]> SearchGuildsAsync(string keyword);

    /// <summary>
    /// 更新公会信息（仅会长）
    /// </summary>
    Task<bool> UpdateGuildInfoAsync(UpdateGuildInfoRequest request);

    // ========== 成员管理 ==========

    /// <summary>
    /// 申请加入公会
    /// </summary>
    Task<bool> ApplyToJoinAsync(string guildId);

    /// <summary>
    /// 批准加入申请（会长/官员）
    /// </summary>
    Task<bool> ApproveJoinRequestAsync(string guildId, string userId);

    /// <summary>
    /// 拒绝加入申请（会长/官员）
    /// </summary>
    Task<bool> RejectJoinRequestAsync(string guildId, string userId);

    /// <summary>
    /// 离开公会
    /// </summary>
    Task<bool> LeaveGuildAsync();

    /// <summary>
    /// 踢出成员（会长/官员）
    /// </summary>
    Task<bool> KickMemberAsync(string guildId, string userId);

    /// <summary>
    /// 提升成员职位（会长）
    /// </summary>
    Task<bool> PromoteMemberAsync(string guildId, string userId, string newRole);

    /// <summary>
    /// 降低成员职位（会长）
    /// </summary>
    Task<bool> DemoteMemberAsync(string guildId, string userId);

    /// <summary>
    /// 转让会长（仅会长）
    /// </summary>
    Task<bool> TransferLeadershipAsync(string guildId, string newLeaderId);

    /// <summary>
    /// 获取公会成员列表
    /// </summary>
    Task<GuildMember[]> GetMembersAsync(string guildId);

    /// <summary>
    /// 获取我的公会信息
    /// </summary>
    Task<MyGuildInfo?> GetMyGuildAsync();

    /// <summary>
    /// 获取加入申请列表（会长/官员）
    /// </summary>
    Task<JoinRequest[]> GetJoinRequestsAsync(string guildId);

    // ========== 公会聊天 ==========

    /// <summary>
    /// 发送公会消息
    /// </summary>
    Task<bool> SendMessageAsync(string content);

    /// <summary>
    /// 获取公会聊天历史
    /// </summary>
    Task<GuildMessage[]> GetChatHistoryAsync(int limit = 50);

    // ========== 公会贡献 ==========

    /// <summary>
    /// 捐献资源
    /// </summary>
    Task<bool> DonateAsync(DonateRequest request);

    /// <summary>
    /// 获取贡献排行榜
    /// </summary>
    Task<GuildMember[]> GetContributionRankingAsync(string guildId);

    // ========== 公会活动 ==========

    /// <summary>
    /// 签到
    /// </summary>
    Task<bool> CheckInAsync();

    /// <summary>
    /// 获取公会活动列表
    /// </summary>
    Task<GuildActivity[]> GetActivitiesAsync(string guildId);

    // ========== 公会公告 ==========

    /// <summary>
    /// 发布公告（会长/官员）
    /// </summary>
    Task<bool> PostAnnouncementAsync(string guildId, string content);

    /// <summary>
    /// 获取公告列表
    /// </summary>
    Task<GuildAnnouncement[]> GetAnnouncementsAsync(string guildId);
}
