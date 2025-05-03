using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PulseRPC.Client;
using PulseRPC.Samples.Shared;
using PulseRPC.Samples.Shared.Messages;

namespace PulseRPC.Samples.Client
{
    /// <summary>
    /// 客户端示例程序
    /// </summary>
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("PulseRPC 客户端示例");
            Console.WriteLine("====================");

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

            // 创建TCP客户端
            var client = new TcpClient(
                "127.0.0.1",
                5000,
                serviceProvider.GetRequiredService<ILogger<TcpClient>>());

            // 注册消息处理程序
            RegisterMessageHandlers(client);

            // 客户端连接事件
            client.Connected += (sender, e) => Console.WriteLine("已连接到服务器");
            client.Disconnected += (sender, e) => Console.WriteLine("与服务器的连接已断开");

            try
            {
                // 连接到服务器
                Console.WriteLine("正在连接服务器...");
                await client.ConnectAsync();

                // 初始化RPC客户端
                RpcClient.Initialize(client);

                while (true)
                {
                    Console.WriteLine("\n请选择操作:");
                    Console.WriteLine("1. 登录");
                    Console.WriteLine("2. 注册");
                    Console.WriteLine("3. 获取用户信息");
                    Console.WriteLine("4. 更新用户信息");
                    Console.WriteLine("0. 退出");

                    var key = Console.ReadKey(true);
                    Console.WriteLine();

                    if (key.KeyChar == '0')
                    {
                        break;
                    }
                    else if (key.KeyChar == '1')
                    {
                        await LoginAsync(client);
                    }
                    else if (key.KeyChar == '2')
                    {
                        await RegisterAsync(client);
                    }
                    else if (key.KeyChar == '3')
                    {
                        await GetUserInfoAsync(client);
                    }
                    else if (key.KeyChar == '4')
                    {
                        await UpdateUserInfoAsync(client);
                    }
                }

                // 断开连接
                await client.DisconnectAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"发生错误: {ex.Message}");
            }

            Console.WriteLine("按任意键退出...");
            Console.ReadKey(true);
        }

        /// <summary>
        /// 注册消息处理程序
        /// </summary>
        /// <param name="client">TCP客户端</param>
        private static void RegisterMessageHandlers(TcpClient client)
        {
            // 注册系统通知处理程序
            client.RegisterHandler<SystemNotification>(notification =>
            {
                Console.WriteLine();
                Console.WriteLine($"收到系统通知: [{notification.Type}] {notification.Title}");
                Console.WriteLine($"内容: {notification.Content}");

                if (notification.ExtraData != null && notification.ExtraData.Count > 0)
                {
                    Console.WriteLine("附加数据:");
                    foreach (var item in notification.ExtraData)
                    {
                        Console.WriteLine($"  {item.Key}: {item.Value}");
                    }
                }

                Console.WriteLine();
            });

            // 注册用户状态通知处理程序
            client.RegisterHandler<UserStatusNotification>(notification =>
            {
                Console.WriteLine();
                Console.WriteLine($"收到用户状态通知: 用户 {notification.UserId} 状态变更为 {notification.Status}");
                Console.WriteLine();
            });

            // 注册全局广播处理程序
            client.RegisterHandler<GlobalBroadcast>(broadcast =>
            {
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"收到全局广播: {broadcast.Content}");
                Console.WriteLine($"发送者: {broadcast.Sender}, 显示时长: {broadcast.Duration}ms");
                Console.ResetColor();
                Console.WriteLine();
            });
        }

        /// <summary>
        /// 登录操作
        /// </summary>
        /// <param name="client">TCP客户端</param>
        static async Task LoginAsync(TcpClient client)
        {
            Console.Write("用户名: ");
            var username = Console.ReadLine();

            Console.Write("密码: ");
            var password = Console.ReadLine();

            try
            {
                // 创建登录请求
                var request = new LoginRequest
                {
                    Username = username,
                    Password = password,
                    ClientVersion = 1
                };

                // 发送请求
                Console.WriteLine("正在发送登录请求...");
                var response = await client.SendRequestAsync<LoginRequest, LoginResponse>(request);

                // 显示响应结果
                if (response.Status == ResponseStatus.Success)
                {
                    Console.WriteLine($"登录成功! 用户ID: {response.UserId}, 令牌: {response.Token}");
                }
                else
                {
                    Console.WriteLine($"登录失败: {response.Status} - {response.ErrorMessage}");
                }
            }
            catch (TimeoutException)
            {
                Console.WriteLine("请求超时，服务器没有响应");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"发送请求时发生错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 注册操作
        /// </summary>
        /// <param name="client">TCP客户端</param>
        static async Task RegisterAsync(TcpClient client)
        {
            Console.Write("用户名: ");
            var username = Console.ReadLine();

            Console.Write("密码: ");
            var password = Console.ReadLine();

            Console.Write("邮箱: ");
            var email = Console.ReadLine();

            Console.Write("手机号: ");
            var phoneNumber = Console.ReadLine();

            try
            {
                // 创建注册请求
                var request = new RegisterRequest
                {
                    Username = username,
                    Password = password,
                    Email = email,
                    PhoneNumber = phoneNumber
                };

                // 发送请求
                Console.WriteLine("正在发送注册请求...");
                var response = await client.SendRequestAsync<RegisterRequest, RegisterResponse>(request);

                // 显示响应结果
                if (response.Status == ResponseStatus.Success)
                {
                    Console.WriteLine($"注册成功! 用户ID: {response.UserId}");
                }
                else
                {
                    Console.WriteLine($"注册失败: {response.Status} - {response.ErrorMessage}");
                }
            }
            catch (TimeoutException)
            {
                Console.WriteLine("请求超时，服务器没有响应");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"发送请求时发生错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取用户信息操作
        /// </summary>
        /// <param name="client">TCP客户端</param>
        static async Task GetUserInfoAsync(TcpClient client)
        {
            Console.Write("用户ID: ");
            if (!int.TryParse(Console.ReadLine(), out var userId))
            {
                Console.WriteLine("无效的用户ID");
                return;
            }

            try
            {
                // 创建获取用户信息请求
                var request = new GetUserInfoRequest
                {
                    UserId = userId
                };

                // 发送请求
                Console.WriteLine("正在获取用户信息...");
                var response = await client.SendRequestAsync<GetUserInfoRequest, GetUserInfoResponse>(request);

                // 显示响应结果
                if (response.Status == ResponseStatus.Success)
                {
                    Console.WriteLine("获取用户信息成功!");
                    Console.WriteLine($"用户ID: {response.UserId}");
                    Console.WriteLine($"用户名: {response.Username}");
                    Console.WriteLine($"昵称: {response.Nickname}");
                    Console.WriteLine($"头像: {response.AvatarUrl}");
                    Console.WriteLine($"状态: {response.Status}");
                    Console.WriteLine($"注册时间: {response.RegisterTime}");
                    Console.WriteLine($"最后登录时间: {response.LastLoginTime}");
                }
                else
                {
                    Console.WriteLine($"获取用户信息失败: {response.Status} - {response.ErrorMessage}");
                }
            }
            catch (TimeoutException)
            {
                Console.WriteLine("请求超时，服务器没有响应");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"发送请求时发生错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 更新用户信息操作
        /// </summary>
        /// <param name="client">TCP客户端</param>
        static async Task UpdateUserInfoAsync(TcpClient client)
        {
            Console.Write("用户ID: ");
            if (!int.TryParse(Console.ReadLine(), out var userId))
            {
                Console.WriteLine("无效的用户ID");
                return;
            }

            Console.Write("新昵称 (留空不修改): ");
            var nickname = Console.ReadLine();

            Console.Write("新头像URL (留空不修改): ");
            var avatarUrl = Console.ReadLine();

            try
            {
                // 创建更新用户信息请求
                var request = new UpdateUserInfoRequest
                {
                    UserId = userId,
                    Nickname = nickname,
                    AvatarUrl = avatarUrl
                };

                // 发送请求
                Console.WriteLine("正在更新用户信息...");
                var response = await client.SendRequestAsync<UpdateUserInfoRequest, UpdateUserInfoResponse>(request);

                // 显示响应结果
                if (response.Status == ResponseStatus.Success)
                {
                    Console.WriteLine($"更新用户信息成功! 更新了 {response.UpdatedCount} 个字段");
                }
                else
                {
                    Console.WriteLine($"更新用户信息失败: {response.Status} - {response.ErrorMessage}");
                }
            }
            catch (TimeoutException)
            {
                Console.WriteLine("请求超时，服务器没有响应");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"发送请求时发生错误: {ex.Message}");
            }
        }
    }
}
