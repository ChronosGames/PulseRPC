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

    #region 帮派系统

    public async Task<Guild?> CreateGuildAsync(CreateGuildRequest request)
    {
        var userId = GetCurrentUserId();
        return await _guildService.CreateGuildAsync(userId, request);
    }

    public async Task<bool> JoinGuildAsync(string guildId)
    {
        var userId = GetCurrentUserId();
        return await _guildService.JoinGuildAsync(userId, guildId);
    }

    public async Task<bool> LeaveGuildAsync()
    {
        var userId = GetCurrentUserId();
        return await _guildService.LeaveGuildAsync(userId);
    }

    public async Task<bool> KickGuildMemberAsync(string targetUserId)
    {
        var userId = GetCurrentUserId();
        return await _guildService.KickMemberAsync(userId, targetUserId);
    }

    public async Task<Guild?> GetGuildAsync(string guildId)
    {
        return await _guildService.GetGuildAsync(guildId);
    }

    public async Task<GuildMember[]> GetGuildMembersAsync(string guildId)
    {
        return await _guildService.GetGuildMembersAsync(guildId);
    }

    public async Task<bool> SendGuildMessageAsync(string content)
    {
        var userId = GetCurrentUserId();
        return await _guildService.SendGuildMessageAsync(userId, content);
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
