using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PulseRPC.Client;
using PulseRPC.Protocol.Serialization;
using PulseRPC.Samples.Shared;
using PulseRPC.Samples.Shared.Messages;

namespace PulseRPC.Samples.Client;

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

        // 构建服务提供程序
        var serviceProvider = services.BuildServiceProvider();

        // 获取日志提供器
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger<Program>();

        // 注册消息类型
        RegisterMessageTypes(logger);

        // 创建 TCP 客户端
        var client = new TcpClient(
            "127.0.0.1",
            5000,
            serviceProvider.GetRequiredService<ILogger<TcpClient>>());

        try
        {
            // 连接到服务器
            await client.ConnectAsync();
            logger.LogInformation("已连接到服务器");

            // 登录
            logger.LogInformation("正在登录...");
            var loginRequest = new LoginRequest
            {
                Username = "admin",
                Password = "password",
                ClientVersion = 1001
            };

            // 验证请求对象可以被序列化
            try {
                var serialized = MessageSerializer.Serialize(loginRequest);
                logger.LogDebug("登录请求序列化成功，大小: {Size} 字节", serialized.Length);
            }
            catch (Exception ex) {
                logger.LogError(ex, "序列化登录请求失败");
                throw;
            }

            var loginResponse = await client.SendRequestAsync<LoginRequest, LoginResponse>(loginRequest);

            if (loginResponse.Success)
            {
                logger.LogInformation("登录成功，用户ID={UserId}, 用户名={Username}", loginResponse.UserId, loginResponse.Username);

                // 获取用户信息
                logger.LogInformation("正在获取用户信息...");
                var userInfoResponse = await client.SendRequestAsync<GetUserInfoRequest, GetUserInfoResponse>(new GetUserInfoRequest
                {
                    UserId = loginResponse.UserId
                });

                if (userInfoResponse.Status == ResponseStatus.Success)
                {
                    logger.LogInformation("用户信息: 昵称={Nickname}, 头像={AvatarUrl}",
                        userInfoResponse.Nickname, userInfoResponse.AvatarUrl);

                    // 更新用户信息
                    logger.LogInformation("正在更新用户信息...");
                    var updateResponse = await client.SendRequestAsync<UpdateUserInfoRequest, UpdateUserInfoResponse>(new UpdateUserInfoRequest
                    {
                        UserId = loginResponse.UserId,
                        Nickname = "超级管理员",
                        AvatarUrl = "https://example.com/avatar.png"
                    });

                    if (updateResponse.Status == ResponseStatus.Success)
                    {
                        logger.LogInformation("用户信息更新成功");
                    }
                    else
                    {
                        logger.LogError("用户信息更新失败: {ErrorMessage}", updateResponse.ErrorMessage);
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

    /// <summary>
    /// 注册消息类型
    /// </summary>
    private static void RegisterMessageTypes(ILogger logger)
    {
        logger.LogInformation("开始注册消息类型...");

        // 确保 MemoryPack 正确生成序列化器
        try
        {
            MemoryPackHelpers.RegisterAllTypes();
            logger.LogInformation("MemoryPack 序列化器注册成功");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "MemoryPack 序列化器注册失败");
        }

        logger.LogInformation("消息类型注册完成");
    }
}
