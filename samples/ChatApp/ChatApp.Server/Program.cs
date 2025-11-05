using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseRPC.Server;
using GameServer.World;
using System;
using System.Threading;
using System.Threading.Tasks;
using ChatApp;
using PulseRPC.Server.Authentication;
using PulseRPC.Server.Extensions;
using PulseRPC.Server.Models;
using PulseRPC.Transport;

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
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            // 如果是交互式控制台，等待ESC键；否则等待Ctrl+C
            if (!Console.IsInputRedirected)
            {
                while (!cts.Token.IsCancellationRequested)
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
            else
            {
                Console.WriteLine("在后台模式下运行，按 Ctrl+C 停止服务器...");
                await Task.Delay(Timeout.Infinite, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消
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

            Console.WriteLine("\n服务器已停止。");
        }
    }

    private static void ConfigureServices(HostBuilderContext context, IServiceCollection services)
    {
        // 添加PulseRPC服务器 - 基于指南文档的最佳实践
        // 必须先添加，因为它会注册核心服务（IEventPublisher等）
        services.AddUnifiedPulseServer(options =>
        {
            options.Transports = new()
            {
                new TransportChannelConfiguration()
                {
                    Name = "TCP",
                    Type = TransportType.TCP,
                    Port = 7000,
                    IsDefault = true
                },
                new TransportChannelConfiguration()
                {
                    Name = "KCP",
                    Type = TransportType.KCP,
                    Port = 7001,
                    IsDefault = false
                }
            };
        });

        // ========================================
        // 认证和授权服务配置
        // ========================================

        // 注册 IJwtTokenService（AuthenticationService 的依赖）
        // 使用 PulseRPC.Server 中的简化实现
        services.AddSingleton<PulseRPC.Server.IJwtTokenService, PulseRPC.Server.JwtTokenService>();

        // 手动注册 IAuthenticationService（AddUnifiedPulseServer 未注册它）
        services.AddSingleton<IAuthenticationService, AuthenticationService>();

        // 添加权限验证器（ChatRoomManager 依赖）
        services.AddSingleton<PermissionValidator>();

        // ========================================
        // 业务服务配置
        // ========================================

        // 添加游戏世界（依赖 IEventPublisher，所以放在 AddUnifiedPulseServer 之后）
        services.AddSingleton<IGameWorld, GameWorld>();
        services.AddSingleton<IPlayerManager, PlayerManager>();

        // 添加聊天室管理器（服务隔离架构，依赖 IAuthenticationService 和 PermissionValidator）
        services.AddSingleton<ChatRoomManager>();

        // ========================================
        // Hub 服务注册
        // ========================================

        // 注册 Hub 服务（这些服务会被源代码生成器自动发现和路由）
        services.AddSingleton<IPlayerHub, PlayerHub>();
        services.AddSingleton<IChatHub, ChatHub>();

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
    }
}
