using System;
using System.Threading.Tasks;
using MiniGame.Shared;
using PulseRPC.Samples.Shared.Messages;

namespace MiniGame.Client
{
    /// <summary>
    /// 小游戏客户端程序
    /// </summary>
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("小游戏客户端启动中...");

            try
            {
                // 初始化网络服务
                await GameNetworkService.Instance.InitializeAsync("localhost", 7000);

                // 获取AuthHub并登录
                var authHub = GameNetworkService.Instance.GetAuthHub();

                Console.Write("请输入用户名: ");
                var username = Console.ReadLine() ?? "Guest";

                Console.Write("请输入密码: ");
                var password = Console.ReadLine() ?? "password";

                var loginResponse = await authHub.Login(new LoginRequest
                {
                    Username = username,
                    Password = password,
                    ClientVersion = 1
                });

                if (loginResponse.Success)
                {
                    Console.WriteLine($"登录成功！欢迎 {loginResponse.Username}");
                    Console.WriteLine($"用户ID: {loginResponse.UserId}");
                    Console.WriteLine($"令牌: {loginResponse.Token}");

                    // 获取游戏Hub
                    var gameHub = GameNetworkService.Instance.GetGameHub();

                    // 获取游戏状态
                    var gameStatus = await gameHub.GetGameStatusAsync();
                    Console.WriteLine($"游戏状态: {gameStatus.Status}");
                    Console.WriteLine($"在线玩家: {gameStatus.OnlinePlayers}");
                    Console.WriteLine($"服务器时间: {gameStatus.ServerTime}");

                    // 订阅通知频道
                    await gameHub.SubscribeNotificationsAsync(new[] { "global", "system" });

                    Console.WriteLine("已连接到游戏服务器并订阅通知频道");
                    Console.WriteLine("按任意键退出...");
                    Console.ReadKey();

                    // 取消订阅通知
                    await gameHub.UnsubscribeNotificationsAsync(new[] { "global", "system" });
                }
                else
                {
                    Console.WriteLine($"登录失败: {loginResponse.ErrorMessage} (错误代码: {loginResponse.ErrorCode})");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"发生错误: {ex.Message}");
            }
            finally
            {
                // 断开连接
                await GameNetworkService.Instance.DisconnectAsync();
                Console.WriteLine("已断开连接");
            }
        }
    }
}
