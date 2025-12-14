using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DistributedGameApp.Infrastructure.Hosting.Bootstrap;

/// <summary>
/// 服务器启动流程编排器
/// 统一管理服务器的启动流程
/// </summary>
public sealed class ServerBootstrapOrchestrator : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ServerBootstrapOrchestrator> _logger;
    private readonly List<IBootstrapPhase> _phases;
    private BootstrapContext? _context;

    public ServerBootstrapOrchestrator(
        IServiceProvider serviceProvider,
        ILogger<ServerBootstrapOrchestrator> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // 初始化启动阶段
        _phases = new List<IBootstrapPhase>
        {
            new Phase1_StartExternalServer(
                _serviceProvider.GetRequiredService<ILogger<Phase1_StartExternalServer>>()),

            new Phase2_StartInternalServer(
                _serviceProvider.GetRequiredService<ILogger<Phase2_StartInternalServer>>()),

            new Phase3_SyncServerNodes(
                _serviceProvider.GetRequiredService<ILogger<Phase3_SyncServerNodes>>()),

            new Phase4_SyncExceptionList(
                _serviceProvider.GetRequiredService<ILogger<Phase4_SyncExceptionList>>()),

            new Phase5_ConnectToOtherNodes(
                _serviceProvider.GetRequiredService<ILogger<Phase5_ConnectToOtherNodes>>()),

            // Phase 5.5: 初始化跨服务客户端连接（在注册到 Consul 之前）
            // 确保依赖服务可用后再对外提供服务
            new Phase5_5_InitializeServiceClient(
                _serviceProvider.GetRequiredService<ILogger<Phase5_5_InitializeServiceClient>>()),

            new Phase6_RegisterToConsul(
                _serviceProvider.GetRequiredService<ILogger<Phase6_RegisterToConsul>>()),

            new Phase7_MarkReady(
                _serviceProvider.GetRequiredService<ILogger<Phase7_MarkReady>>())
        };
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("");
        _logger.LogInformation("╔═══════════════════════════════════════════════════════╗");
        _logger.LogInformation("║      Server Bootstrap Orchestrator - 启动流程编排器     ║");
        _logger.LogInformation("╚═══════════════════════════════════════════════════════╝");
        _logger.LogInformation("");

        try
        {
            // 创建启动上下文
            _context = new BootstrapContext
            {
                ServiceProvider = _serviceProvider
            };

            // 依次执行各个阶段
            for (var i = 0; i < _phases.Count; i++)
            {
                var phase = _phases[i];
                var phaseNumber = i + 1;

                _logger.LogInformation("[{PhaseNumber}/{TotalPhases}] 执行阶段: {PhaseName}",
                    phaseNumber, _phases.Count, phase.PhaseName);

                var success = await phase.ExecuteAsync(_context, cancellationToken);

                if (!success)
                {
                    _logger.LogError("✗ 阶段 {PhaseNumber} 执行失败: {PhaseName}", phaseNumber, phase.PhaseName);
                    _logger.LogError("服务器启动流程中止");
                    throw new InvalidOperationException($"Bootstrap phase failed: {phase.PhaseName}");
                }

                _logger.LogInformation("");
            }

            _logger.LogInformation("╔═══════════════════════════════════════════════════════╗");
            _logger.LogInformation("║             所有启动阶段执行成功！服务器已就绪              ║");
            _logger.LogInformation("╚═══════════════════════════════════════════════════════╝");
            _logger.LogInformation("");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("服务器启动流程被取消");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "服务器启动流程失败");
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("");
        _logger.LogInformation("========== 服务器关闭流程 ==========");

        try
        {
            if (_context == null)
            {
                _logger.LogWarning("启动上下文为空，跳过关闭流程");
                return;
            }

            // 1. 从 Consul 注销
            if (!string.IsNullOrEmpty(_context.ServiceId))
            {
                _logger.LogInformation("从 Consul 注销服务...");

                var consulRegistry = _serviceProvider.GetService<Consul.ConsulServiceRegistry>();
                if (consulRegistry != null)
                {
                    await consulRegistry.UnregisterServiceAsync(_context.ServiceId, cancellationToken);
                    _logger.LogInformation("✓ 服务已从 Consul 注销: {ServiceId}", _context.ServiceId);
                }
            }

            // 2. 断开节点连接
            if (_context.State.TryGetValue("InternalRpcClient", out var clientObj)
                && clientObj is PulseRPC.Client.IPulseClient client)
            {
                _logger.LogInformation("断开所有节点连接...");
                try
                {
                    await client.StopAsync(graceful: true, timeout: TimeSpan.FromSeconds(5), cancellationToken);
                    _logger.LogInformation("✓ 所有节点连接已断开");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "断开节点连接时发生异常");
                }
            }

            // 3. 停止 External 服务器
            if (_context.ExternalServer != null && _context.ExternalServer.IsRunning)
            {
                _logger.LogInformation("停止 External 服务器...");
                await _context.ExternalServer.StopAsync(cancellationToken);
                _logger.LogInformation("✓ External 服务器已停止");
            }

            // 4. 停止 Internal 服务器
            if (_context.InternalServer != null && _context.InternalServer.IsRunning)
            {
                _logger.LogInformation("停止 Internal 服务器...");
                await _context.InternalServer.StopAsync(cancellationToken);
                _logger.LogInformation("✓ Internal 服务器已停止");
            }

            _logger.LogInformation("服务器关闭流程完成");
            _logger.LogInformation("===================================");
            _logger.LogInformation("");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "服务器关闭流程异常");
            throw;
        }
    }
}
