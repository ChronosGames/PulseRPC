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
        System.Console.WriteLine("  服务隔离架构演示");
        System.Console.WriteLine("================================");

        // 演示服务隔离架构的聊天功能
        await RunChatDemoAsync();

        System.Console.WriteLine("\n按任意键退出...");
        System.Console.ReadKey();
    }

    /// <summary>
    /// 演示服务隔离架构的聊天功能
    /// </summary>
    private static async Task RunChatDemoAsync()
    {
        System.Console.WriteLine("\n=== 服务隔离架构聊天演示 ===");
        System.Console.WriteLine("该演示展示了如何使用基于服务隔离的聊天室服务");
        System.Console.WriteLine("每个房间对应一个独立的服务实例，确保消息顺序处理\n");

        // 创建日志工厂
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        var logger = loggerFactory.CreateLogger<ChatClient>();
        var chatClient = new ChatClient(logger);

        try
        {
            // 初始化客户端
            System.Console.WriteLine("1. 初始化客户端...");
            await chatClient.InitializeAsync();
            System.Console.WriteLine("   ✓ 客户端初始化成功\n");

            // 加入房间
            System.Console.WriteLine("2. 加入聊天室 'lobby'...");
            var joinResult = await chatClient.JoinRoomAsync("lobby", "Alice");
            System.Console.WriteLine(joinResult
                ? "   ✓ 成功加入房间 'lobby'\n"
                : "   ✗ 加入房间失败\n");

            if (joinResult)
            {
                // 发送消息
                System.Console.WriteLine("3. 发送消息...");
                await chatClient.SendMessageAsync("Hello from service isolation architecture!");
                System.Console.WriteLine("   ✓ 消息已发送\n");

                await chatClient.SendMessageAsync("This message is processed sequentially in the room service instance.");
                System.Console.WriteLine("   ✓ 消息已发送\n");

                // 测试异常处理
                System.Console.WriteLine("4. 测试异常处理（单个房间的异常不影响其他房间）...");
                try
                {
                    await chatClient.TestExceptionAsync("Test exception in room 'lobby'");
                }
                catch (Exception ex)
                {
                    System.Console.WriteLine($"   ✓ 捕获到预期的异常: {ex.Message}\n");
                }

                // 离开房间
                System.Console.WriteLine("5. 离开聊天室...");
                await chatClient.LeaveRoomAsync();
                System.Console.WriteLine("   ✓ 已离开房间\n");
            }

            System.Console.WriteLine("演示完成！");
            System.Console.WriteLine("\n服务隔离架构特性:");
            System.Console.WriteLine("  • 每个房间有独立的服务实例");
            System.Console.WriteLine("  • 相同房间的消息顺序处理（无需加锁）");
            System.Console.WriteLine("  • 不同房间的消息并发处理");
            System.Console.WriteLine("  • 单个房间故障不影响其他房间");
        }
        catch (Exception ex)
        {
            System.Console.ForegroundColor = ConsoleColor.Red;
            System.Console.WriteLine($"\n❌ 演示失败: {ex.Message}");
            System.Console.ResetColor();
        }
        finally
        {
            // 关闭客户端
            await chatClient.ShutdownAsync();
        }
    }

    /// <summary>
    /// 使用依赖注入版本的客户端API（已注释 - 等待客户端API完善）
    /// </summary>
    /*
    private static async Task RunWithDependencyInjectionAsync()
    {
        // TODO: 等待客户端API完善后取消注释
        await Task.CompletedTask;
    }
    */

    /// <summary>
    /// 使用非依赖注入版本的客户端API（Unity兼容）（已注释 - 等待客户端API完善）
    /// </summary>
    /*
    private static async Task RunWithoutDependencyInjectionAsync()
    {
        // TODO: 等待客户端API完善后取消注释
        await Task.CompletedTask;
    }
    */
}
