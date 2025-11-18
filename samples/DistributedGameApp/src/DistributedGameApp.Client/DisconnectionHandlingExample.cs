using System;
using System.Threading;
using System.Threading.Tasks;
using DistributedGameApp.Shared.Messages;
using Microsoft.Extensions.Logging;

namespace DistributedGameApp.Client;

/// <summary>
/// 断线处理示例
/// </summary>
/// <remarks>
/// 演示如何正确处理服务器断线情况，包括：
/// - 自动检测断线
/// - 自动重连
/// - 状态恢复
/// - 用户通知
/// </remarks>
public class DisconnectionHandlingExample
{
    /// <summary>
    /// 基础断线重连示例
    /// </summary>
    public static async Task RunBasicReconnectionExampleAsync()
    {
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        using var client = new GameClient("http://localhost:5000", loggerFactory);
        using var reconnectionManager = new ReconnectionManager(
            client,
            loggerFactory.CreateLogger<ReconnectionManager>(),
            ReconnectionStrategy.ExponentialBackoff());

        using var stateMonitor = new ConnectionStateMonitor(
            loggerFactory.CreateLogger<ConnectionStateMonitor>());

        Console.WriteLine("=== 基础断线重连示例 ===\n");

        try
        {
            // 1. 配置重连事件处理
            ConfigureReconnectionEvents(reconnectionManager, stateMonitor);

            // 2. 登录
            Console.WriteLine("正在登录...");
            await client.LoginAsync("testuser", "password123");
            Console.WriteLine("登录成功\n");

            // 3. 连接到游戏服务器
            Console.WriteLine("正在连接到游戏服务器...");
            var server = await client.GetRecommendedGameServerAsync();
            if (server != null)
            {
                await client.ConnectToGameServerAsync(server);
                Console.WriteLine($"已连接到: {server.ServerName}\n");

                // 4. 保存状态（用于重连）
                reconnectionManager.SaveCurrentState();

                // 5. 启动连接状态监控
                if (client.CurrentConnection?.Channel != null)
                {
                    // 获取 HeartbeatAsync 方法的委托
                    async Task<long> HeartbeatFunc()
                    {
                        if (client.CurrentConnection?.GameHub != null)
                        {
                            return await client.CurrentConnection.GameHub.HeartbeatAsync();
                        }
                        throw new InvalidOperationException("GameHub not available");
                    }

                    stateMonitor.StartMonitoring(
                        client.CurrentConnection.Channel,
                        HeartbeatFunc);

                    Console.WriteLine("连接状态监控已启动\n");
                }
            }

            // 6. 进行一些游戏操作
            Console.WriteLine("创建角色...");
            await client.CreateCharacterAsync("TestHero", CharacterClass.Warrior, Gender.Male);
            var characters = await client.GetCharacterListAsync();
            if (characters.Count > 0)
            {
                await client.SelectCharacterAsync(characters[0].CharacterId);
            }

            Console.WriteLine("\n客户端已准备好。");
            Console.WriteLine("提示：您可以手动停止服务器来测试断线重连功能。");
            Console.WriteLine("按 'q' 退出，或按任意键模拟断线...\n");

            var key = Console.ReadKey(true);
            if (key.KeyChar == 'q')
            {
                return;
            }

            // 7. 模拟断线（实际场景中这会自动检测）
            Console.WriteLine("\n模拟断线情况...");
            await reconnectionManager.HandleDisconnectionAsync("Manual disconnection test");

            // 8. 等待一段时间观察重连过程
            Console.WriteLine("\n等待重连完成（最多60秒）...");
            await Task.Delay(TimeSpan.FromSeconds(60));

            Console.WriteLine("\n示例完成。");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n发生错误: {ex.Message}");
        }
    }

