using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PulseRPC.Protocol;
using PulseRPC.Protocol.Attributes;
using PulseRPC.Samples.Server.Handlers;
using PulseRPC.Samples.Shared;
using PulseRPC.Samples.Shared.Messages;
using PulseRPC.Server;

namespace PulseRPC.Samples.Server;

/// <summary>
/// 服务器示例程序
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("PulseRPC 服务器示例");
        Console.WriteLine("====================");

        // 创建服务容器
        var services = new ServiceCollection();

        // 添加日志
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        // 注册消息处理器
        services.AddTransient<LoginRequestHandler>();
        services.AddTransient<RegisterRequestHandler>();
        services.AddTransient<GetUserInfoRequestHandler>();
        services.AddTransient<UpdateUserInfoRequestHandler>();

        // 注册通知服务
        services.AddSingleton<NotificationService>();

        // 构建服务提供程序
        var serviceProvider = services.BuildServiceProvider();

        // 创建消息分发器
        var dispatcher = new MessageDispatcher(
            serviceProvider,
            serviceProvider.GetRequiredService<ILogger<MessageDispatcher>>());

        // 预热处理器实例
        dispatcher.InitializeHandlers();

        // 手动注册消息处理器
        RegisterMessageHandlers(dispatcher);

        // 创建TCP服务器
        var server = new TcpServer(
            "127.0.0.1",
            5000,
            dispatcher,
            serviceProvider.GetRequiredService<ILogger<TcpServer>>());

        // 注册通知服务
        serviceProvider.GetRequiredService<NotificationService>().SetServer(server);

        // 启动服务器
        var serverTask = server.StartAsync();

        Console.WriteLine("服务器已启动，监听端口：5000");
        Console.WriteLine("按 ESC 键退出，按 B 键发送全局广播，按 N 键发送系统通知");

        // 获取通知服务
        var notificationService = serviceProvider.GetRequiredService<NotificationService>();

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

            await Task.Delay(100);
        }

        // 停止服务器
        await server.StopAsync();

        Console.WriteLine("服务器已停止");
    }

    /// <summary>
    /// 手动注册消息处理器
    /// </summary>
    /// <param name="dispatcher">消息分发器</param>
    private static void RegisterMessageHandlers(MessageDispatcher dispatcher)
    {
        // 注意：在Roslyn源代码生成器实现中，这部分会被自动生成

        // 注册LoginRequest处理器
        dispatcher.RegisterHandler<LoginRequest, LoginRequestHandler>();

        // 注册RegisterRequest处理器
        dispatcher.RegisterHandler<RegisterRequest, RegisterRequestHandler>();

        // 注册GetUserInfoRequest处理器
        dispatcher.RegisterHandler<GetUserInfoRequest, GetUserInfoRequestHandler>();

        // 注册UpdateUserInfoRequest处理器
        dispatcher.RegisterHandler<UpdateUserInfoRequest, UpdateUserInfoRequestHandler>();
    }
}

/// <summary>
/// MessageDispatcher扩展方法
/// </summary>
public static class MessageDispatcherExtensions
{
    /// <summary>
    /// 注册处理器
    /// </summary>
    /// <typeparam name="TMessage">消息类型</typeparam>
    /// <typeparam name="THandler">处理器类型</typeparam>
    /// <param name="dispatcher">消息分发器</param>
    public static void RegisterHandler<TMessage, THandler>(this MessageDispatcher dispatcher)
        where TMessage : class, IMessage
        where THandler : class
    {
        // 获取消息特性
        var messageAttribute = typeof(TMessage).GetCustomAttributes(false)
            .OfType<MessageAttribute>()
            .FirstOrDefault();

        if (messageAttribute == null)
        {
            throw new InvalidOperationException($"消息类型 {typeof(TMessage).Name} 未标记 MessageAttribute");
        }

        // 注册处理器
        dispatcher.RegisterHandlerType(messageAttribute.Id, typeof(THandler));
    }
}
