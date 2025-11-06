using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PulseRPC.Server.Routing.Examples;

/// <summary>
/// 集群扩缩容示例
/// 演示如何使用零迁移策略进行节点扩缩容
/// </summary>
public class ClusterScalingExample
{
    private readonly IServiceRouter _router;
    private readonly NodeChangeHandler _nodeChangeHandler;
    private readonly ServiceLifecycleManager _lifecycle;
    private readonly ILogger<ClusterScalingExample> _logger;

    public ClusterScalingExample(
        IServiceRouter router,
        NodeChangeHandler nodeChangeHandler,
        ServiceLifecycleManager lifecycle,
        ILogger<ClusterScalingExample> logger)
    {
        _router = router;
        _nodeChangeHandler = nodeChangeHandler;
        _lifecycle = lifecycle;
        _logger = logger;
    }

    /// <summary>
    /// 示例1：节点扩容
    /// 场景：晚高峰流量激增，需要增加2个游戏服务器节点
    /// </summary>
    public async Task Example1_ScaleOut()
    {
        _logger.LogInformation("=== 示例1：节点扩容 ===");

        // 当前集群状态
        _logger.LogInformation("扩容前 - 活跃节点: {Nodes}",
            string.Join(",", _router.GetActiveNodes()));

        // 模拟当前有1000个活跃的玩家Service
        var activePlayerCount = 1000;
        _logger.LogInformation("当前活跃玩家数: {Count}", activePlayerCount);

        // 执行扩容：新增节点10和11
        var newNodes = new List<ushort> { 10, 11 };
        await _nodeChangeHandler.OnNodesAddedAsync(
            newNodes,
            "晚高峰流量激增，扩容2个节点以提高承载能力");

        _logger.LogInformation("扩容后 - 活跃节点: {Nodes}",
            string.Join(",", _router.GetActiveNodes()));

        // 新玩家登录
        _logger.LogInformation("新玩家登录测试...");
        for (int i = 0; i < 10; i++)
        {
            var playerId = $"new_player_{i}";
            var hash = NodeConsistentHashRing.ComputeHash(playerId);
            var nodeId = await _lifecycle.OnServiceCreatedAsync(hash, "新玩家登录");

            _logger.LogInformation("新玩家 {PlayerId} 分配到节点 {NodeId}", playerId, nodeId);
        }

        // 查看路由指标
        PrintMetrics();

        _logger.LogInformation("=== 扩容完成 ===\n");
    }

    /// <summary>
    /// 示例2：节点缩容
    /// 场景：凌晨低峰期，下线部分节点进行维护
    /// </summary>
    public async Task Example2_ScaleIn()
    {
        _logger.LogInformation("=== 示例2：节点缩容 ===");

        // 当前集群状态
        _logger.LogInformation("缩容前 - 活跃节点: {Nodes}",
            string.Join(",", _router.GetActiveNodes()));

        // 执行缩容：下线节点5
        var removedNodes = new List<ushort> { 5 };
        await _nodeChangeHandler.OnNodesRemovedAsync(
            removedNodes,
            "凌晨低峰期，节点5下线进行硬件维护");

        _logger.LogInformation("缩容后 - 活跃节点: {Nodes}",
            string.Join(",", _router.GetActiveNodes()));

        _logger.LogInformation("注意：节点5上的Service将自然下线，不会迁移");

        // 查看路由指标
        PrintMetrics();

        _logger.LogInformation("=== 缩容完成 ===\n");
    }

    /// <summary>
    /// 示例3：Service生命周期管理
    /// 演示Service创建、定位、下线的完整流程
    /// </summary>
    public async Task Example3_ServiceLifecycle()
    {
        _logger.LogInformation("=== 示例3：Service生命周期管理 ===");

        var playerId = "player_12345";
        var hash = NodeConsistentHashRing.ComputeHash(playerId);

        // 1. 玩家登录 - 创建Service
        _logger.LogInformation("1. 玩家 {PlayerId} 登录...", playerId);
        var nodeId = await _lifecycle.OnServiceCreatedAsync(hash, "玩家登录");
        _logger.LogInformation("   Service创建在节点 {NodeId}", nodeId);

        // 2. 游戏中 - 定位Service
        await Task.Delay(1000);  // 模拟游戏时间
        _logger.LogInformation("2. 游戏中 - 定位玩家Service...");
        var locatedNodeId = await _router.LocateServiceAsync(hash);
        _logger.LogInformation("   Service定位到节点 {NodeId}", locatedNodeId);

        // 3. 玩家下线 - 清理Service
        await Task.Delay(1000);
        _logger.LogInformation("3. 玩家 {PlayerId} 下线...", playerId);
        await _lifecycle.OnServiceShutdownAsync(hash, ShutdownReason.PlayerLogout);
        _logger.LogInformation("   Service已清理，固定映射已删除");

        // 4. 玩家重新登录 - 使用新哈希环
        await Task.Delay(1000);
        _logger.LogInformation("4. 玩家 {PlayerId} 重新登录...", playerId);
        var newNodeId = await _lifecycle.OnServiceCreatedAsync(hash, "玩家重新登录");
        _logger.LogInformation("   Service创建在节点 {NodeId} (可能使用新哈希环)", newNodeId);

        _logger.LogInformation("=== 生命周期演示完成 ===\n");
    }