    /// <summary>
    /// 高级断线处理示例 - 带状态恢复
    /// </summary>
    public static async Task RunAdvancedReconnectionExampleAsync()
    {
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        using var client = new GameClient("http://localhost:5000", loggerFactory);
        using var reconnectionManager = new ReconnectionManager(
            client,
            loggerFactory.CreateLogger<ReconnectionManager>(),
            ReconnectionStrategy.Progressive());

        using var stateMonitor = new ConnectionStateMonitor(
            loggerFactory.CreateLogger<ConnectionStateMonitor>());

        Console.WriteLine("=== 高级断线处理示例 ===\n");

        try
        {
            // 1. 配置事件处理
            ConfigureAdvancedEvents(reconnectionManager, stateMonitor, client);

            // 2. 登录和连接
            await client.LoginAsync("testuser", "password123");

            var server = await client.GetRecommendedGameServerAsync();
            if (server != null)
            {
                await client.ConnectToGameServerAsync(server);
                reconnectionManager.SaveCurrentState();

                if (client.CurrentConnection?.Channel != null)
                {
                    async Task<long> HeartbeatFunc()
                    {
                        return await client.CurrentConnection!.GameHub!.HeartbeatAsync();
                    }

                    stateMonitor.StartMonitoring(
                        client.CurrentConnection.Channel,
                        HeartbeatFunc);
                }
            }

            // 3. 创建并选择角色
            var characters = await client.GetCharacterListAsync();
            if (characters.Count == 0)
            {
                await client.CreateCharacterAsync("AdvancedHero", CharacterClass.Mage, Gender.Female);
                characters = await client.GetCharacterListAsync();
            }

            if (characters.Count > 0)
            {
                await client.SelectCharacterAsync(characters[0].CharacterId);
                reconnectionManager.SaveCurrentState(); // 更新保存的状态
            }

            Console.WriteLine("\n客户端已准备好，可以测试各种断线场景。");
            Console.WriteLine("按任意键继续...\n");
            Console.ReadKey(true);

            // 4. 测试不同的断线场景
            await TestDisconnectionScenariosAsync(client, reconnectionManager, stateMonitor);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n发生错误: {ex.Message}");
        }
    }

    /// <summary>
    /// 手动重连示例
    /// </summary>
    public static async Task RunManualReconnectionExampleAsync()
    {
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        using var client = new GameClient("http://localhost:5000", loggerFactory);
        using var reconnectionManager = new ReconnectionManager(
            client,
            loggerFactory.CreateLogger<ReconnectionManager>());

        Console.WriteLine("=== 手动重连示例 ===\n");

        // 禁用自动重连
        reconnectionManager.AutoReconnectEnabled = false;
        Console.WriteLine("自动重连已禁用\n");

        try
        {
            // 登录和连接
            await client.LoginAsync("testuser", "password123");

            var server = await client.GetRecommendedGameServerAsync();
            if (server != null)
            {
                await client.ConnectToGameServerAsync(server);
                reconnectionManager.SaveCurrentState();
            }

            Console.WriteLine("连接成功。");
            Console.WriteLine("按任意键模拟断线...\n");
            Console.ReadKey(true);

            // 模拟断线
            Console.WriteLine("模拟断线...");
            await reconnectionManager.HandleDisconnectionAsync("Manual test");

            Console.WriteLine("\n由于自动重连已禁用，请手动触发重连。");
            Console.WriteLine("按 'r' 重连，按 'q' 退出：");

            while (true)
            {
                var key = Console.ReadKey(true);

                if (key.KeyChar == 'r')
                {
                    Console.WriteLine("\n开始手动重连...");
                    var success = await reconnectionManager.ReconnectAsync();

                    if (success)
                    {
                        Console.WriteLine("重连成功！");
                        break;
                    }
                    else
                    {
                        Console.WriteLine("重连失败，请重试。");
                        Console.WriteLine("按 'r' 重连，按 'q' 退出：");
                    }
                }
                else if (key.KeyChar == 'q')
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n发生错误: {ex.Message}");
        }
    }

    /// <summary>
    /// 配置重连事件
    /// </summary>
    private static void ConfigureReconnectionEvents(
        ReconnectionManager reconnectionManager,
        ConnectionStateMonitor stateMonitor)
    {
        // 重连管理器事件
        reconnectionManager.OnReconnecting += (sender, args) =>
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\n[{args.Timestamp:HH:mm:ss}] 开始重连... (原因: {args.Reason})");
            Console.ResetColor();
        };

        reconnectionManager.OnReconnectionProgress += (sender, args) =>
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[{args.Timestamp:HH:mm:ss}] 重连尝试 {args.CurrentAttempt}/{args.MaxAttempts}");
            Console.ResetColor();
        };

