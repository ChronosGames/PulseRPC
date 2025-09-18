using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseRPC.Server.Processing;
using PulseRPC.Server.Transport;

namespace PulseRPC.Server.Extensions;

/// <summary>
/// PulseRPC Server 依赖注入扩展方法 - 三层抽象架构增强版
/// </summary>
public static class ServerServiceCollectionExtensions
{
    /// <summary>
    /// 添加PulseRPC服务端核心服务（传统模式）
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddPulseServer(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // 注册传统的通道管理器
        services.TryAddSingleton<IServerChannelManager, ServerChannelManager>();

        return services;
    }

    /// <summary>
    /// 添加客户端会话管理服务
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configure">配置选项</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddPulseServer(this IServiceCollection services, Action<EnhancedServerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // 配置选项
        if (configure != null)
        {
            services.Configure(configure);
        }

        return services;
    }
}

/// <summary>
/// 增强服务器配置选项
/// </summary>
public class EnhancedServerOptions
{
    /// <summary>
    /// 是否启用会话管理
    /// </summary>
    public bool EnableSessionManagement { get; set; } = true;

    /// <summary>
    /// 是否启用自动会话清理
    /// </summary>
    public bool EnableAutoSessionCleanup { get; set; } = true;

    /// <summary>
    /// 会话超时时间（毫秒）
    /// </summary>
    public int SessionTimeoutMs { get; set; } = 300000; // 5分钟

    /// <summary>
    /// 健康检查间隔（毫秒）
    /// </summary>
    public int HealthCheckIntervalMs { get; set; } = 30000; // 30秒

    /// <summary>
    /// 清理间隔（毫秒）
    /// </summary>
    public int CleanupIntervalMs { get; set; } = 60000; // 1分钟
}

