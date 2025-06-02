using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseRPC.Transport;
using PulseRPC.Server;
using GameServer.World;
using System;
using System.Threading.Tasks;
using ChatApp;
using PulseRPC.Server.Authentication;
using PulseRPC.Server.Services;

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

        // 获取服务器管理器
        var serverManager = host.Services.GetRequiredService<IServerManager>();

        try
        {
            // 启动服务器
            await serverManager.StartAsync();

            // 注册服务到ServiceRegistry（必须在服务器启动后）
            var serviceRegistry = host.Services.GetRequiredService<ServiceRegistry>();
            var playerService = host.Services.GetRequiredService<IPlayerHub>();
            serviceRegistry.RegisterService<IPlayerHub, PlayerHub>((PlayerHub)playerService);
            Console.WriteLine("服务已注册到ServiceRegistry");

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
            await serverManager.StopAsync();

            Console.WriteLine("\n服务器已停止。按任意键退出...");
            Console.ReadKey(true);
        }
    }

    private static void ConfigureServices(HostBuilderContext context, IServiceCollection services)
    {
        // 添加游戏世界
        services.AddSingleton<IGameWorld, GameWorld>();
        services.AddSingleton<IPlayerManager, PlayerManager>();

        // 添加PulseRPC服务器的相关服务
        services.AddPulseRpcServer();

        // 覆盖默认的认证提供程序，使用ChatApp专用的SimpleAuthenticationProvider
        services.AddSingleton<IAuthenticationProvider, SimpleAuthenticationProvider>();

        // 添加服务实现
        services.AddTransient<IPlayerHub, PlayerHub>();

        // 添加位置更新批处理器
        services.AddSingleton<PlayerMovementBatcher>();
        services.AddHostedService(sp => sp.GetRequiredService<PlayerMovementBatcher>());

        // 在服务器启动后注册服务到ServiceRegistry
        services.Configure<HostOptions>(options =>
        {
            options.ServicesStartConcurrently = false;
            options.ServicesStopConcurrently = false;
        });

        // 添加服务器管理器
        services.AddSingleton<IServerManager>(sp =>
        {
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var serverChannelManager = sp.GetRequiredService<PulseRPC.Server.Transport.IServerChannelManager>();

            // 创建服务器管理器
            var serverManager = new ServerManager(serverChannelManager, loggerFactory);

            // 添加TCP传输 (端口7000)
            serverManager.AddTransport(
                "TcpChannel",
                TransportType.Tcp,
                7000,
                new TransportOptions { NoDelay = true },
                true);

            // 添加KCP传输 (端口7001)
            serverManager.AddTransport(
                "KcpChannel",
                TransportType.Kcp,
                7001,
                new TransportOptions
                {
                    Kcp = new KcpOptions
                    {
                        NoDelay = 1,
                        Interval = 10,
                        Resend = 2
                    }
                });

            return serverManager;
        });
    }
}
