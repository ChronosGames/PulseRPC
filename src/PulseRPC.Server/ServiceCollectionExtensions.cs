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
        // 注册核心服务
        services.AddSingleton<ServiceRegistry>();

        // 注册通道管理器
        services.AddSingleton<IServerChannelManager, ServerChannelManager>();

        // 注册认证中间件
        services.AddSingleton<AuthenticationMiddleware>();

        return services;
    }
}
