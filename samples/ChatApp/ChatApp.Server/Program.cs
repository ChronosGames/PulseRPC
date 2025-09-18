using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseRPC.Server;
using GameServer.World;
using System;
using System.Threading.Tasks;
using ChatApp;
using PulseRPC.Server.Authentication;
using PulseRPC.Server.Engine;

namespace GameServer;

internal abstract class Program
{
    private static async Task Main(string[] args)
    {
        Console.WriteLine("=================================");
        Console.WriteLine("  PulseRPC 高性能游戏服务器 v2.0");
        Console.WriteLine("=================================");

        // 创建主机
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole();
                logging.SetMinimumLevel(LogLevel.Information);
            })
            .ConfigureServices(ConfigureServices)
            .Build();

        // 获取服务器实例
        var server = host.Services.GetRequiredService<IPulseServer>();

        try
        {
            // 启动服务器
            await server.StartAsync();

            Console.WriteLine("\n高性能服务器已启动，按 ESC 键停止服务器...\n");

            // 等待退出
            while (true)
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true);
                    if (key.Key == ConsoleKey.Escape)
                        break;
                }

                await Task.Delay(100);
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"服务器启动失败: {ex.Message}");
            Console.ResetColor();
        }
        finally
        {
            // 停止服务器
            await server.StopAsync();

            Console.WriteLine("\n服务器已停止。按任意键退出...");
            Console.ReadKey(true);
        }
    }

    private static void ConfigureServices(HostBuilderContext context, IServiceCollection services)
    {
        // 添加游戏世界
        services.AddSingleton<IGameWorld, GameWorld>();
        services.AddSingleton<IPlayerManager, PlayerManager>();

        // 添加PulseRPC服务器 - 基于指南文档的最佳实践
        services.AddPulseServer(builder =>
        {
            // TCP 监听器配置
            builder.AddTcp("TcpChannel", 7000, isDefault: true);

            // KCP 监听器配置（低延迟）
            builder.AddKcp("KcpChannel", 7001);
        });

        // 注册 Hub 服务
        services.AddScoped<IPlayerHub, PlayerHub>();

        // 添加服务注册
        // services.AddPulseRpcServiceRegistration(context.Configuration);

        // 添加性能监控
        // services.AddPulseRpcMonitoring(options =>
        // {
        //     options.Performance.Enabled = true;
        //     options.Performance.SamplingInterval = TimeSpan.FromSeconds(10);
        // });

        // 添加链路追踪
        // services.AddPulseRpcTracing(options =>
        // {
        //     options.Enabled = true;
        //     options.ServiceName = "UserService";
        //     options.SamplingRate = 1.0; // 100%采样用于演示
        //     options.Exporter.Type = TracingExporterType.Console;
        // });

        // 覆盖默认的认证提供程序，使用ChatApp专用的SimpleAuthenticationProvider
        services.AddSingleton<IAuthenticationProvider, SimpleAuthenticationProvider>();

        // 添加位置更新批处理器
        services.AddSingleton<PlayerMovementBatcher>();
        services.AddHostedService(sp => sp.GetRequiredService<PlayerMovementBatcher>());

        // 在服务器启动后注册服务到ServiceRegistry
        services.Configure<HostOptions>(options =>
        {
            options.ServicesStartConcurrently = false;
            options.ServicesStopConcurrently = false;
        });

        // 使用编译时生成的消息调度器以获得最佳性能
        services.AddSingleton<IMessageDispatcher, CompiledMessageDispatcher>();
    }
}
