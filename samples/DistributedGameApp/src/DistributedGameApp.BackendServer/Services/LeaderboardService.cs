using DistributedGameApp.Shared.Domain.Leaderboards;
using Microsoft.Extensions.Logging;
using PulseRPC.Server;
using PulseRPC.Server.Abstractions;
using PulseRPC.Server.Configuration;

namespace DistributedGameApp.BackendServer.Services;

/// <summary>
/// 排行榜服务 - 处理各种排行榜数据
/// </summary>
/// <remarks>
/// <para><strong>改进点</strong>:</para>
/// <list type="bullet">
/// <item><description>继承 ConcurrentServiceBase，支持并发处理（只读服务）</description></item>
/// <item><description>配置高并发度，优化查询性能</description></item>
/// <item><description>获得监控指标和性能优化</description></item>
/// </list>
/// </remarks>
public class LeaderboardService : ConcurrentServiceBase
{
    public LeaderboardService(
        ILogger<LeaderboardService> logger,
        IAuthenticationService authenticationService,
        PermissionValidator permissionValidator)
        : base(logger, authenticationService, permissionValidator, new ConcurrentServiceOptions
        {
            MaxConcurrency = 20, // 排行榜只读服务，可以高并发
            QueueCapacity = 2000,
            BackpressureStrategy = BackpressureStrategy.DropOldest
        })
    {
    }

    public Task<LeaderboardEntry[]> GetLeaderboardAsync(GetLeaderboardRequest request)
    {
        Logger.LogInformation("获取排行榜: {Type}", request.LeaderboardType);
        // TODO: 实现实际的数据库查询
        return Task.FromResult(Array.Empty<LeaderboardEntry>());
    }

    public Task<LeaderboardEntry?> GetMyRankAsync(string userId, string leaderboardType, string seasonId = "")
    {
        Logger.LogInformation("查询排名: {UserId} - {Type} - Season: {SeasonId}", userId, leaderboardType, string.IsNullOrEmpty(seasonId) ? "current" : seasonId);
        // TODO: 实现实际的数据库查询
        return Task.FromResult<LeaderboardEntry?>(null);
    }
}
