using DistributedGameApp.Infrastructure.ServiceClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DistributedGameApp.Infrastructure.Hosting.Bootstrap;

/// <summary>
/// 阶段5.5: 注册跨服务客户端类型
/// </summary>
/// <remarks>
/// <para>
/// 此阶段负责在 ServiceClientManager 中注册服务类型，但不等待服务就绪。
/// </para>
/// <para>
/// 设计原理：
/// <list type="bullet">
/// <item>服务可以按任意顺序启动</item>
/// <item>启动时只注册服务类型，不等待连接</item>
/// <item>运行时调用 GetServer/GetHub 时按需建立连接</item>
/// </list>
/// </para>
/// </remarks>
public class Phase5_5_InitializeServiceClient : IBootstrapPhase
{
    private readonly ILogger<Phase5_5_InitializeServiceClient> _logger;

    public string PhaseName => "Phase 5.5: Register Service Client Types";

    public Phase5_5_InitializeServiceClient(ILogger<Phase5_5_InitializeServiceClient> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> ExecuteAsync(BootstrapContext context, CancellationToken cancellationToken)
    {
        _logger.LogInformation("========== {PhaseName} ==========", PhaseName);

        try
        {
            // 1. 获取服务依赖配置
            var dependencyOptions = context.ServiceProvider.GetService<IOptions<ServiceDependencyOptions>>()?.Value
                ?? new ServiceDependencyOptions();

            if (dependencyOptions.ServerTypes.Length == 0)
            {
                _logger.LogInformation("未配置服务类型，跳过服务客户端注册");
                return true;
            }

            _logger.LogInformation("注册服务类型: {Types}（按需连接，不等待就绪）",
                string.Join(", ", dependencyOptions.ServerTypes));

            // 2. 获取 ServiceClientManager
            var serviceClientManager = context.ServiceProvider.GetService<ServiceClientManager>();
            if (serviceClientManager == null)
            {
                _logger.LogWarning("ServiceClientManager 未注册，跳过服务客户端初始化");
                return true;
            }

            // 3. 注册所有服务类型（不等待就绪）
            foreach (var serverType in dependencyOptions.ServerTypes)
            {
                try
                {
                    await serviceClientManager.RegisterServerTypeAsync(
                        serverType,
                        dependencyOptions.RoutingStrategy,
                        maxRetries: 1,
                        retryDelayMs: 500,
                        allowEmpty: true,  // 允许空连接，运行时按需建立
                        cancellationToken);

                    var stats = serviceClientManager.GetStats();
                    var connCount = stats.ServerStats.TryGetValue(serverType, out var s) ? s.ConnectedCount : 0;

                    if (connCount > 0)
                    {
                        _logger.LogInformation("  ✓ {ServerType}: 已注册，{Count} 个连接可用",
                            serverType, connCount);
                    }
                    else
                    {
                        _logger.LogInformation("  ○ {ServerType}: 已注册，等待运行时连接",
                            serverType);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "  ✗ {ServerType}: 注册失败（将在运行时重试）", serverType);
                }
            }

            // 4. 保存配置到上下文（供运行时使用）
            context.State["ServiceClientManager"] = serviceClientManager;
            context.State["ServiceDependencyOptions"] = dependencyOptions;

            _logger.LogInformation("✓ 服务类型注册完成（连接将在运行时按需建立）");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "服务类型注册失败");
            return false;
        }
    }
}
