using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PulseRPC.Client;
using PulseRPC.Network;
using PulseRPC.Samples.Shared;
using PulseRPC.Samples.Shared.Messages;
using System.Net;

namespace PulseRPC.Samples.Client;

[assembly: PulseClientGeneration(typeof(IAuthStreamingHub))]
[assembly: PulseClientGeneration(typeof(IUserStreamingHub))]

/// <summary>
/// 客户端示例程序
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("PulseRPC 客户端示例");
        Console.WriteLine("===================");

        // 创建服务容器
        var services = new ServiceCollection();

        // 添加日志
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        // 添加网络服务
        services.AddSingleton<NetworkOptions>();
        services.AddSingleton(sp => 
        {
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<NetworkClient>();
            var options = sp.GetRequiredService<NetworkOptions>();
            // 创建NetworkClient时传入IPulseService实现
            return new NetworkClient(logger, "127.0.0.1", 8888, new DefaultPulseService(), options);
        });

        // 构建服务提供程序
        var serviceProvider = services.BuildServiceProvider();

        // 获取日志提供器
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger<Program>();

        // 创建 TCP 客户端
        var client = serviceProvider.GetRequiredService<NetworkClient>();

        try
        {
            // 连接到服务器
            await client.ConnectAsync();
            logger.LogInformation("已连接到服务器");

            // 使用源生成的客户端代理
            var authHub = new AuthStreamingHubClient(client);
            var userHub = new UserStreamingHubClient(client);

            // 登录
            logger.LogInformation("正在登录...");
            var loginRequest = new LoginRequest
            {
                Username = "admin",
                Password = "password",
                ClientVersion = 1001
            };

            var loginResponse = await authHub.Login(loginRequest);

            if (loginResponse.Success)
            {
                logger.LogInformation("登录成功，用户ID={UserId}, 用户名={Username}", loginResponse.UserId, loginResponse.Username);

                // 获取用户信息
                logger.LogInformation("正在获取用户信息...");
                var userInfoResponse = await userHub.GetUserInfoAsync(loginResponse.UserId);

                if (userInfoResponse.Status == ResponseStatus.Success)
                {
                    logger.LogInformation("用户信息: 昵称={Nickname}, 头像={AvatarUrl}",
                        userInfoResponse.Nickname, userInfoResponse.AvatarUrl);

                    // 更新用户信息
                    logger.LogInformation("正在更新用户信息...");
                    var updateResponse = await userHub.UpdateUserInfoAsync(new UpdateUserInfoRequest
                    {
                        UserId = loginResponse.UserId,
                        Nickname = "超级管理员",
                        AvatarUrl = "https://example.com/avatar.png"
                    });

                    if (updateResponse.Item1 == ResponseStatus.Success)
                    {
                        logger.LogInformation("用户信息更新成功");
                    }
                    else
                    {
                        logger.LogError("用户信息更新失败: {ErrorMessage}", updateResponse.Item2);
                    }
                }
                else
                {
                    logger.LogError("获取用户信息失败: {ErrorMessage}", userInfoResponse.ErrorMessage);
                }
            }
            else
            {
                logger.LogError("登录失败: {ErrorMessage}", loginResponse.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "发生错误");
        }
        finally
        {
            // 断开连接
            client.Dispose();
            logger.LogInformation("已断开连接");
        }

        Console.WriteLine("按任意键退出...");
        Console.ReadKey();
    }
}

// 默认的PulseService实现，用于客户端
internal class DefaultPulseService : IPulseService
{
    public ValueTask<bool> HandleMessageAsync(NetworkSession session, Memory<byte> data, CancellationToken cancellationToken = default)
    {
        // 客户端实现，接收服务端消息
        return ValueTask.FromResult(true);
    }

    public byte[] SerializeMessage<T>(T message) where T : IMemoryPackable<T>
    {
        return MemoryPackSerializer.Serialize(message);
    }

    public T? DeserializeMessage<T>(ReadOnlyMemory<byte> data) where T : IMemoryPackable<T>
    {
        return MemoryPackSerializer.Deserialize<T>(data);
    }
}
