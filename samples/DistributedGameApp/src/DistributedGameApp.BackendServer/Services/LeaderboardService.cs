using DistributedGameApp.Shared.Domain.Leaderboards;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Services;

namespace DistributedGameApp.BackendServer.Services;

/// <summary>
/// 排行榜服务 - 处理各种排行榜数据
/// </summary>
/// <remarks>
/// <para><strong>设计模式</strong>:</para>
/// <list type="bullet">
/// <item><description>继承 UnifiedPulseServiceBase，获得生命周期管理和消息队列支持</description></item>
/// <item><description>全局单例，自动启动</description></item>
/// <item><description>可配置并发度（只读服务）</description></item>
/// </list>
/// </remarks>
[PulseService(
    Scenario = ServiceScenario.Actor,  // 单线程顺序执行，保证线程安全
    StartupType = ServiceStartupType.AutoStart,
    InstanceScope = ServiceInstanceScope.Singleton,
    DisplayName = "LeaderboardService",
    EnableHealthCheck = true)]
public class LeaderboardService : UnifiedPulseServiceBase
{
    public LeaderboardService(ILogger<LeaderboardService> logger)
        : base("LeaderboardService", "Global", logger)
    {
    }

    public override Task OnStartingAsync(CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("LeaderboardService starting...");
        // TODO: 加载排行榜数据到缓存
        return Task.CompletedTask;
    }

    public override Task OnStoppingAsync(CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("LeaderboardService stopping...");
        // TODO: 保存排行榜数据
        return Task.CompletedTask;
    }

    public Task<LeaderboardEntry[]> GetLeaderboardAsync(GetLeaderboardRequest request)
    {
        Logger.LogInformation("获取排行榜: {Type}", request.LeaderboardType);
        // TODO: 实现实际的数据库查询
        return Task.FromResult(Array.Empty<LeaderboardEntry>());
    }

    public Task<LeaderboardEntry?> GetMyRankAsync(string userId, string leaderboardType, string seasonId = "")
    {
        Logger.LogInformation("查询排名: {UserId} - {Type} - Season: {SeasonId}",
            userId, leaderboardType, string.IsNullOrEmpty(seasonId) ? "current" : seasonId);
        // TODO: 实现实际的数据库查询
        return Task.FromResult<LeaderboardEntry?>(null);
    }
}
