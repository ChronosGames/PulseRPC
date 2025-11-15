using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DistributedGameApp.Infrastructure.MongoDB.Repositories;

/// <summary>
/// 索引初始化后台服务 - 在应用启动时自动初始化索引
/// </summary>
/// <remarks>
/// <para><strong>使用场景</strong>：</para>
/// <list type="bullet">
/// <item><description>开发环境：快速启动，自动创建索引</description></item>
/// <item><description>测试环境：集成测试前自动准备索引</description></item>
/// <item><description>生产环境：配合分布式锁使用</description></item>
/// </list>
/// </remarks>
public class IndexInitializerHostedService : IHostedService
{
    private readonly DistributedIndexInitializer _indexInitializer;
    private readonly ILogger<IndexInitializerHostedService> _logger;
    private readonly bool _enabled;

    public IndexInitializerHostedService(
        DistributedIndexInitializer indexInitializer,
        ILogger<IndexInitializerHostedService> logger,
        bool enabled = true)
    {
        _indexInitializer = indexInitializer ?? throw new ArgumentNullException(nameof(indexInitializer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _enabled = enabled;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_enabled)
        {
            _logger.LogInformation("索引自动初始化已禁用（配置：IndexInitializer:Enabled=false）");
            return;
        }

        _logger.LogInformation("应用启动中 - 开始初始化 MongoDB 索引...");

        try
        {
            await _indexInitializer.EnsureIndexesAsync(cancellationToken);
            _logger.LogInformation("✓ MongoDB 索引初始化完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 索引初始化失败");

            // 根据配置决定是否允许应用启动失败
            // 生产环境建议设置为 false，允许应用启动（索引可以后续手动创建）
            // 开发环境建议设置为 true，确保索引存在
            throw; // 或者根据配置决定是否抛出异常
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("应用停止中 - 无需清理索引相关资源");
        return Task.CompletedTask;
    }
}
