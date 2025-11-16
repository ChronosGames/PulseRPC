using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PulseRPC.Server;

namespace DistributedGameApp.Infrastructure.Hosting.Bootstrap;

/// <summary>
/// 阶段2: 启动 Internal 服务器（内网 RPC 通道）
/// </summary>
public class Phase2_StartInternalServer : IBootstrapPhase
{
    private readonly ILogger<Phase2_StartInternalServer> _logger;

    public string PhaseName => "Phase 2: Start Internal Server";

    public Phase2_StartInternalServer(ILogger<Phase2_StartInternalServer> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> ExecuteAsync(BootstrapContext context, CancellationToken cancellationToken)
    {
        _logger.LogInformation("========== {PhaseName} ==========", PhaseName);

        try
        {
            // 获取 Internal 服务器
            var internalServer = context.ServiceProvider.GetKeyedService<INamedPulseServer>("Internal");

            if (internalServer == null)
            {
                _logger.LogWarning("Internal 服务器未配置，跳过此阶段");
                return true;
            }

            _logger.LogInformation("启动 Internal 服务器...");

            // 启动服务器
            await internalServer.StartAsync(cancellationToken);

            // 等待服务器完全启动
            await Task.Delay(500, cancellationToken);

            // 验证服务器状态
            if (!internalServer.IsRunning)
            {
                _logger.LogError("Internal 服务器启动失败: 状态={State}", internalServer.State);
                return false;
            }

            // 保存到上下文
            context.InternalServer = internalServer;

            // 记录传输信息
            var transports = internalServer.GetTransports();
            foreach (var (name, transport) in transports)
            {
                _logger.LogInformation(
                    "  - Internal/{TransportName}: {Type} @ {Endpoint} [{Status}]",
                    name,
                    transport.Type,
                    transport.LocalEndPoint,
                    transport.IsListening ? "Listening" : "NotListening");
            }

            _logger.LogInformation("✓ Internal 服务器启动成功 (连接数: {Count})", internalServer.ActiveConnectionCount);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Internal 服务器启动失败");
            return false;
        }
    }
}
