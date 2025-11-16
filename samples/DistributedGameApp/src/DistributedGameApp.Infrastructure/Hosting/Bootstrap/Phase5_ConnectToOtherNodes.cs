using DistributedGameApp.Infrastructure.Consul;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PulseRPC.Client;
using PulseRPC.Serialization;
using PulseRPC.Transport;

namespace DistributedGameApp.Infrastructure.Hosting.Bootstrap;

/// <summary>
/// 阶段5: 建立与其他节点的内网物理连接
/// </summary>
public class Phase5_ConnectToOtherNodes : IBootstrapPhase
{
    private readonly ILogger<Phase5_ConnectToOtherNodes> _logger;

    public string PhaseName => "Phase 5: Connect to Other Nodes";

    public Phase5_ConnectToOtherNodes(ILogger<Phase5_ConnectToOtherNodes> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> ExecuteAsync(BootstrapContext context, CancellationToken cancellationToken)
    {
        _logger.LogInformation("========== {PhaseName} ==========", PhaseName);

        try
        {
            // 检查是否有 Internal 服务器
            if (context.InternalServer == null)
            {
                _logger.LogInformation("Internal 服务器未配置，跳过节点连接");
                return true;
            }

            // 获取所有已发现的节点
            var allNodes = context.DiscoveredServices.Values
                .SelectMany(list => list)
                .ToList();

            if (allNodes.Count == 0)
            {
                _logger.LogInformation("未发现其他节点，跳过连接建立");
                return true;
            }

            _logger.LogInformation("正在建立与 {Count} 个节点的内网连接...", allNodes.Count);

            // 创建 PulseRPC 客户端（用于内网连接）
            var loggerFactory = context.ServiceProvider.GetService<ILoggerFactory>();
            var serializerProvider = context.ServiceProvider.GetService<ISerializerProvider>()
                ?? PulseRPCSerializerProvider.Instance;

            var clientBuilder = new PulseClientBuilder()
                .WithLogging(loggerFactory)
                .WithSerializer(serializerProvider)
                .WithLoadBalancing(LoadBalancingStrategy.RoundRobin);

            var client = clientBuilder.Build();
            await client.InitializeAsync(cancellationToken);

            // 保存客户端到上下文
            context.State["InternalRpcClient"] = client;

            var successCount = 0;
            var failedCount = 0;
            var skippedCount = 0;
            var connections = new List<PulseRPC.Client.IClientChannel>();

            foreach (var node in allNodes)
            {
                try
                {
                    // 检查黑名单
                    if (context.ExceptionList.Blacklist.Contains(node.ServiceId))
                    {
                        _logger.LogDebug("跳过黑名单节点: {ServiceId}", node.ServiceId);
                        skippedCount++;
                        continue;
                    }

                    // 检查是否是自己
                    if (node.ServiceId == context.ServiceId)
                    {
                        _logger.LogDebug("跳过自己: {ServiceId}", node.ServiceId);
                        skippedCount++;
                        continue;
                    }

                    // 获取内网端点
                    var internalEndpoint = node.InternalEndpoint ?? node.GetPreferredEndpoint(preferInternal: true);
                    if (internalEndpoint == null || !internalEndpoint.Enabled)
                    {
                        _logger.LogWarning("节点 {ServiceId} 没有可用的内网端点", node.ServiceId);
                        skippedCount++;
                        continue;
                    }

                    // 尝试建立连接
                    _logger.LogDebug("连接到 {ServiceId} ({Host}:{Port})...",
                        node.ServiceId,
                        internalEndpoint.Host,
                        internalEndpoint.TcpPort);

                    var connection = await client.ConnectToServerAsync(
                        internalEndpoint.Host,
                        internalEndpoint.TcpPort,
                        serverId: node.ServiceId,
                        name: $"Internal-{node.ServiceType}-{node.NodeId}",
                        transport: TransportType.TCP,
                        strategy: ConnectionStrategy.Session,
                        cancellationToken: cancellationToken);

                    connections.Add(connection);
                    successCount++;

                    _logger.LogInformation(
                        "  ✓ 已连接到 {ServiceId} ({ServiceType}) @ {Host}:{Port}",
                        node.ServiceId,
                        node.ServiceType,
                        internalEndpoint.Host,
                        internalEndpoint.TcpPort);
                }
                catch (Exception ex)
                {
                    failedCount++;
                    _logger.LogWarning(ex,
                        "连接到 {ServiceId} 失败: {Message}",
                        node.ServiceId,
                        ex.Message);
                    // 继续尝试连接其他节点
                }
            }

            // 保存连接到上下文
            context.State["NodeConnections"] = connections;

            _logger.LogInformation(
                "✓ 节点连接完成: 成功 {Success}, 失败 {Failed}, 跳过 {Skipped}",
                successCount,
                failedCount,
                skippedCount);

            // 即使有失败也继续（连接是可选的）
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "建立节点连接阶段失败");
            // 节点连接失败不影响启动流程
            return true;
        }
    }
}
