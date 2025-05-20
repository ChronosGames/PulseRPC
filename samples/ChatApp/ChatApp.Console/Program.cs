using Microsoft.Extensions.Logging;

namespace ChatApp.Console;

class Program
{
    static async Task Main(string[] args)
    {
        // 设置日志工厂
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder
                .AddConsole()
                .SetMinimumLevel(LogLevel.Information);
        });

        // 创建客户端
        var client = new GameConsoleClient(loggerFactory);

        try
        {
            // 初始化客户端
            await client.InitializeAsync();

            // 启动客户端循环
            await client.RunAsync();
        }
        catch (Exception ex)
        {
            System.Console.ForegroundColor = ConsoleColor.Red;
            System.Console.WriteLine($"错误: {ex.Message}");
            System.Console.ResetColor();
        }
        finally
        {
            // 清理资源
            await client.ShutdownAsync();

            // 等待用户按键退出
            System.Console.WriteLine("按任意键退出...");
            System.Console.ReadKey(true);
        }
    }
}
