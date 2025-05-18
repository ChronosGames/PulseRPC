using Microsoft.Extensions.Logging;
using PulseRPC.Client;
using PulseRPC.Samples.Shared;
using PulseRPC.Samples.Shared.Messages;

namespace PulseRPC.Samples.Client;

/// <summary>
/// 客户端示例程序
/// </summary>
class Program
{
    private const string MainNodeName = "MainServer";

    static async Task Main(string[] args)
    {
        Console.WriteLine("PulseRPC 客户端示例");
        Console.WriteLine("===================");

        try
        {
            var factory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Debug);
            });

            NetworkManager.SetLogger(factory.CreateLogger("PulseRPC"));

            // 注册主服务节点
            NetworkManager.RegisterNode(MainNodeName, "127.0.0.1", 8888, new NodeOptions
            {
                AutoReconnect = true,
                MaxReconnectAttempts = 3,
                ReconnectInterval = TimeSpan.FromSeconds(3),
                ConnectionTimeout = TimeSpan.FromSeconds(10)
            });

            // 连接所有节点
            await NetworkManager.ConnectAllAsync();
            Console.WriteLine("已连接到服务器");

            // 获取StreamingHub客户端
            var authHub = NetworkManager.CreateServiceClient<IAuthStreamingHub>(MainNodeName);
            var userHub = NetworkManager.CreateServiceClient<IUserStreamingHub>(MainNodeName);

            // 登录
            Console.WriteLine("正在登录...");
            var loginRequest = new LoginRequest
            {
                Username = "admin",
                Password = "password",
                ClientVersion = 1001
            };

            var loginResponse = await authHub.Login(loginRequest);

            if (loginResponse.Success)
            {
                Console.WriteLine($"登录成功，用户ID={loginResponse.UserId}, 用户名={loginResponse.Username}");

                // 获取用户信息
                Console.WriteLine("正在获取用户信息...");
                var userInfoResponse = await userHub.GetUserInfoAsync(loginResponse.UserId);

                if (userInfoResponse.Status == ResponseStatus.Success)
                {
                    Console.WriteLine($"用户信息: 昵称={userInfoResponse.Nickname}, 头像={userInfoResponse.AvatarUrl}");

                    // 更新用户信息
                    Console.WriteLine("正在更新用户信息...");
                    var updateResponse = await userHub.UpdateUserInfoAsync(new UpdateUserInfoRequest
                    {
                        UserId = loginResponse.UserId,
                        Nickname = "超级管理员",
                        AvatarUrl = "https://example.com/avatar.png"
                    });

                    if (updateResponse.Item1 == ResponseStatus.Success)
                    {
                        Console.WriteLine("用户信息更新成功");
                    }
                    else
                    {
                        Console.WriteLine($"用户信息更新失败: {updateResponse.Item2}");
                    }
                }
                else
                {
                    Console.WriteLine($"获取用户信息失败: {userInfoResponse.ErrorMessage}");
                }
            }
            else
            {
                Console.WriteLine($"登录失败: {loginResponse.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"发生错误: {ex.Message}");
        }
        finally
        {
            // 断开所有连接
            await NetworkManager.DisconnectAllAsync();
            Console.WriteLine("已断开连接");
        }

        Console.WriteLine("按任意键退出...");
        Console.ReadKey();
    }
}
