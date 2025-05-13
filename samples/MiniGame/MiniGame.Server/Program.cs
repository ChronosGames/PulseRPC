using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseRPC.Samples.Server.Services;
using PulseRPC.Samples.Shared;
using PulseRPC.Server;

namespace PulseRPC.Samples.Server;

/// <summary>
/// 迷你游戏服务器示例程序
/// </summary>
internal abstract class Program
{
    private static async Task Main(string[] args)
    {
        Console.WriteLine("PulseRPC 迷你游戏服务器示例");
        Console.WriteLine("=============================");

        // 创建托管应用程序
        var host = CreateHostBuilder(args).Build();

        // 启动服务
        await host.StartAsync();

        Console.WriteLine("服务器已启动，监听端口: 5000");
        Console.WriteLine("按 ESC 键退出，按 B 键发送全局广播，按 N 键发送系统通知");

        // 获取通知服务
        var notificationService = host.Services.GetRequiredService<NotificationService>();

        // 等待按键
        var keepRunning = true;
        while (keepRunning)
        {
            var key = Console.ReadKey(true);

            switch (key.Key)
            {
                case ConsoleKey.Escape:
                    keepRunning = false;
                    break;

                case ConsoleKey.B:
                    await SendGlobalBroadcastAsync(notificationService);
                    break;

                case ConsoleKey.N:
                    await SendSystemNotificationAsync(notificationService);
                    break;
            }
        }

        // 停止服务
        await host.StopAsync();
        Console.WriteLine("服务器已停止");
    }

    /// <summary>
    /// 创建托管服务构建器
    /// </summary>
    private static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureServices((hostContext, services) =>
            {
                // 添加日志
                services.AddLogging(builder =>
                {
                    builder.AddConsole();
                    builder.SetMinimumLevel(LogLevel.Debug);
                });

                // 注册核心服务
                services.AddSingleton<NotificationService>();

                // 添加PulseRPC服务器
                services.AddSingleton<NetworkServer>();

                // 手动注册各种消息处理器
                services.AddPulseServerServices();

                // 添加游戏服务器应用程序生命周期服务
                services.AddSingleton<IHostedService, GameServerLifetimeService>();
            });

    /// <summary>
    /// 发送全局广播
    /// </summary>
    private static async Task SendGlobalBroadcastAsync(NotificationService notificationService)
    {
        await notificationService.SendGlobalBroadcastAsync(
            "这是一条服务器发送的全局广播消息！",
            "服务器",
            "#FF0000",
            10000);

        Console.WriteLine("已发送全局广播");
    }

    /// <summary>
    /// 发送系统通知
    /// </summary>
    private static async Task SendSystemNotificationAsync(NotificationService notificationService)
    {
        await notificationService.SendSystemNotificationAsync(
            "系统公告",
            "服务器将于今日24:00进行例行维护，预计维护时间2小时。",
            NotificationType.Maintenance,
            DateTimeOffset.Now.AddHours(12).ToUnixTimeMilliseconds(),
            new Dictionary<string, string> { { "MaintenanceId", "M2023120100" } });

        Console.WriteLine("已发送系统通知");
    }
}
