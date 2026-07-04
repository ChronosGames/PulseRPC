using Microsoft.Extensions.Logging;

namespace ChatApp.Client.Console;

internal class Program
{
    private static async Task Main(string[] args)
    {
        System.Console.WriteLine("================================");
        System.Console.WriteLine("  PulseRPC ChatApp 控制台客户端");
        System.Console.WriteLine("  服务隔离架构演示");
        System.Console.WriteLine("================================");

        await RunChatDemoAsync();

        if (!System.Console.IsInputRedirected)
        {
            System.Console.WriteLine("\n按任意键退出...");
            System.Console.ReadKey();
        }
    }

    /// <summary>
    /// 演示服务隔离架构的聊天功能
    /// </summary>
    private static async Task RunChatDemoAsync()
    {
        System.Console.WriteLine("\n=== 服务隔离架构聊天演示 ===");
        System.Console.WriteLine("该演示展示了如何使用基于服务隔离的聊天室服务");
        System.Console.WriteLine("每个房间对应一个独立的服务实例，确保消息顺序处理\n");

        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Warning);
        });

        var logger = loggerFactory.CreateLogger<ChatClient>();
        var chatClient = new ChatClient(logger, loggerFactory);

        try
        {
            System.Console.WriteLine("1. 初始化客户端...");
            await chatClient.InitializeAsync();
            System.Console.WriteLine("   √ 客户端初始化成功\n");

            System.Console.WriteLine("2. 登录 (用户名: Alice)...");
            var loginResult = await chatClient.LoginAsync("Alice");
            System.Console.WriteLine(loginResult
                ? "   √ 登录成功\n"
                : "   × 登录失败\n");

            System.Console.WriteLine("3. 加入聊天室 'lobby'...");
            var joinResult = await chatClient.JoinRoomAsync("lobby");
            System.Console.WriteLine(joinResult
                ? "   √ 成功加入房间 'lobby'\n"
                : "   × 加入房间失败\n");

            if (joinResult)
            {
                System.Console.WriteLine("4. 发送消息...");
                await chatClient.SendMessageAsync("Hello from service isolation architecture!");
                System.Console.WriteLine("   √ 消息已发送\n");

                await chatClient.SendMessageAsync("This message is processed sequentially in the room service instance.");
                System.Console.WriteLine("   √ 消息已发送\n");

                System.Console.WriteLine("5. 查询房间成员...");
                var members = await chatClient.GetMembersAsync();
                System.Console.WriteLine($"   当前成员: {string.Join(", ", members)}\n");

                System.Console.WriteLine("6. 查询最近消息...");
                var recentMessages = await chatClient.GetRecentMessagesAsync(10);
                foreach (var msg in recentMessages)
                {
                    System.Console.WriteLine($"   [{msg.Timestamp:HH:mm:ss}] {msg.UserName}: {msg.Content}");
                }
                System.Console.WriteLine();

                System.Console.WriteLine("7. 离开聊天室...");
                await chatClient.LeaveRoomAsync();
                System.Console.WriteLine("   √ 已离开房间\n");
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
            System.Console.WriteLine($"\n演示失败: {ex.Message}");
            System.Console.ResetColor();
        }
        finally
        {
            await chatClient.ShutdownAsync();
        }
    }
}
