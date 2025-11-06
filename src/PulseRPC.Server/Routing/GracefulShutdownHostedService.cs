using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PulseRPC.Server.Routing;

/// <summary>
/// 优雅关闭托管服务
/// 在应用停止时触发优雅关闭流程
/// </summary>
public class GracefulShutdownHostedService : IHostedService
{
    private readonly IGracefulShutdownCoordinator _coordinator;
    private readonly ILogger<GracefulShutdownHostedService> _logger;
    private readonly IHostApplicationLifetime _lifetime;

    public GracefulShutdownHostedService(
        IGracefulShutdownCoordinator coordinator,
        ILogger<GracefulShutdownHostedService> logger,
        IHostApplicationLifetime lifetime)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("优雅关闭服务已启动");

        // 注册应用停止事件
        _lifetime.ApplicationStopping.Register(() =>
        {
            _logger.LogWarning("检测到应用停止信号，开始优雅关闭...");

            // 同步等待优雅关闭完成（ASP.NET Core会等待此回调完成）
            _coordinator.InitiateShutdownAsync(
                "应用停止",
                CancellationToken.None).GetAwaiter().GetResult();
        });

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("优雅关闭服务已停止");
        return Task.CompletedTask;
    }
}
