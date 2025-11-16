using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DistributedGameApp.Infrastructure.Hosting.Bootstrap;

/// <summary>
/// 阶段7: 标记为就绪，开始提供服务
/// </summary>
public class Phase7_MarkReady : IBootstrapPhase
{
    private readonly ILogger<Phase7_MarkReady> _logger;

    public string PhaseName => "Phase 7: Mark Ready";

    public Phase7_MarkReady(ILogger<Phase7_MarkReady> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> ExecuteAsync(BootstrapContext context, CancellationToken cancellationToken)
    {
        _logger.LogInformation("========== {PhaseName} ==========", PhaseName);

        try
        {
            var elapsedTime = DateTime.UtcNow - context.StartTime;

            _logger.LogInformation("服务器启动完成！");
            _logger.LogInformation("  - 启动耗时: {ElapsedMs} ms", (int)elapsedTime.TotalMilliseconds);

            if (context.ExternalServer != null)
            {
                _logger.LogInformation("  - External 服务器: 就绪");
            }

            if (context.InternalServer != null)
            {
                _logger.LogInformation("  - Internal 服务器: 就绪");
            }

            if (!string.IsNullOrEmpty(context.ServiceId))
            {
                _logger.LogInformation("  - Consul 服务ID: {ServiceId}", context.ServiceId);
            }

            var totalDiscoveredNodes = context.DiscoveredServices.Values.Sum(list => list.Count);
            _logger.LogInformation("  - 已发现节点数: {TotalNodes}", totalDiscoveredNodes);

            // 打印已建立的连接数
            if (context.State.TryGetValue("NodeConnections", out var connectionsObj)
                && connectionsObj is System.Collections.IList connections)
            {
                _logger.LogInformation("  - 已建立连接数: {ConnectedNodes}", connections.Count);
            }

            // 打印系统状态摘要
            _logger.LogInformation("");
            _logger.LogInformation("========== 系统状态摘要 ==========");
            _logger.LogInformation("服务类型: {ServiceType}", context.ServiceProvider.GetRequiredService<ServerIdentityOptions>().ServiceType);
            _logger.LogInformation("节点ID: {NodeId}", context.ServiceProvider.GetRequiredService<ServerIdentityOptions>().NodeId);
            _logger.LogInformation("外网监听: {Status}", context.ExternalServer?.IsRunning == true ? "✓" : "✗");
            _logger.LogInformation("内网监听: {Status}", context.InternalServer?.IsRunning == true ? "✓" : "✗");
            _logger.LogInformation("Consul注册: {Status}", !string.IsNullOrEmpty(context.ServiceId) ? "✓" : "✗");

            // 打印连接统计
            var connectedCount = 0;
            if (context.State.TryGetValue("NodeConnections", out var conns)
                && conns is System.Collections.IList connList)
            {
                connectedCount = connList.Count;
            }
            _logger.LogInformation("已连接节点数: {Count}", connectedCount);

            _logger.LogInformation("黑名单节点: {Count}", context.ExceptionList.Blacklist.Count);
            _logger.LogInformation("白名单节点: {Count}", context.ExceptionList.Whitelist.Count);
            _logger.LogInformation("===================================");
            _logger.LogInformation("");
            _logger.LogInformation("🚀 服务器已就绪，开始提供服务");
            _logger.LogInformation("");

            await Task.CompletedTask;

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "标记就绪阶段失败");
            return false;
        }
    }
}
