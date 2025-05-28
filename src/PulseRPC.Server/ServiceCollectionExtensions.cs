using System.Reflection;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PulseRPC.Network;
using PulseRPC.Serialization;
using PulseRPC.Server.Auth;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Services;
using PulseRPC.Server.Transport;
using PulseRPC.Server.Events;

namespace PulseRPC.Server;

/// <summary>
/// 消息处理器扩展方法
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 添加PulseRPC服务器服务
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddPulseRpcServer(this IServiceCollection services)
    {
        // 注册序列化器提供程序
        services.AddSingleton<ISerializerProvider>(PulseRPCSerializerProvider.Instance);

        // 注册通道管理器
        services.AddSingleton<IServerChannelManager>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<ServerChannelManager>>();
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            return new ServerChannelManager(logger, loggerFactory);
        });

        // 注册认证中间件
        services.AddSingleton<AuthenticationMiddleware>();

        // 注册ServiceRegistry（使用工厂方法）
        services.AddSingleton<ServiceRegistry>(sp =>
        {
            var authMiddleware = sp.GetRequiredService<AuthenticationMiddleware>();
            var channelManager = sp.GetRequiredService<IServerChannelManager>();
            var serializerProvider = sp.GetRequiredService<ISerializerProvider>();
            var logger = sp.GetRequiredService<ILogger<ServiceRegistry>>();

            return ServiceRegistry.CreateWithRpcHandling(authMiddleware, channelManager, serializerProvider, logger);
        });

        // 注册事件发布器
        services.AddSingleton<IEventPublisher, EventPublisher>();

        return services;
    }
}
