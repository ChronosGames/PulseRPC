using System;
using System.Threading.Tasks;
using JwtAuthApp.Shared;
using PulseRPC;
using PulseRPC.Client;

namespace JwtAuthApp.Client
{
    class Program : ITimerHubReceiver
    {
        static Task Main(string[] args)
        {
            return new Program().MainCore(args);
        }

        private async Task MainCore(string[] args)
        {
            var connection = new PulseTcpConnection("localhost", 5001);
            await connection.ConnectAsync();

            // 1. 在没有身份验证令牌的情况下调用API
            {
                var accountClient = PulseClientFactory.Create<IAccountService>(connection);
                var user = await accountClient.GetCurrentUserNameAsync();
                Console.WriteLine($@"[IAccountService.GetCurrentUserNameAsync] Current User: UserId={user.Value.UserId}; IsAuthenticated={user.Value.IsAuthenticated}; Name={user.Value.Name}");
                try
                {
                    var greeterClientAnon = PulseClientFactory.Create<IGreeterService>(connection);
                    var result = await greeterClientAnon.HelloAsync();
                    Console.WriteLine($"[IGreeterService.HelloAsync] {result.Value}");
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[IGreeterService.HelloAsync] Exception: {e.Message}");
                }
            }

            // 3. 使用ID和密码登录并接收身份验证令牌
            var signInId = "kyaru@example.com";
            var password = "P@ssword2";

            // 4. 使用身份验证令牌获取用户信息
            {
                var accountClient = PulseClientFactory.Create<IAccountService>(connection);
                var signInResult = await accountClient.SignInAsync(signInId, password);
                if (signInResult.IsSuccess)
                {
                    connection.SetAuthToken(signInResult.Value.Token);
                    var user = await accountClient.GetCurrentUserNameAsync();
                    Console.WriteLine($@"[IAccountService.GetCurrentUserNameAsync] Current User: UserId={user.Value.UserId}; IsAuthenticated={user.Value.IsAuthenticated}; Name={user.Value.Name}");

                    // 5. 使用身份验证令牌调用API
                    var greeterClient = PulseClientFactory.Create<IGreeterService>(connection);
                    var result = await greeterClient.HelloAsync();
                    Console.WriteLine($"[IGreeterService.HelloAsync] {result.Value}");
                }
            }

            // 5. 使用身份验证调用StreamingHub
            {
                var timerHub = PulseClientFactory.ConnectToHub<ITimerHub, ITimerHubReceiver>(connection, this);
                await timerHub.SetAsync(TimeSpan.FromSeconds(5));
            }

            // 6. 权限不足（当前用户不在管理员角色中）
            {
                var accountClient = PulseClientFactory.Create<IAccountService>(connection);
                try
                {
                    var result = await accountClient.DangerousOperationAsync();
                    if (!result.IsSuccess)
                    {
                        Console.WriteLine($"[IAccountService.DangerousOperationAsync] Error: {result.ErrorMessage}");
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[IAccountService.DangerousOperationAsync] Exception: {e.Message}");
                }
            }

            // 7. 在调用API之前刷新令牌
            {
                await Task.Delay(1000 * 6); // 服务器配置的令牌过期时间为5秒
                var accountClient = PulseClientFactory.Create<IAccountService>(connection);
                var signInResult = await accountClient.SignInAsync(signInId, password);
                if (signInResult.IsSuccess)
                {
                    connection.SetAuthToken(signInResult.Value.Token);
                    var greeterClient = PulseClientFactory.Create<IGreeterService>(connection);
                    var result = await greeterClient.HelloAsync();
                    Console.WriteLine($"[IGreeterService.HelloAsync] {result.Value}");
                }
            }

            Console.ReadLine();
            await connection.DisconnectAsync();
        }

        void ITimerHubReceiver.OnTick(string message)
        {
            Console.WriteLine($"[ITimerHubReceiver.OnTick] {message}");
        }
    }
}
