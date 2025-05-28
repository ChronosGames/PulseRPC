using System.Reflection;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PulseRPC.Network;
using PulseRPC.Serialization;

namespace PulseRPC.Server;

/// <summary>
/// 消息处理器扩展方法
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPulseServerServices(this IServiceCollection services)
     {
         // 添加序列化器
         services.AddSingleton<ISerializerProvider>(PulseRPCSerializerProvider.Instance);

         // 添加事件发布器
         services.AddSingleton<IEventPublisher, EventPublisher>();

         // 添加服务
         services.AddSingleton<IServerChannelManager, ServerChannelManager>();

         return services;
     }
}
