using DistributedGameApp.GameServer.Services.Backend;
using DistributedGameApp.GameServer.Services.Generic;
using DistributedGameApp.Infrastructure.ServiceClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DistributedGameApp.GameServer.Services;

/// <summary>
/// 统一服务客户端初始化后台服务
/// </summary>
public class UnifiedServiceClientInitializationService : BackgroundService
{
    private readonly UnifiedServiceClientManager _serviceManager;
    private readonly ILogger<UnifiedServiceClientInitializationService> _logger;

    public UnifiedServiceClientInitializationService(
        UnifiedServiceClientManager serviceManager,
        ILogger<UnifiedServiceClientInitializationService> logger)
    {
        _serviceManager = serviceManager;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // 等待一段时间确保服务器已启动
            await Task.Delay(2000, stoppingToken);

            _logger.LogInformation("正在初始化统一服务客户端...");

            // 注册需要的服务类型
            // 可以根据配置文件动态决定注册哪些服务类型
            await _serviceManager.InitializeAsync(
                new[]
                {
                    ServerType.Backend,  // 匹配、排行榜等后端服务
                    // ServerType.Chat,     // 聊天服务（如果需要）
                    // ServerType.Battle,   // 战斗服务（如果需要）
                },
                RoutingStrategy.ConsistentHash,  // 使用一致性哈希策略
                stoppingToken
            );

            // 输出统计信息
            var stats = _serviceManager.GetStats();
            _logger.LogInformation(
                "统一服务客户端初始化完成 - 注册服务类型: {Types}",
                string.Join(", ", stats.RegisteredServerTypes)
            );

            foreach (var (serverType, typeStats) in stats.ServerStats)
            {
                _logger.LogInformation(
                    "{ServerType}: 连接数={Connected}/{Total}, 请求数={Requests}, 策略={Strategy}",
                    serverType,
                    typeStats.ConnectedCount,
                    typeStats.TotalConnections,
                    typeStats.TotalRequests,
                    typeStats.RoutingStrategy
                );
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "统一服务客户端初始化失败");
        }

        // 保持运行
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
