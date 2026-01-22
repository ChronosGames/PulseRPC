using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseRPC.Benchmark.Contracts;
using PulseRPC.Server;
using PulseRPC.Server.Configuration;
using PulseRPC.Server.Extensions;

namespace PulseRPC.Benchmark.Server;

/// <summary>
/// 基准测试服务端
/// </summary>
public static class BenchmarkServer
{
    /// <summary>
    /// 运行基准测试服务端
    /// </summary>
    public static async Task RunAsync(int tcpPort, CancellationToken cancellationToken)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Logging.AddConsole();
        builder.Logging.SetMinimumLevel(LogLevel.Information);

        // 注册 Hub 实现
        builder.Services.AddSingleton<BenchmarkHubImpl>();

        // 配置 PulseRPC 服务器
        builder.Services.AddPulseServer(options =>
        {
            options.UsePreset(ServerPreset.LowLatency);
            options.AddTcp(tcpPort);
        });

        // 注册 Hub
        builder.Services.AddSingleton<IBenchmarkHub>(sp => sp.GetRequiredService<BenchmarkHubImpl>());

        var host = builder.Build();
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("BenchmarkServer");

        logger.LogInformation("基准测试服务端已启动，TCP端口: {TcpPort}", tcpPort);
        logger.LogInformation("按 Ctrl+C 停止服务端");

        await host.RunAsync(cancellationToken);
    }
}
