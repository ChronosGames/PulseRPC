using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using MiniGame.Shared;
using PulseRPC;
using PulseRPC.Client;
using PulseRPC.Samples.Shared.Messages;

namespace MiniGame.Client
{
    /// <summary>
    /// 小游戏客户端程序
    /// </summary>
    class Program
    {
        private const string NodeName = "MiniGameServer";

        static async Task Main(string[] args)
        {
            Console.WriteLine("小游戏客户端启动中...");

            try
            {
                // 初始化网络服务
                InitializeNetworkManager();

                // 连接服务器
                await NetworkManager.ConnectAllAsync();
                Console.WriteLine($"已连接到服务器: localhost:7000");

                // 获取AuthHub并登录
                var authHub = NetworkManager.CreateServiceClient<AuthStreamingHub>(NodeName);

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
                    var gameHub = NetworkManager.CreateServiceClient<GameStreamingHub>(NodeName);

                    // 获取用户信息
                    var userInfo = await gameHub.GetUserInfoAsync(loginResponse.UserId);
                    Console.WriteLine($"用户状态: {userInfo.UserStatus}");
                    Console.WriteLine($"用户昵称: {userInfo.Nickname}");
                    Console.WriteLine($"头像URL: {userInfo.AvatarUrl}");
                    
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
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"内部错误: {ex.InnerException.Message}");
                }
                Console.WriteLine($"堆栈跟踪: {ex.StackTrace}");
            }
            finally
            {
                // 断开连接
                await NetworkManager.DisconnectAllAsync();
                Console.WriteLine("已断开连接");
            }
        }

        /// <summary>
        /// 初始化网络管理器
        /// </summary>
        private static void InitializeNetworkManager()
        {
            try
            {
                // 设置日志记录器
                NetworkManager.SetLogger(NullLogger.Instance);

                // 配置节点选项
                var options = new NodeOptions
                {
                    AutoReconnect = true,
                    ReconnectInterval = TimeSpan.FromSeconds(5),
                    ConnectionTimeout = TimeSpan.FromSeconds(10),
                    SerializerFactory = () => new DefaultPulseService()
                };

                // 注册服务节点
                NetworkManager.RegisterNode(NodeName, "localhost", 7000, options);

                // 扫描当前程序集，查找服务客户端
                var assembly = typeof(Program).Assembly;
                NetworkManager.ScanAssemblies(assembly);

                Console.WriteLine("使用PulseRPC自动生成的序列化器...");
                Console.WriteLine("所有临时请求/响应对象将使用生成的MemoryPack格式化器");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"初始化网络管理器时出错: {ex.Message}");
                throw;
            }
        }
    }
}
