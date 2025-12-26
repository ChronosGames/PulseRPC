using DistributedGameApp.Shared.Domain.Guilds;
using DistributedGameApp.Shared.Domain.Leaderboards;
using DistributedGameApp.Shared.Domain.Matchmaking;
using DistributedGameApp.Shared.Domain.Social;
using DistributedGameApp.Shared.Hubs;
using DistributedGameApp.BackendServer.Services;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Abstractions;
using PulseRPC.Server.Hubs;
using PulseRPC.Server.ServiceManagement;

namespace DistributedGameApp.BackendServer.Hubs;

/// <summary>
/// 后台服务 Hub 实现 - 无状态 Hub
/// 整合社交、帮派、排行榜、匹配四大系统
/// </summary>
/// <remarks>
/// <para><strong>设计原则</strong>:</para>
/// <list type="bullet">
/// <item><description>✅ IPulseHub 保持无状态 - 只作为请求的入口点</description></item>
/// <item><description>✅ 所有业务逻辑委托给有状态的 Service 层(SocialService、GuildService等)</description></item>
/// <item><description>✅ 使用 IServiceAccessor 访问 ProcessSingleton 服务，确保线程安全</description></item>
/// <item><description>✅ 不维护任何成员状态,只依赖注入的服务访问器</description></item>
/// </list>
/// <para><strong>职责划分</strong>:</para>
/// <list type="bullet">
/// <item><description>Hub 层 - 无状态,负责接收请求、提取上下文、调用 Service</description></item>
/// <item><description>Service 层 - 有状态,负责业务逻辑、状态管理、数据持久化</description></item>
/// </list>
/// <para><strong>服务访问模式</strong>:</para>
/// <code>
/// // ProcessSingleton 服务通过 IServiceAccessor 访问，serviceId 固定为 "local"
/// await _socialService.ExecuteAsync("local", s => s.AddFriendAsync(userId, friendId));
/// </code>
/// </remarks>
public class BackendHub : PulseHubBase, IBackendHub
{
    private readonly IServiceAccessor<SocialService> _socialService;
    private readonly IServiceAccessor<GuildService> _guildService;
    private readonly IServiceAccessor<LeaderboardService> _leaderboardService;
    private readonly IServiceAccessor<MatchmakingService> _matchmakingService;
    private readonly ILogger<BackendHub> _logger;

