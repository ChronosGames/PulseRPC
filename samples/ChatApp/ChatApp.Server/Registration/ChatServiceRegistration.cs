using ChatApp.NewArchitecture.Contracts;
using ChatApp.NewArchitecture.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Extensions;

namespace ChatApp.NewArchitecture.Registration;

/// <summary>
/// 聊天服务 DI 注册
/// </summary>
/// <remarks>
/// <para><strong>注册内容</strong>：</para>
/// <list type="bullet">
/// <item><description>ChatRoomService - 有状态服务，按 RoomId 缓存</description></item>
/// <item><description>ChatRoomHub - 无状态 Hub，Singleton</description></item>
/// </list>
/// </remarks>
public static class ChatServiceRegistration
{
    /// <summary>
    /// 注册聊天服务（新架构）
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合，支持链式调用</returns>
    /// <example>
    /// <code>
    /// // 在 Program.cs 中
    /// services.AddChatServices();
    /// </code>
    /// </example>
    public static IServiceCollection AddChatServices(this IServiceCollection services)
    {
        // ════════════════════════════════════════════════════════════════════
        // 1. 注册有状态服务（ChatRoomService）
        // ════════════════════════════════════════════════════════════════════
        //
        // AddPulseService 会自动：
        //   - 注册 PulseServiceManagement（如果尚未注册）
        //   - 注册 IServiceAccessor<ChatRoomService>
        //   - 注册服务工厂到 PulseServiceManager
        //
        // 服务实例按 RoomId 缓存，相同 RoomId 返回相同实例
        //
        services.AddPulseService<ChatRoomService>((sp, roomId) =>
        {
            var logger = sp.GetRequiredService<ILogger<ChatRoomService>>();
            return new ChatRoomService(roomId, logger);
        });

        // ════════════════════════════════════════════════════════════════════
        // 2. 注册无状态 Hub（ChatRoomHub）
        // ════════════════════════════════════════════════════════════════════
        //
        // Hub 是无状态的，注册为 Singleton，全局复用
        // Hub 通过 IServiceAccessor<ChatRoomService> 访问 Service
        //
        services.AddSingleton<IChatRoomHub, ChatRoomHub>();

        return services;
    }
}

/// <summary>
/// Program.cs 使用示例
/// </summary>
/// <remarks>
/// <code>
/// // Program.cs
/// var builder = Host.CreateDefaultBuilder(args);
///
/// builder.ConfigureServices((context, services) =>
/// {
///     // 添加 PulseRPC 服务器
///     services.AddPulseServer(options =>
///     {
///         options.Transports = new()
///         {
///             new TransportChannelConfiguration
///             {
///                 Name = "TCP",
///                 Type = TransportType.TCP,
///                 Port = 7000,
///                 IsDefault = true
///             }
///         };
///     });
///
///     // 添加聊天服务（新架构）
///     services.AddChatServices();
/// });
///
/// var host = builder.Build();
/// await host.RunAsync();
/// </code>
/// </remarks>
file class ProgramExample { }

