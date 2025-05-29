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
using PulseRPC.Server.Authentication;
using PulseRPC.Server.Services;

namespace GameServer
{
    class Program
    {
        static async Task Main(string[] args)
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

            // 检查命令行参数
            // bool runPerfTest = Array.Exists(args, arg => arg.Equals("--perf-test", StringComparison.OrdinalIgnoreCase));
            // bool runServiceTest = Array.Exists(args, arg => arg.Equals("--service-test", StringComparison.OrdinalIgnoreCase));

            // if (runPerfTest)
            // {
            //     // 运行性能测试
            //     // await RunPerformanceTests(host.Services);
            //     return;
            // }
            // else if (runServiceTest)
            // {
            //     // 运行服务测试
            //     await RunServiceTests(host.Services);
            //     return;
            // }

            // 获取服务器管理器
            var serverManager = host.Services.GetRequiredService<IServerManager>();

            try
            {
                // 启动服务器
                await serverManager.StartAsync();

                // 注册服务到ServiceRegistry（必须在服务器启动后）
                var serviceRegistry = host.Services.GetRequiredService<ServiceRegistry>();
                var playerService = host.Services.GetRequiredService<IPlayerService>();
                serviceRegistry.RegisterService<IPlayerService, PlayerService>((PlayerService)playerService);
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

        /// <summary>
        /// 运行性能测试
        /// </summary>
        // private static async Task RunPerformanceTests(IServiceProvider serviceProvider)
        // {
        //     Console.WriteLine("正在启动性能测试...");
        //
        //     try
        //     {
        //         var tester = serviceProvider.GetRequiredService<PerformanceTester>();
        //         await tester.RunAsync();
        //
        //         Console.WriteLine("性能测试完成，按任意键退出...");
        //         Console.ReadKey(true);
        //     }
        //     catch (Exception ex)
        //     {
        //         Console.ForegroundColor = ConsoleColor.Red;
        //         Console.WriteLine($"性能测试运行失败: {ex.Message}");
        //         Console.ResetColor();
        //         Console.WriteLine(ex.StackTrace);
        //         Console.WriteLine("按任意键退出...");
        //         Console.ReadKey(true);
        //     }
        // }

        /// <summary>
        /// 运行服务测试
        /// </summary>
        // private static async Task RunServiceTests(IServiceProvider serviceProvider)
        // {
        //     Console.WriteLine("正在启动服务测试...");
        //
        //     try
        //     {
        //         // 获取日志记录器
        //         var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        //         var logger = loggerFactory.CreateLogger("ServiceTest");
        //
        //         logger.LogInformation("正在初始化测试环境...");
        //
        //         // 先检查服务是否已注册
        //         try {
        //             var registry = serviceProvider.GetRequiredService<ServiceRegistry>();
        //             logger.LogInformation("成功获取HighPerformanceServiceRegistry");
        //         }
        //         catch (Exception ex) {
        //             logger.LogError(ex, "获取HighPerformanceServiceRegistry失败");
        //             throw;
        //         }
        //
        //         try {
        //             var playerService = serviceProvider.GetRequiredService<IPlayerService>();
        //             logger.LogInformation("成功获取IPlayerService");
        //         }
        //         catch (Exception ex) {
        //             logger.LogError(ex, "获取IPlayerService失败");
        //             throw;
        //         }
        //
        //         logger.LogInformation("尝试创建PlayerServiceTests实例...");
        //
        //         // var tester = serviceProvider.GetRequiredService<PlayerServiceTests>();
        //         // logger.LogInformation("PlayerServiceTests实例创建成功");
        //
        //         // 运行登录流程测试
        //         await tester.TestLoginAsync();
        //
        //         // 运行移动流程测试
        //         await tester.TestMoveAsync();
        //
        //         // 运行基准测试 (使用较小的迭代次数)
        //         await tester.RunBenchmarkAsync(iterations: 1000);
        //
        //         Console.WriteLine("服务测试完成，按任意键退出...");
        //         Console.ReadKey(true);
        //     }
        //     catch (Exception ex)
        //     {
        //         Console.ForegroundColor = ConsoleColor.Red;
        //         Console.WriteLine($"服务测试运行失败: {ex.Message}");
        //         Console.ResetColor();
        //
        //         // 输出详细的异常信息
        //         Console.WriteLine("详细错误:");
        //         Console.WriteLine(ex.ToString());
        //
        //         if (ex.InnerException != null)
        //         {
        //             Console.WriteLine("内部异常:");
        //             Console.WriteLine(ex.InnerException.ToString());
        //         }
        //
        //         Console.WriteLine("按任意键退出...");
        //         Console.ReadKey(true);
        //     }
        // }

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
            services.AddTransient<IPlayerService, PlayerService>();

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
                var serverManager = new ServerManager(
                    serverChannelManager,
                    loggerFactory);

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
