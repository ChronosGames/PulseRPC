using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PulseRPC.Server;

namespace DistributedGameApp.Infrastructure.Hosting.Bootstrap;

/// <summary>
/// 阶段1: 启动 External 服务器（外网监听）
/// </summary>
public class Phase1_StartExternalServer : IBootstrapPhase
{
    private readonly ILogger<Phase1_StartExternalServer> _logger;

    public string PhaseName => "Phase 1: Start External Server";

    public Phase1_StartExternalServer(ILogger<Phase1_StartExternalServer> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> ExecuteAsync(BootstrapContext context, CancellationToken cancellationToken)
    {
        _logger.LogInformation("========== {PhaseName} ==========", PhaseName);

        try
        {
            // 获取 External 服务器
            var externalServer = context.ServiceProvider.GetKeyedService<INamedPulseServer>("External");

            if (externalServer == null)
            {
                _logger.LogInformation("External 服务器未配置，跳过此阶段");
                return true;
            }

            _logger.LogInformation("启动 External 服务器...");

            // 启动服务器
            await externalServer.StartAsync(cancellationToken);

            // 等待服务器完全启动
            await Task.Delay(500, cancellationToken);

            // 验证服务器状态
            if (!externalServer.IsRunning)
            {
                _logger.LogError("External 服务器启动失败: 状态={State}", externalServer.State);
                return false;
            }

            // 保存到上下文
            context.ExternalServer = externalServer;

            // 记录传输信息
            var transports = externalServer.GetTransports();
            foreach (var (name, transport) in transports)
            {
                _logger.LogInformation(
                    "  - External/{TransportName}: {Type} @ {Endpoint} [{Status}]",
                    name,
                    transport.Type,
                    transport.LocalEndPoint,
                    transport.IsListening ? "Listening" : "NotListening");
            }

            _logger.LogInformation("✓ External 服务器启动成功 (连接数: {Count})", externalServer.ActiveConnectionCount);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "External 服务器启动失败");
            return false;
        }
    }
}
