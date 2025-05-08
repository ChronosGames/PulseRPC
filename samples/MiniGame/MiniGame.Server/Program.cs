using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PulseRPC.Protocol;
using PulseRPC.Protocol.Serialization;
using PulseRPC.Samples.Server.Handlers;
using PulseRPC.Samples.Shared;
using PulseRPC.Samples.Shared.Messages;
using PulseRPC.Server;

namespace PulseRPC.Samples.Server;

/// <summary>
/// 迷你游戏服务器示例程序
/// </summary>
internal class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("PulseRPC 迷你游戏服务器示例");
        Console.WriteLine("=============================");

        // 创建服务容器
        var services = new ServiceCollection();

        // 添加日志
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        // 注册消息类型
        RegisterMessageTypes();

        // 注册通知服务
        services.AddSingleton<NotificationService>();

        // 添加 PulseRPC 服务
        services.AddTcpServer(ipAddress: "127.0.0.1", port: 5000);

        // 手动注册各种消息处理器
        services.AddMessageHandler<LoginRequestHandler>();
        services.AddMessageHandler<RegisterRequestHandler>();
        services.AddMessageHandler<GetUserInfoRequestHandler>();
        services.AddMessageHandler<UpdateUserInfoRequestHandler>();

        // 构建服务提供程序
        var serviceProvider = services.BuildServiceProvider();

        try
        {
            // 获取服务器初始化器
            var serverInitializer = serviceProvider.GetRequiredService<ServerInitializer>();

            // 获取通知服务和TCP服务器
            var notificationService = serviceProvider.GetRequiredService<NotificationService>();
            var tcpServer = serviceProvider.GetRequiredService<TcpServer>();

            // 设置通知服务的服务器
            notificationService.SetServer(tcpServer);

            // 初始化服务器（自动注册处理器并启动 TcpServer）
            serverInitializer.Initialize();

            Console.WriteLine("服务器已启动，监听端口: 5000");
            Console.WriteLine("按 ESC 键退出，按 B 键发送全局广播，按 N 键发送系统通知");

            // 等待按键
            while (true)
            {
                var key = Console.ReadKey(true);

                if (key.Key == ConsoleKey.Escape)
                {
                    break;
                }
                else if (key.Key == ConsoleKey.B)
                {
                    // 发送全局广播
                    await notificationService.SendGlobalBroadcastAsync(
                        "这是一条服务器发送的全局广播消息！",
                        "服务器",
                        "#FF0000",
                        10000);

                    Console.WriteLine("已发送全局广播");
                }
                else if (key.Key == ConsoleKey.N)
                {
                    // 发送系统通知
                    await notificationService.SendSystemNotificationAsync(
                        "系统公告",
                        "服务器将于今日24:00进行例行维护，预计维护时间2小时。",
                        NotificationType.Maintenance,
                        DateTimeOffset.Now.AddHours(12).ToUnixTimeMilliseconds(),
                        new Dictionary<string, string> { { "MaintenanceId", "M2023120100" } });

                    Console.WriteLine("已发送系统通知");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"服务器运行出错: {ex.Message}");
        }
        finally
        {
            // 关闭服务器
            if (serviceProvider.GetService<TcpServer>() is TcpServer server)
            {
                await server.StopAsync();
                Console.WriteLine("服务器已停止");
            }
        }
    }

    /// <summary>
    /// 注册消息类型
    /// </summary>
    private static void RegisterMessageTypes()
    {
        // 注册请求和响应消息
        MessageRegistry.RegisterMessageType<LoginRequest>(1001);
        MessageRegistry.RegisterMessageType<LoginResponse>(1002);
        MessageRegistry.RegisterMessageType<RegisterRequest>(1003);
        MessageRegistry.RegisterMessageType<RegisterResponse>(1004);
        MessageRegistry.RegisterMessageType<GetUserInfoRequest>(1005);
        MessageRegistry.RegisterMessageType<GetUserInfoResponse>(1006);
        MessageRegistry.RegisterMessageType<UpdateUserInfoRequest>(1007);
        MessageRegistry.RegisterMessageType<UpdateUserInfoResponse>(1008);

        // 注册通知消息
        MessageRegistry.RegisterMessageType<SystemNotification>(2001);
        MessageRegistry.RegisterMessageType<UserStatusNotification>(2002);

        // 注册广播消息
        MessageRegistry.RegisterMessageType<GlobalBroadcast>(3001);
    }
}
