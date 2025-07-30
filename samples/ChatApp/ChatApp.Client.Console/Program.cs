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
                // 使用新的客户端API - 配置双通道
                services.AddPulseRpcClient(client =>
                {
                    client.ConfigureClient(options =>
                    {
                        // 使用现有的ClientOptions属性
                        options.ConnectionTimeout = TimeSpan.FromSeconds(10);
                        options.AutoReconnect = true;
                    })
                    // TCP通道用于可靠通信
                    .AddTcp("reliable", "localhost", 7000, options =>
                    {
                        options.NoDelay = true;
                    }, isDefault: true)
                    // KCP通道用于低延迟通信
                    .AddKcp("gaming", "localhost", 7001, options =>
                    {
                        options.Kcp = new KcpOptions
                        {
                            NoDelay = 1,
                            Interval = 10,
                            Resend = 2,
                            DisableFlowControl = true
                        };
                    });
                });
            })
            .Build();

        var client = host.Services.GetRequiredService<IPulseClient>();

        try
        {
            // 连接服务器
            await client.ConnectAsync();
            System.Console.WriteLine("✓ 已连接到服务器");

            // 获取服务代理
            var playerHub = client.GetService<IPlayerHub>();

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

            // 断开连接
            await client.DisconnectAsync();
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

        // 使用工厂方法创建客户端 - 方式1: 简单创建
        using var simpleClient = PulseRpcClientFactory.CreateTcpClient("TcpChannel", "localhost", 7000);

        try
        {
            await simpleClient.ConnectAsync();
            System.Console.WriteLine("✓ 简单TCP客户端连接成功");
            await simpleClient.DisconnectAsync();
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"❌ 简单客户端测试失败: {ex.Message}");
        }

        // 方式2: 使用构建器创建复杂配置
        using var complexClient = PulseRpcClientFactory.CreateBuilder()
            .WithLogger(LoggerFactory.Create(builder =>
            {
                builder.AddConsole().SetMinimumLevel(LogLevel.Information);
            }))
            .WithOptions(options =>
            {
                // 使用现有的ClientOptions属性
                options.ConnectionTimeout = TimeSpan.FromSeconds(10);
                options.AutoReconnect = true;
            })
            .AddTcp("reliable", "localhost", 7000, options =>
            {
                options.NoDelay = true;
            }, isDefault: true)
            .AddKcp("gaming", "localhost", 7001, options =>
            {
                options.Kcp = new KcpOptions
                {
                    NoDelay = 1,
                    Interval = 10,
                    Resend = 2,
                    DisableFlowControl = true
                };
            })
            .Build();

        try
        {
            // 连接服务器
            await complexClient.ConnectAsync();
            System.Console.WriteLine("✓ 复杂配置客户端连接成功");

            // 获取服务代理
            var playerHub = complexClient.GetService<IPlayerHub>();

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

            // 显示传输信息
            var transports = complexClient.GetTransports();
            System.Console.WriteLine("✓ 配置的传输通道:");
            foreach (var (name, info) in transports)
            {
                System.Console.WriteLine($"  - {name}: {info.Type} @ {info.Host}:{info.Port} (默认: {info.IsDefault})");
            }

            // 断开连接
            await complexClient.DisconnectAsync();
            System.Console.WriteLine("✓ 复杂配置客户端已断开连接");
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"❌ 复杂配置客户端测试失败: {ex.Message}");
        }
    }
}
