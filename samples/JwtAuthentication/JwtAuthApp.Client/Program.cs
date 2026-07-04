using JwtAuthApp.Client;
using JwtAuthApp.Shared;
using Microsoft.Extensions.Logging;
using PulseRPC.Client;
using PulseRPC.Client.Configuration;

Console.WriteLine("=================================");
Console.WriteLine("  PulseRPC JWT 认证示例客户端");
Console.WriteLine("=================================\n");

var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Warning);
});

var client = new PulseClientBuilder()
    .WithLogging(loggerFactory)
    .Build();

await client.InitializeAsync();
var channel = await client.ConnectToServerAsync("127.0.0.1", 5001);
Console.WriteLine("已连接到服务器 127.0.0.1:5001\n");

var accountHub = channel.GetHub<IAccountHub>();
var greeterHub = channel.GetHub<IGreeterHub>();
var timerHub = channel.GetHub<ITimerHub>();

// 1. 未认证状态下调用
Console.WriteLine("1. 未认证状态下调用 API");
var anonymousUser = await accountHub.GetCurrentUserNameAsync();
Console.WriteLine($"   [GetCurrentUserNameAsync] IsAuthenticated={anonymousUser.IsAuthenticated}; Name={anonymousUser.Name}");
try
{
    await greeterHub.HelloAsync();
}
catch (Exception ex)
{
    Console.WriteLine($"   [HelloAsync] 预期异常: {ex.Message}");
}

// 2. 登录（连接级认证：登录成功后，同一连接上的后续调用均自动携带身份）
Console.WriteLine("\n2. 登录 (kyaru@example.com)");
const string signInId = "kyaru@example.com";
const string password = "P@ssword2";
var signInResult = await accountHub.SignInAsync(signInId, password);
if (!signInResult.Success)
{
    Console.WriteLine($"   登录失败: {signInResult.ErrorMessage}");
    return;
}
Console.WriteLine($"   登录成功: UserId={signInResult.UserId}; Name={signInResult.Name}; Token 过期时间={signInResult.Expiration:O}");

// 3. 认证后调用
Console.WriteLine("\n3. 认证后调用 API（同一连接自动携带身份）");
var authenticatedUser = await accountHub.GetCurrentUserNameAsync();
Console.WriteLine($"   [GetCurrentUserNameAsync] IsAuthenticated={authenticatedUser.IsAuthenticated}; Name={authenticatedUser.Name}; Roles=[{string.Join(",", authenticatedUser.Roles)}]");
var greeting = await greeterHub.HelloAsync();
Console.WriteLine($"   [HelloAsync] {greeting}");

// 4. 订阅定时器推送并启动
Console.WriteLine("\n4. 启动定时器（每 1 秒推送一次，持续约 3 秒）");
var timerReceiver = new TimerReceiver();
var subscription = channel.RegisterReceiver<ITimerReceiver>(timerReceiver);
await timerHub.StartAsync(TimeSpan.FromSeconds(1));
await Task.Delay(TimeSpan.FromSeconds(3.5));
await timerHub.StopAsync();
subscription.Dispose();

// 5. 权限不足（当前用户不在 Administrators 角色）
Console.WriteLine("\n5. 权限不足场景（当前用户没有 Administrators 角色）");
try
{
    var result = await accountHub.DangerousOperationAsync();
    Console.WriteLine($"   [DangerousOperationAsync] {result}");
}
catch (Exception ex)
{
    Console.WriteLine($"   [DangerousOperationAsync] 预期异常: {ex.Message}");
}

// 6. Token 过期后使用旧 Token 重新认证（预期失败），随后重新登录恢复
Console.WriteLine("\n6. 等待 Token 过期(15秒)后尝试用旧 Token 重新认证");
await Task.Delay(TimeSpan.FromSeconds(16));
var reauthenticated = await accountHub.AuthenticateAsync(signInResult.Token);
Console.WriteLine($"   [AuthenticateAsync(旧Token)] 结果: {reauthenticated} (预期 false，因为 Token 已过期)");

var renewedSignIn = await accountHub.SignInAsync(signInId, password);
Console.WriteLine($"   [SignInAsync] 重新登录成功: {renewedSignIn.Success}");
var greetingAfterRenew = await greeterHub.HelloAsync();
Console.WriteLine($"   [HelloAsync] {greetingAfterRenew}");

if (Console.IsInputRedirected)
{
    Console.WriteLine("\n演示完成。");
}
else
{
    Console.WriteLine("\n演示完成，按任意键退出...");
    Console.ReadKey();
}

await channel.DisconnectAsync();
await client.StopAsync();