    public BackendHub(
        IServiceAccessor<SocialService> socialService,
        IServiceAccessor<GuildService> guildService,
        IServiceAccessor<LeaderboardService> leaderboardService,
        IServiceAccessor<MatchmakingService> matchmakingService,
        ILogger<BackendHub> logger)
    {
        _socialService = socialService ?? throw new ArgumentNullException(nameof(socialService));
        _guildService = guildService ?? throw new ArgumentNullException(nameof(guildService));
        _leaderboardService = leaderboardService ?? throw new ArgumentNullException(nameof(leaderboardService));
        _matchmakingService = matchmakingService ?? throw new ArgumentNullException(nameof(matchmakingService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    #region 社交系统

    public async Task<bool> AddFriendAsync(string friendUserId)
    {
        return await _socialService.ExecuteWithUserId(
            (service, userId) => service.AddFriendAsync(userId, friendUserId));
    }

    public async Task<bool> AcceptFriendAsync(string friendUserId)
    {
        return await _socialService.ExecuteWithUserId(
            (service, userId) => service.AcceptFriendAsync(userId, friendUserId));
    }

    public async Task<bool> RejectFriendAsync(string friendUserId)
    {
        return await _socialService.ExecuteWithUserId(
            (service, userId) => service.RejectFriendAsync(userId, friendUserId));
    }

    public async Task<bool> RemoveFriendAsync(string friendUserId)
    {
        return await _socialService.ExecuteWithUserId(
            (service, userId) => service.RemoveFriendAsync(userId, friendUserId));
    }

    public async Task<Friend[]> GetFriendsAsync()
    {
        return await _socialService.ExecuteWithUserId(
            (service, userId) => service.GetFriendsAsync(userId));
    }

    public async Task<bool> SendPrivateMessageAsync(string receiverId, string content)
    {
        return await _socialService.ExecuteWithUserId(
            (service, userId) => service.SendPrivateMessageAsync(userId, receiverId, content));
    }

    public async Task<bool> SendWorldMessageAsync(string content)
    {
        return await _socialService.ExecuteWithUserId(
            (service, userId) => service.SendWorldMessageAsync(userId, content));
    }

    #endregion

    #region 公会系统（IGuildHub）

    // ========== 公会管理 ==========

    public async Task<CreateGuildResponse> CreateGuildAsync(CreateGuildRequest request)
    {
        var username = UserId; // 默认使用 userId

        return await _guildService.ExecuteWithUserId(
            (service, userId) => service.CreateGuildAsync(userId, username, request));
    }

    public async Task<bool> DisbandGuildAsync(string guildId)
    {
        return await _guildService.ExecuteWithUserId(
            (service, userId) => service.DisbandGuildAsync(userId, guildId));
    }

    public async Task<Guild?> GetGuildAsync(string guildId)
    {
        return await _guildService.Execute(
            service => service.GetGuildAsync(guildId));
    }

    public async Task<GuildListResponse> GetGuildsAsync(GetGuildListRequest request)
    {
        return await _guildService.Execute(
            service => service.GetGuildsAsync(request));
    }

    public async Task<Guild[]> SearchGuildsAsync(string keyword)
    {
        return await _guildService.Execute(
            service => service.SearchGuildsAsync(keyword));
    }

    public async Task<bool> UpdateGuildInfoAsync(UpdateGuildInfoRequest request)
    {
        return await _guildService.ExecuteWithUserId(
            (service, userId) => service.UpdateGuildInfoAsync(userId, request));
    }

    // ========== 成员管理 ==========

    public async Task<bool> ApplyToJoinAsync(string guildId)
    {
        var username = UserId; // 默认使用 userId
        int level = 1; // TODO: 从用户服务获取

        return await _guildService.ExecuteWithUserId(
            (service, userId) => service.ApplyToJoinAsync(userId, username, level, guildId));
    }

    public async Task<bool> ApproveJoinRequestAsync(string guildId, string userId)
    {
        return await _guildService.ExecuteWithUserId(
            (service, operatorId) => service.ApproveJoinRequestAsync(operatorId, guildId, userId));
    }

    public async Task<bool> RejectJoinRequestAsync(string guildId, string userId)
    {
        return await _guildService.ExecuteWithUserId(
            (service, operatorId) => service.RejectJoinRequestAsync(operatorId, guildId, userId));
    }

    public async Task<bool> LeaveGuildAsync()
    {
        return await _guildService.ExecuteWithUserId(
            (service, userId) => service.LeaveGuildAsync(userId));
    }

    public async Task<bool> KickMemberAsync(string guildId, string userId)
    {
        return await _guildService.ExecuteWithUserId(
            (service, operatorId) => service.KickMemberAsync(operatorId, guildId, userId));
    }

    public async Task<bool> PromoteMemberAsync(string guildId, string userId, string newRole)
    {
        return await _guildService.ExecuteWithUserId(
            (service, operatorId) => service.PromoteMemberAsync(operatorId, guildId, userId, newRole));
    }

    public async Task<bool> DemoteMemberAsync(string guildId, string userId)
    {
        return await _guildService.ExecuteWithUserId(
            (service, operatorId) => service.DemoteMemberAsync(operatorId, guildId, userId));
    }

    public async Task<bool> TransferLeadershipAsync(string guildId, string newLeaderId)
    {
        return await _guildService.ExecuteWithUserId(
            (service, currentLeaderId) => service.TransferLeadershipAsync(currentLeaderId, guildId, newLeaderId));
    }

    public async Task<GuildMember[]> GetMembersAsync(string guildId)
    {
        return await _guildService.Execute(
            service => service.GetMembersAsync(guildId));
    }

    public async Task<MyGuildInfo?> GetMyGuildAsync()
    {
        return await _guildService.ExecuteWithUserId(
            (service, userId) => service.GetMyGuildAsync(userId));
    }

    public async Task<JoinRequest[]> GetJoinRequestsAsync(string guildId)
    {
        return await _guildService.ExecuteWithUserId(
            (service, operatorId) => service.GetJoinRequestsAsync(operatorId, guildId));
    }

    // ========== 公会聊天 ==========

    public async Task<bool> SendMessageAsync(string content)
    {
        var username = UserId; // 默认使用 userId

        return await _guildService.ExecuteWithUserId(
            (service, userId) => service.SendMessageAsync(userId, username, content));
    }

    public async Task<GuildMessage[]> GetChatHistoryAsync(int limit = 50)
    {
        return await _guildService.ExecuteWithUserId(
            (service, userId) => service.GetChatHistoryAsync(userId, limit));
    }

    // ========== 公会贡献 ==========

    public async Task<bool> DonateAsync(DonateRequest request)
    {
        return await _guildService.ExecuteWithUserId(
            (service, userId) => service.DonateAsync(userId, request));
    }

    public async Task<GuildMember[]> GetContributionRankingAsync(string guildId)
    {
        return await _guildService.Execute(
            service => service.GetContributionRankingAsync(guildId));
    }

    // ========== 公会活动 ==========

    public async Task<bool> CheckInAsync()
    {
        var username = UserId; // 默认使用 userId

        return await _guildService.ExecuteWithUserId(
            (service, userId) => service.CheckInAsync(userId, username));
    }

    public async Task<GuildActivity[]> GetActivitiesAsync(string guildId)
    {
        return await _guildService.Execute(
            service => service.GetActivitiesAsync(guildId));
    }

    // ========== 公会公告 ==========

    public async Task<bool> PostAnnouncementAsync(string guildId, string content)
    {
        return await _guildService.ExecuteWithUserId(
            (service, operatorId) => service.PostAnnouncementAsync(operatorId, guildId, content));
    }

    public async Task<GuildAnnouncement[]> GetAnnouncementsAsync(string guildId)
    {
        return await _guildService.ExecuteWithUserId(
            (service, userId) => service.GetAnnouncementsAsync(userId, guildId));
    }

    #endregion

    #region 排行榜系统

    public async Task<LeaderboardEntry[]> GetLeaderboardAsync(GetLeaderboardRequest request)
    {
        return await _leaderboardService.Execute(
            service => service.GetLeaderboardAsync(request));
    }

    public async Task<LeaderboardEntry?> GetMyRankAsync(string leaderboardType, string seasonId = "")
    {
        return await _leaderboardService.ExecuteWithUserId(
            (service, userId) => service.GetMyRankAsync(userId, leaderboardType, seasonId));
    }

    #endregion

    #region 匹配系统

    public async Task<MatchmakingResponse> StartMatchmakingAsync(MatchmakingRequest request)
    {
        return await _matchmakingService.Execute(
            service => service.StartMatchmakingAsync(request));
    }

    public async Task<bool> CancelMatchmakingAsync(string userId)
    {
        return await _matchmakingService.Execute(
            service => service.CancelMatchmakingAsync(userId));
    }

    #endregion
}
