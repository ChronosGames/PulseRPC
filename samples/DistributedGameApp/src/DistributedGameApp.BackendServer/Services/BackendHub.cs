using DistributedGameApp.Shared.Domain.Guilds;
using DistributedGameApp.Shared.Domain.Leaderboards;
using DistributedGameApp.Shared.Domain.Matchmaking;
using DistributedGameApp.Shared.Domain.Social;
using DistributedGameApp.Shared.Hubs;
using Microsoft.Extensions.Logging;
using PulseRPC;
using PulseRPC.Server;
using PulseRPC.Server.Abstractions;

namespace DistributedGameApp.BackendServer.Services;

/// <summary>
/// 后台服务 Hub 实现 - 基于 BaseService 架构
/// 整合社交、帮派、排行榜、匹配四大系统
/// </summary>
/// <remarks>
/// <para><strong>改进点</strong>:</para>
/// <list type="bullet">
/// <item><description>继承 BaseService，获得消息队列和线程安全保证</description></item>
/// <item><description>实现 IPulseService，支持全局单例</description></item>
/// <item><description>使用 GetCurrentCaller() 获取认证上下文，替代 AsyncLocal</description></item>
/// <item><description>获得表达式树编译优化（性能提升 50 倍）</description></item>
/// <item><description>获得监控指标和灾难隔离能力</description></item>
/// </list>
/// </remarks>
public class BackendHub : BaseService, IBackendHub, IPulseService
{
    private readonly SocialService _socialService;
    private readonly GuildService _guildService;
    private readonly LeaderboardService _leaderboardService;
    private readonly MatchmakingService _matchmakingService;

    // ServiceId 用于标识全局单例
    public string ServiceName => "BackendHub";
    public string ServiceId => "BackendHub:Global";

    public BackendHub(
        SocialService socialService,
        GuildService guildService,
        LeaderboardService leaderboardService,
        MatchmakingService matchmakingService,
        ILogger<BackendHub> logger,
        IAuthenticationService authenticationService,
        PermissionValidator permissionValidator)
        : base(logger, authenticationService, permissionValidator)
    {
        _socialService = socialService;
        _guildService = guildService;
        _leaderboardService = leaderboardService;
        _matchmakingService = matchmakingService;
    }

    /// <summary>
    /// 获取当前用户ID的辅助方法
    /// </summary>
    private string GetCurrentUserId()
    {
        var caller = GetCurrentCaller();
        return caller.UserId ?? caller.CallerId ?? throw new InvalidOperationException("无法获取用户ID");
    }

    #region 社交系统

    public async Task<bool> AddFriendAsync(string friendUserId)
    {
        var userId = GetCurrentUserId();
        return await _socialService.AddFriendAsync(userId, friendUserId);
    }

    public async Task<bool> AcceptFriendAsync(string friendUserId)
    {
        var userId = GetCurrentUserId();
        return await _socialService.AcceptFriendAsync(userId, friendUserId);
    }

    public async Task<bool> RejectFriendAsync(string friendUserId)
    {
        var userId = GetCurrentUserId();
        return await _socialService.RejectFriendAsync(userId, friendUserId);
    }

    public async Task<bool> RemoveFriendAsync(string friendUserId)
    {
        var userId = GetCurrentUserId();
        return await _socialService.RemoveFriendAsync(userId, friendUserId);
    }

    public async Task<Friend[]> GetFriendsAsync()
    {
        var userId = GetCurrentUserId();
        return await _socialService.GetFriendsAsync(userId);
    }

    public async Task<bool> SendPrivateMessageAsync(string receiverId, string content)
    {
        var userId = GetCurrentUserId();
        return await _socialService.SendPrivateMessageAsync(userId, receiverId, content);
    }

    public async Task<bool> SendWorldMessageAsync(string content)
    {
        var userId = GetCurrentUserId();
        return await _socialService.SendWorldMessageAsync(userId, content);
    }

    #endregion

    #region 公会系统（IGuildHub）

    // ========== 公会管理 ==========

    public async Task<CreateGuildResponse> CreateGuildAsync(CreateGuildRequest request)
    {
        var caller = GetCurrentCaller();
        var userId = caller.UserId ?? caller.CallerId ?? throw new InvalidOperationException("无法获取用户ID");
        // 从 Claims 中获取 username，如果没有则使用 userId
        var username = caller.Claims.TryGetValue("username", out var name) ? name : userId;
        return await _guildService.CreateGuildAsync(userId, username, request);
    }

    public async Task<bool> DisbandGuildAsync(string guildId)
    {
        var userId = GetCurrentUserId();
        return await _guildService.DisbandGuildAsync(userId, guildId);
    }

    public async Task<Guild?> GetGuildAsync(string guildId)
    {
        return await _guildService.GetGuildAsync(guildId);
    }

    public async Task<GuildListResponse> GetGuildsAsync(GetGuildListRequest request)
    {
        return await _guildService.GetGuildsAsync(request);
    }

    public async Task<Guild[]> SearchGuildsAsync(string keyword)
    {
        return await _guildService.SearchGuildsAsync(keyword);
    }

    public async Task<bool> UpdateGuildInfoAsync(UpdateGuildInfoRequest request)
    {
        var userId = GetCurrentUserId();
        return await _guildService.UpdateGuildInfoAsync(userId, request);
    }

    // ========== 成员管理 ==========

