using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PulseRPC.Server.Routing;

/// <summary>
/// 优雅关闭健康检查
/// 在节点准备关闭时返回不健康状态，让负载均衡器停止发送新请求
/// </summary>
public class GracefulShutdownHealthCheck(IGracefulShutdownCoordinator coordinator) : IHealthCheck
{
    private readonly IGracefulShutdownCoordinator _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // 如果正在关闭，返回不健康状态
        if (_coordinator.IsShuttingDown)
        {
            var progress = _coordinator.GetProgress();
            var data = new Dictionary<string, object>
            {
                ["state"] = progress.State.ToString(),
                ["completionPercentage"] = progress.CompletionPercentage,
                ["pendingRequests"] = progress.PendingRequests,
                ["currentStep"] = progress.CurrentStep
            };

            return Task.FromResult(HealthCheckResult.Unhealthy(
                description: $"节点正在关闭: {progress.CurrentStep}",
                data: data));
        }

        // 检查是否可以接受新连接
        if (!_coordinator.CanAcceptNewConnections())
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                description: "节点不接受新连接"));
        }

        return Task.FromResult(HealthCheckResult.Healthy("节点运行正常"));
    }
}
