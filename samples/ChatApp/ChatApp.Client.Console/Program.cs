using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseRPC.Client;
using PulseRPC.Transport;
using ChatApp;

namespace ChatApp.Client.Console;

internal class Program
{
    private static async Task Main(string[] args)
    {
        System.Console.WriteLine("================================");
        System.Console.WriteLine("  PulseRPC ChatApp 控制台客户端");
        System.Console.WriteLine("================================");

        // 演示1: 使用DI版本的客户端（推荐用于服务端或控制台应用）
        await RunWithDependencyInjectionAsync();

        System.Console.WriteLine("\n按任意键继续演示非DI版本...");
        System.Console.ReadKey();

        // 演示2: 使用非DI版本的客户端（Unity兼容）
        await RunWithoutDependencyInjectionAsync();

        System.Console.WriteLine("\n按任意键退出...");
        System.Console.ReadKey();
    }

    /// <summary>
    /// 使用依赖注入版本的客户端API
    /// </summary>
    private static async Task RunWithDependencyInjectionAsync()
    {
        System.Console.WriteLine("\n=== 使用DI版本客户端 ===");

        var host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole();
                logging.SetMinimumLevel(LogLevel.Information);
            })
            .ConfigureServices(services =>
            {
                // 基于指南文档的客户端配置
                services.AddSingleton<IPulseRPCClient>(provider =>
                {
                    return new PulseRPCClientBuilder()
                        .ConfigureConnection("127.0.0.1", 7000)
                        .ConfigureTransport(TransportType.Tcp)
                        .ConfigureTransportOptions(options =>
                        {
                            options.ConnectTimeoutMs = 10000;
                            options.EnableTcpNoDelay = true;
                        })
                        .ConfigureLogging(logging =>
                        {
                            logging.AddConsole();
                            logging.SetMinimumLevel(LogLevel.Information);
                        })
                        .Build();
                });
            })
            .Build();

        var client = host.Services.GetRequiredService<IPulseRPCClient>();

        try
        {
            // 初始化并连接服务器
            await client.InitializeAsync();
            System.Console.WriteLine("✓ 已连接到服务器");

            // 获取服务代理
            var playerHub = await client.GetServiceAsync<IPlayerHub>();

            // 发送测试Ping
            var pingRequest = new PingRequest
            {
                Message = "来自DI版本客户端的问候！"
            };

            var pingResponse = await playerHub.PingAsync(pingRequest);
            System.Console.WriteLine($"✓ Ping响应: {pingResponse}");

            // 模拟登录
            var loginRequest = new LoginRequest
            {
                Username = "console_user",
                Password = "test123"
            };

            var loginResponse = await playerHub.LoginAsync(loginRequest);
            if (loginResponse.Success)
            {
                System.Console.WriteLine($"✓ 登录成功: {loginResponse.Player?.Username}");
            }
            else
            {
                System.Console.WriteLine($"✗ 登录失败: {loginResponse.ErrorMessage}");
            }

            // 停止客户端
            await client.StopAsync();
            System.Console.WriteLine("✓ 已断开连接");
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"❌ DI版本测试失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 使用非依赖注入版本的客户端API（Unity兼容）
    /// </summary>
    private static async Task RunWithoutDependencyInjectionAsync()
    {
        System.Console.WriteLine("\n=== 使用非DI版本客户端（Unity兼容）===");

        // 方式1: 基本客户端配置
        using var simpleClient = new PulseRPCClientBuilder()
            .ConfigureConnection("127.0.0.1", 7000)
            .ConfigureTransport(TransportType.Tcp)
            .Build();

        try
        {
            await simpleClient.InitializeAsync();
            System.Console.WriteLine("✓ 基本客户端连接成功");
            await simpleClient.StopAsync();
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"❌ 基本客户端测试失败: {ex.Message}");
        }

        // 方式2: 高级客户端配置
        using var complexClient = new PulseRPCClientBuilder()
            .ConfigureConnection("127.0.0.1", 7001)
            .ConfigureTransport(TransportType.Kcp)
            .ConfigureKcp(kcp =>
            {
                kcp.NoDelay = true;
                kcp.Interval = 10;
                kcp.Resend = 2;
                kcp.DisableFlowControl = false;
            })
            .ConfigureTransportOptions(options =>
            {
                options.ConnectTimeoutMs = 10000;
                options.ReadBufferSize = 16384;
                options.WriteBufferSize = 16384;
            })
            .ConfigureLogging(logging =>
            {
                logging.AddConsole();
                logging.SetMinimumLevel(LogLevel.Information);
            })
            .Build();

        try
        {
            // 初始化并连接服务器
            await complexClient.InitializeAsync();
            System.Console.WriteLine("✓ 高级配置客户端连接成功");

            // 获取服务代理
            var playerHub = await complexClient.GetServiceAsync<IPlayerHub>();

            // 发送测试Ping
            var pingRequest = new PingRequest
            {
                Message = "来自非DI版本客户端的问候！"
            };

            var pingResponse = await playerHub.PingAsync(pingRequest);
            System.Console.WriteLine($"✓ Ping响应: {pingResponse}");

            // 模拟登录
            var loginRequest = new LoginRequest
            {
                Username = "nondi_user",
                Password = "test123"
            };

            var loginResponse = await playerHub.LoginAsync(loginRequest);
            if (loginResponse.Success)
            {
                System.Console.WriteLine($"✓ 登录成功: {loginResponse.Player?.Username}");
            }
            else
            {
                System.Console.WriteLine($"✗ 登录失败: {loginResponse.ErrorMessage}");
            }

            // 显示连接信息
            System.Console.WriteLine("✓ 使用 KCP 传输协议连接成功");

            // 停止客户端
            await complexClient.StopAsync();
            System.Console.WriteLine("✓ 高级配置客户端已断开连接");
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"❌ 高级配置客户端测试失败: {ex.Message}");
        }
    }
}