    public async Task<bool> ApplyToJoinAsync(string guildId)
    {
        var caller = GetCurrentCaller();
        var userId = caller.UserId ?? caller.CallerId ?? throw new InvalidOperationException("无法获取用户ID");
        // 从 Claims 中获取 username，如果没有则使用 userId
        var username = caller.Claims.TryGetValue("username", out var name) ? name : userId;
        // TODO: 从用户服务获取 level，这里先用默认值
        int level = 1;
        return await _guildService.ApplyToJoinAsync(userId, username, level, guildId);
    }

    public async Task<bool> ApproveJoinRequestAsync(string guildId, string userId)
    {
        var operatorId = GetCurrentUserId();
        return await _guildService.ApproveJoinRequestAsync(operatorId, guildId, userId);
    }

    public async Task<bool> RejectJoinRequestAsync(string guildId, string userId)
    {
        var operatorId = GetCurrentUserId();
        return await _guildService.RejectJoinRequestAsync(operatorId, guildId, userId);
    }

    public async Task<bool> LeaveGuildAsync()
    {
        var userId = GetCurrentUserId();
        return await _guildService.LeaveGuildAsync(userId);
    }

    public async Task<bool> KickMemberAsync(string guildId, string userId)
    {
        var operatorId = GetCurrentUserId();
        return await _guildService.KickMemberAsync(operatorId, guildId, userId);
    }

    public async Task<bool> PromoteMemberAsync(string guildId, string userId, string newRole)
    {
        var operatorId = GetCurrentUserId();
        return await _guildService.PromoteMemberAsync(operatorId, guildId, userId, newRole);
    }

    public async Task<bool> DemoteMemberAsync(string guildId, string userId)
    {
        var operatorId = GetCurrentUserId();
        return await _guildService.DemoteMemberAsync(operatorId, guildId, userId);
    }

    public async Task<bool> TransferLeadershipAsync(string guildId, string newLeaderId)
    {
        var currentLeaderId = GetCurrentUserId();
        return await _guildService.TransferLeadershipAsync(currentLeaderId, guildId, newLeaderId);
    }

    public async Task<GuildMember[]> GetMembersAsync(string guildId)
    {
        return await _guildService.GetMembersAsync(guildId);
    }

    public async Task<MyGuildInfo?> GetMyGuildAsync()
    {
        var userId = GetCurrentUserId();
        return await _guildService.GetMyGuildAsync(userId);
    }

    public async Task<JoinRequest[]> GetJoinRequestsAsync(string guildId)
    {
        var operatorId = GetCurrentUserId();
        return await _guildService.GetJoinRequestsAsync(operatorId, guildId);
    }

    // ========== 公会聊天 ==========

    public async Task<bool> SendMessageAsync(string content)
    {
        var caller = GetCurrentCaller();
        var userId = caller.UserId ?? caller.CallerId ?? throw new InvalidOperationException("无法获取用户ID");
        // 从 Claims 中获取 username，如果没有则使用 userId
        var username = caller.Claims.TryGetValue("username", out var name) ? name : userId;
        return await _guildService.SendMessageAsync(userId, username, content);
    }

    public async Task<GuildMessage[]> GetChatHistoryAsync(int limit = 50)
    {
        var userId = GetCurrentUserId();
        return await _guildService.GetChatHistoryAsync(userId, limit);
    }

    // ========== 公会贡献 ==========

    public async Task<bool> DonateAsync(DonateRequest request)
    {
        var userId = GetCurrentUserId();
        return await _guildService.DonateAsync(userId, request);
    }

    public async Task<GuildMember[]> GetContributionRankingAsync(string guildId)
    {
        return await _guildService.GetContributionRankingAsync(guildId);
    }

    // ========== 公会活动 ==========

    public async Task<bool> CheckInAsync()
    {
        var caller = GetCurrentCaller();
        var userId = caller.UserId ?? caller.CallerId ?? throw new InvalidOperationException("无法获取用户ID");
        // 从 Claims 中获取 username，如果没有则使用 userId
        var username = caller.Claims.TryGetValue("username", out var name) ? name : userId;
        return await _guildService.CheckInAsync(userId, username);
    }

    public async Task<GuildActivity[]> GetActivitiesAsync(string guildId)
    {
        return await _guildService.GetActivitiesAsync(guildId);
    }

    // ========== 公会公告 ==========

    public async Task<bool> PostAnnouncementAsync(string guildId, string content)
    {
        var operatorId = GetCurrentUserId();
        return await _guildService.PostAnnouncementAsync(operatorId, guildId, content);
    }

    public async Task<GuildAnnouncement[]> GetAnnouncementsAsync(string guildId)
    {
        var userId = GetCurrentUserId();
        return await _guildService.GetAnnouncementsAsync(userId, guildId);
    }

    #endregion

    #region 排行榜系统

    public async Task<LeaderboardEntry[]> GetLeaderboardAsync(GetLeaderboardRequest request)
    {
        return await _leaderboardService.GetLeaderboardAsync(request);
    }

    public async Task<LeaderboardEntry?> GetMyRankAsync(string leaderboardType, string seasonId = "")
    {
        var userId = GetCurrentUserId();
        return await _leaderboardService.GetMyRankAsync(userId, leaderboardType, seasonId);
    }

    #endregion

    #region 匹配系统

    public async Task<MatchmakingResponse> StartMatchmakingAsync(MatchmakingRequest request)
    {
        var userId = GetCurrentUserId();
        request.PlayerId = userId; // 设置玩家ID
        return await _matchmakingService.StartMatchmakingAsync(request);
    }

    public async Task<bool> CancelMatchmakingAsync()
    {
        var userId = GetCurrentUserId();
        return await _matchmakingService.CancelMatchmakingAsync(userId);
    }

    #endregion
}
