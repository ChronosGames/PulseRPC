using JwtAuthApp.Server.Services;
using JwtAuthApp.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseRPC.Server;
using PulseRPC.Server.Extensions;
using PulseRPC.Shared;
using JwtTokenService = JwtAuthApp.Server.Authentication.JwtTokenService;
using JwtTokenServiceOptions = JwtAuthApp.Server.Authentication.JwtTokenServiceOptions;

Console.WriteLine("=================================");
Console.WriteLine("  PulseRPC JWT 认证示例服务器");
Console.WriteLine("=================================");

var host = Host.CreateDefaultBuilder(args)
    // 显式以程序集所在目录为基准加载 appsettings.json，避免 `dotnet run` 的工作目录
    // 与输出目录不一致导致配置（例如 JwtTokenService:Secret）绑定为空。
    .ConfigureAppConfiguration((_, configuration) =>
    {
        configuration.SetBasePath(AppContext.BaseDirectory);
        configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
    })
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddConsole();
        logging.SetMinimumLevel(LogLevel.Information);
    })
    .ConfigureServices((context, services) =>
    {
        services.AddPulseServer(options =>
        {
            options.Transports = new()
            {
                new TransportChannelConfiguration
                {
                    Name = "TCP",
                    Type = TransportType.TCP,
                    Port = 5001,
                    IsDefault = true,
                },
            };
        });

        services.Configure<JwtTokenServiceOptions>(
            context.Configuration.GetSection("JwtAuthApp.Server:JwtTokenService"));
        services.AddSingleton<JwtTokenService>();

        // Hub 实现注册（无状态/连接安全，单例即可；TimerHub 内部按 connectionId 隔离状态）
        services.AddSingleton<IAccountHub, AccountHub>();
        services.AddSingleton<IGreeterHub, GreeterHub>();
        services.AddSingleton<ITimerHub, TimerHub>();

        // 注册所有 [Channel("CLIENT")] : IPulseHub 推送接收器的 IHubContext<T>（源生成，含 ITimerReceiver）
        services.AddAllPulseReceiverContexts();
    })
    .Build();

var server = host.Services.GetRequiredService<IPulseServer>();

try
{
    await server.StartAsync();

    Console.WriteLine("\nJWT 认证示例服务器已启动 (TCP :5001)，按 Ctrl+C 停止...\n");

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    await Task.Delay(Timeout.Infinite, cts.Token);
}
catch (OperationCanceledException)
{
    // 正常停止
}
finally
{
    await server.StopAsync();
    Console.WriteLine("\n服务器已停止。");
}
