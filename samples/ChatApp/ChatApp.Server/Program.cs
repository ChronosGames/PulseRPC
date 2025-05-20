// GameServer/Program.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseRPC.Serialization;
using PulseRPC.Transport;
using PulseRPC.Server;
using GameServer.Services;
using GameServer.World;
using System;
using System.Threading.Tasks;
using ChatApp.Shared;

namespace GameServer
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=================================");
            Console.WriteLine("      PulseRPC 游戏服务器");
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

                Console.WriteLine("\n服务器已启动，按 ESC 键停止服务器...\n");

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

            // 添加序列化器
            services.AddSingleton<ISerializer, PulseRPCSerializer>();

            // 添加事件发布器
            services.AddSingleton<IEventPublisher, EventPublisher>();

            // 添加服务
            services.AddSingleton<IServerChannelManager, ServerChannelManager>();

            // 添加服务实现
            services.AddTransient<IPlayerService, PlayerService>();

            // 添加位置更新批处理器
            services.AddSingleton<PlayerMovementBatcher>();
            services.AddHostedService(sp => sp.GetRequiredService<PlayerMovementBatcher>());

            // 添加服务注册中心
            services.AddSingleton<ServiceRegistry>(sp =>
            {
                var registry = new ServiceRegistry();

                // 注册服务
                registry.RegisterService<IPlayerService, PlayerService>(
                    (PlayerService)sp.GetRequiredService<IPlayerService>());

                return registry;
            });

            // 添加服务器管理器
            services.AddSingleton<IServerManager>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<ServerManager>>();
                var serializer = sp.GetRequiredService<ISerializer>();
                var registry = sp.GetRequiredService<ServiceRegistry>();

                // 创建服务器管理器
                var serverManager = new ServerManager(registry, serializer, logger);

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
}