        reconnectionManager.OnReconnected += (sender, args) =>
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n[{args.Timestamp:HH:mm:ss}] ✓ 重连成功!");
            Console.ResetColor();
        };

        reconnectionManager.OnReconnectionFailed += (sender, args) =>
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[{args.Timestamp:HH:mm:ss}] ✗ 重连失败: {args.Reason}");
            Console.ResetColor();
        };

        // 状态监控器事件
        stateMonitor.OnDisconnected += async (sender, args) =>
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[{args.Timestamp:HH:mm:ss}] ● 检测到断线: {args.Reason}");
            Console.WriteLine($"    最后活动时间: {args.LastSuccessfulActivity:HH:mm:ss}");
            Console.ResetColor();

            // 触发重连
            if (reconnectionManager.AutoReconnectEnabled)
            {
                await reconnectionManager.ReconnectAsync();
            }
        };

        stateMonitor.OnReconnected += (sender, args) =>
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n● 连接已恢复");
            Console.ResetColor();
        };

        stateMonitor.OnHeartbeatSuccess += (sender, args) =>
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ♥ 心跳成功 (延迟: {args.Latency.TotalMilliseconds:F0}ms)");
            Console.ResetColor();
        };

        stateMonitor.OnHeartbeatFailure += (sender, args) =>
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\n[{DateTime.Now:HH:mm:ss}] ♥ 心跳失败: {args.ErrorMessage}");
            Console.ResetColor();
        };
    }

    /// <summary>
    /// 配置高级事件处理
    /// </summary>
    private static void ConfigureAdvancedEvents(
        ReconnectionManager reconnectionManager,
        ConnectionStateMonitor stateMonitor,
        GameClient client)
    {
        ConfigureReconnectionEvents(reconnectionManager, stateMonitor);

        // 添加连接状态变化处理
        stateMonitor.OnStateChanged += (sender, args) =>
        {
            var stateColor = args.NewState switch
            {
                ConnectionState.Connected => ConsoleColor.Green,
                ConnectionState.Connecting => ConsoleColor.Yellow,
                ConnectionState.Reconnecting => ConsoleColor.Yellow,
                ConnectionState.Disconnected => ConsoleColor.Red,
                _ => ConsoleColor.White
            };

            Console.ForegroundColor = stateColor;
            Console.WriteLine($"\n[状态] {args.OldState} -> {args.NewState}");
            Console.ResetColor();
        };

        // 显示重连统计
        reconnectionManager.OnReconnected += (sender, args) =>
        {
            var stats = reconnectionManager.Statistics;
            Console.WriteLine($"\n[统计] 总断线: {stats.TotalDisconnections}, " +
                            $"重连成功: {stats.SuccessfulReconnections}, " +
                            $"重连失败: {stats.FailedReconnections}, " +
                            $"成功率: {stats.SuccessRate:P0}");
        };
    }

    /// <summary>
    /// 测试不同的断线场景
    /// </summary>
    private static async Task TestDisconnectionScenariosAsync(
        GameClient client,
        ReconnectionManager reconnectionManager,
        ConnectionStateMonitor stateMonitor)
    {
        Console.WriteLine("\n=== 测试断线场景 ===\n");

        // 场景 1: 短暂断线
        Console.WriteLine("场景 1: 短暂断线（模拟网络抖动）");
        Console.WriteLine("按任意键开始...");
        Console.ReadKey(true);

        await reconnectionManager.HandleDisconnectionAsync("Network jitter");
        await Task.Delay(TimeSpan.FromSeconds(5));

        // 场景 2: 中等时长断线
        Console.WriteLine("\n场景 2: 中等时长断线");
        Console.WriteLine("按任意键开始...");
        Console.ReadKey(true);

        await reconnectionManager.HandleDisconnectionAsync("Connection timeout");
        await Task.Delay(TimeSpan.FromSeconds(15));

        // 场景 3: 取消重连
        Console.WriteLine("\n场景 3: 取消重连");
        Console.WriteLine("按任意键开始...");
        Console.ReadKey(true);

        var reconnectTask = reconnectionManager.ReconnectAsync();

        await Task.Delay(TimeSpan.FromSeconds(2));
        Console.WriteLine("取消重连...");
        reconnectionManager.CancelReconnection();

        await reconnectTask;

        Console.WriteLine("\n所有场景测试完成。");
    }

    /// <summary>
    /// 交互式断线测试工具
    /// </summary>
    public static async Task RunInteractiveDisconnectionTestAsync()
    {
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        using var client = new GameClient("http://localhost:5000", loggerFactory);
        using var reconnectionManager = new ReconnectionManager(
            client,
            loggerFactory.CreateLogger<ReconnectionManager>());

        using var stateMonitor = new ConnectionStateMonitor(
            loggerFactory.CreateLogger<ConnectionStateMonitor>());

        Console.WriteLine("=== 交互式断线测试工具 ===\n");

        ConfigureReconnectionEvents(reconnectionManager, stateMonitor);

        try
        {
            // 初始化
            await client.LoginAsync("testuser", "password123");
            var server = await client.GetRecommendedGameServerAsync();
            if (server != null)
            {
                await client.ConnectToGameServerAsync(server);
                reconnectionManager.SaveCurrentState();

                if (client.CurrentConnection?.Channel != null)
                {
                    async Task<long> HeartbeatFunc()
                    {
                        return await client.CurrentConnection!.GameHub!.HeartbeatAsync();
                    }

                    stateMonitor.StartMonitoring(
                        client.CurrentConnection.Channel,
                        HeartbeatFunc);
                }
            }

            Console.WriteLine("\n可用命令:");
            Console.WriteLine("  d - 模拟断线");
            Console.WriteLine("  r - 手动重连");
            Console.WriteLine("  s - 显示统计信息");
            Console.WriteLine("  a - 切换自动重连");
            Console.WriteLine("  c - 取消当前重连");
            Console.WriteLine("  q - 退出");
            Console.WriteLine();

            var running = true;
            while (running)
            {
                Console.Write("> ");
                var key = Console.ReadKey(true);
                Console.WriteLine();

                switch (key.KeyChar)
                {
                    case 'd':
                        Console.WriteLine("模拟断线...");
                        await reconnectionManager.HandleDisconnectionAsync("Manual test");
                        break;

                    case 'r':
                        Console.WriteLine("开始手动重连...");
                        await reconnectionManager.ReconnectAsync();
                        break;

                    case 's':
                        var stats = reconnectionManager.Statistics;
                        Console.WriteLine($"\n=== 统计信息 ===");
                        Console.WriteLine($"总断线次数: {stats.TotalDisconnections}");
                        Console.WriteLine($"重连成功: {stats.SuccessfulReconnections}");
                        Console.WriteLine($"重连失败: {stats.FailedReconnections}");
                        Console.WriteLine($"成功率: {stats.SuccessRate:P0}");
                        Console.WriteLine($"最后断线时间: {stats.LastDisconnectionTime}");
                        Console.WriteLine($"当前监控状态: {(stateMonitor.IsMonitoring ? "运行中" : "已停止")}");
                        Console.WriteLine($"连接状态: {stateMonitor.CurrentState}");
                        Console.WriteLine();
                        break;

                    case 'a':
                        reconnectionManager.AutoReconnectEnabled = !reconnectionManager.AutoReconnectEnabled;
                        Console.WriteLine($"自动重连: {(reconnectionManager.AutoReconnectEnabled ? "已启用" : "已禁用")}");
                        break;

                    case 'c':
                        if (reconnectionManager.IsReconnecting)
                        {
                            Console.WriteLine("取消重连...");
                            reconnectionManager.CancelReconnection();
                        }
                        else
                        {
                            Console.WriteLine("当前没有进行中的重连操作");
                        }
                        break;

                    case 'q':
                        running = false;
                        break;

                    default:
                        Console.WriteLine("未知命令");
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n发生错误: {ex.Message}");
        }
    }
}
