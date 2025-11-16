using DistributedGameApp.Infrastructure.Consul;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DistributedGameApp.Infrastructure.Hosting.Bootstrap;

/// <summary>
/// 阶段3: 从 Consul 同步其他服务器节点信息
/// </summary>
public class Phase3_SyncServerNodes : IBootstrapPhase
{
    private readonly ILogger<Phase3_SyncServerNodes> _logger;

    public string PhaseName => "Phase 3: Sync Server Nodes from Consul";

    // 需要同步的服务类型列表
    private static readonly string[] ServiceTypesToSync = new[]
    {
        "GameServer",
        "BattleServer",
        "BackendServer",
        "LoginServer"
    };

    public Phase3_SyncServerNodes(ILogger<Phase3_SyncServerNodes> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> ExecuteAsync(BootstrapContext context, CancellationToken cancellationToken)
    {
        _logger.LogInformation("========== {PhaseName} ==========", PhaseName);

        try
        {
            // 获取 Consul 服务发现
            var consulDiscovery = context.ServiceProvider.GetService<ConsulServiceDiscovery>();
            if (consulDiscovery == null)
            {
                _logger.LogWarning("ConsulServiceDiscovery 未配置，跳过节点同步");
                return true;
            }

            _logger.LogInformation("正在从 Consul 同步服务器节点信息...");

            var totalNodes = 0;

            // 同步各类型服务节点
            foreach (var serviceType in ServiceTypesToSync)
            {
                try
                {
                    var services = await consulDiscovery.GetServicesAsync(serviceType, cancellationToken);

                    if (services.Count > 0)
                    {
                        context.DiscoveredServices[serviceType] = services;
                        totalNodes += services.Count;

                        _logger.LogInformation(
                            "  - {ServiceType}: 发现 {Count} 个节点",
                            serviceType,
                            services.Count);

                        // 记录详细节点信息
                        foreach (var service in services)
                        {
                            _logger.LogDebug(
                                "    [{ServiceId}] {Host}:{Port} (负载: {Load}/{Max}, 状态: {Status})",
                                service.ServiceId,
                                service.Host,
                                service.TcpPort,
                                service.CurrentLoad,
                                service.MaxCapacity,
                                service.Status);
                        }
                    }
                    else
                    {
                        _logger.LogDebug("  - {ServiceType}: 未发现节点", serviceType);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "同步 {ServiceType} 节点失败", serviceType);
                    // 继续同步其他类型
                }
            }

            _logger.LogInformation("✓ 节点同步完成: 共发现 {TotalNodes} 个服务器节点", totalNodes);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "节点同步阶段失败");
            // 节点同步失败不影响启动流程
            return true;
        }
    }
}