    /// <summary>
    /// 示例4：监控和指标
    /// 演示如何获取和监控集群路由指标
    /// </summary>
    public void Example4_Monitoring()
    {
        _logger.LogInformation("=== 示例4：监控和指标 ===");

        PrintMetrics();

        // 模拟定期监控
        _logger.LogInformation("建议在Prometheus/Grafana中配置以下指标:");
        _logger.LogInformation("- consistent_hash_route_count (一致性哈希路由次数)");
        _logger.LogInformation("- fixed_mapping_route_count (固定映射路由次数)");
        _logger.LogInformation("- active_node_count (活跃节点数)");
        _logger.LogInformation("- fixed_mapping_expiring_1h (1小时内过期的映射数)");

        _logger.LogInformation("=== 监控示例完成 ===\n");
    }

    /// <summary>
    /// 示例5：自然过渡过程
    /// 演示24小时内从老哈希环到新哈希环的自然过渡
    /// </summary>
    public async Task Example5_NaturalTransition()
    {
        _logger.LogInformation("=== 示例5：自然过渡过程 ===");

        // 时刻 T0: 扩容前
        _logger.LogInformation("T0: 节点1,2,3,4,5运行中，1000个活跃Service");
        await Task.Delay(500);

        // 时刻 T1: 扩容
        _logger.LogInformation("T1: 新增节点10,11（扩容）");
        var newNodes = new List<ushort> { 10, 11 };
        await _nodeChangeHandler.OnNodesAddedAsync(newNodes, "扩容演示");
        _logger.LogInformation("    - 1000个老Service固定在原节点（TTL=24h）");
        _logger.LogInformation("    - 新哈希环包含节点1,2,3,4,5,10,11");
        await Task.Delay(500);

        // 时刻 T2-T24: 渐进过渡期
        _logger.LogInformation("T2-T24: 渐进过渡期");
        _logger.LogInformation("    - 老玩家下线 → 清理固定映射");
        _logger.LogInformation("    - 新玩家登录 → 使用新哈希环创建");
        _logger.LogInformation("    - 老玩家重新登录 → 使用新哈希环创建（可能到新节点）");
        await Task.Delay(500);

        // 模拟一些玩家下线和新玩家登录
        for (int i = 0; i < 5; i++)
        {
            var oldPlayerId = $"old_player_{i}";
            var oldHash = NodeConsistentHashRing.ComputeHash(oldPlayerId);
            await _lifecycle.OnServiceShutdownAsync(oldHash, ShutdownReason.PlayerLogout);
            _logger.LogInformation("    - 老玩家 {PlayerId} 下线", oldPlayerId);

            var newPlayerId = $"new_player_{i}";
            var newHash = NodeConsistentHashRing.ComputeHash(newPlayerId);
            var nodeId = await _lifecycle.OnServiceCreatedAsync(newHash, "新玩家登录");
            _logger.LogInformation("    - 新玩家 {PlayerId} 登录到节点 {NodeId}", newPlayerId, nodeId);

            await Task.Delay(200);
        }

        // 时刻 T24+: 完全过渡
        _logger.LogInformation("T24+: 完全过渡");
        _logger.LogInformation("    - 固定映射全部过期");
        _logger.LogInformation("    - 所有Service都使用新哈希环");

        _logger.LogInformation("=== 自然过渡演示完成 ===\n");
    }

    /// <summary>
    /// 打印路由指标
    /// </summary>
    private void PrintMetrics()
    {
        var metrics = _router.GetMetrics();

        _logger.LogInformation("--- 路由指标 ---");
        _logger.LogInformation("一致性哈希路由次数: {Count}", metrics.ConsistentHashRouteCount);
        _logger.LogInformation("固定映射路由次数: {Count}", metrics.FixedMappingRouteCount);
        _logger.LogInformation("当前哈希环版本: {Version}", metrics.HashRingVersion);
        _logger.LogInformation("活跃节点数: {Count}", metrics.ActiveNodeCount);

        var totalRoutes = metrics.ConsistentHashRouteCount + metrics.FixedMappingRouteCount;
        if (totalRoutes > 0)
        {
            var fixedPercentage = (double)metrics.FixedMappingRouteCount / totalRoutes * 100;
            _logger.LogInformation("固定映射使用率: {Percentage:F2}%", fixedPercentage);
        }

        _logger.LogInformation("---------------");
    }
}

// ============================================
// 主程序入口示例（需要在控制台应用中使用）
// ============================================
/*
/// <summary>
/// 主程序入口示例
/// 使用方法：创建一个Console应用，添加以下代码
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        // 配置服务
        var services = new ServiceCollection();

        // 添加日志（需要引用 Microsoft.Extensions.Logging.Console）
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // 添加集群路由
        services.AddClusterRouting(options =>
        {
            options.NodeId = 1;
            options.NodeName = "GameServer-01";
            options.EtcdEndpoints = new[] { "http://localhost:2379" };
            options.FixedMappingTTL = TimeSpan.FromHours(24);
            options.VirtualNodesPerNode = 150;
            options.EnableFixedMapping = true;
        });

        // 注册示例类
        services.AddSingleton<ClusterScalingExample>();

        // 构建服务提供者
        var serviceProvider = services.BuildServiceProvider();

        // 运行示例
        var example = serviceProvider.GetRequiredService<ClusterScalingExample>();

        Console.WriteLine("PulseRPC 集群路由 - 零迁移扩缩容示例\n");

        // 运行所有示例
        await example.Example1_ScaleOut();
        await Task.Delay(1000);

        await example.Example2_ScaleIn();
        await Task.Delay(1000);

        await example.Example3_ServiceLifecycle();
        await Task.Delay(1000);

        example.Example4_Monitoring();
        await Task.Delay(1000);

        await example.Example5_NaturalTransition();

        Console.WriteLine("\n所有示例运行完成！");
    }
}
*/
