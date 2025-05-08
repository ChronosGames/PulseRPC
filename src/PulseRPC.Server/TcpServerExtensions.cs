using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;

namespace PulseRPC.Server;

/// <summary>
/// TcpServer 扩展方法
/// </summary>
public static class TcpServerExtensions
{
    /// <summary>
    /// 添加 TcpServer 服务
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="ipAddress">监听地址，为空则使用任意地址</param>
    /// <param name="port">监听端口</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddTcpServer(
        this IServiceCollection services,
        string ipAddress = "",
        int port = 7000)
    {
        // 注册依赖服务
        services.AddPulseRpcMessageHandling();

        // 注册 TcpServer
        services.TryAddSingleton<TcpServer>(sp =>
        {
            var dispatcher = sp.GetRequiredService<MessageDispatcher>();
            var logger = sp.GetRequiredService<ILogger<TcpServer>>();
            return new TcpServer(ipAddress, port, dispatcher, logger);
        });

        // 注册服务器初始化器
        services.TryAddSingleton<ServerInitializer>();

        return services;
    }

    /// <summary>
    /// 添加 TcpServer 作为托管服务
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="ipAddress">监听地址，为空则使用任意地址</param>
    /// <param name="port">监听端口</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddTcpServerAsHostedService(
        this IServiceCollection services,
        string ipAddress = "",
        int port = 7000)
    {
        // 添加基础TcpServer服务
        services.AddTcpServer(ipAddress, port);

        // 注册托管服务
        services.AddSingleton<IHostedService, TcpServerHostedService>();

        return services;
    }

    /// <summary>
    /// 异步启动 TcpServer
    /// </summary>
    /// <param name="server">TcpServer 实例</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>表示服务器运行的任务</returns>
    public static Task StartAsync(this TcpServer server, CancellationToken cancellationToken = default)
    {
        return server.StartAsync();
    }

    /// <summary>
    /// 同步启动 TcpServer
    /// </summary>
    /// <param name="server">TcpServer 实例</param>
    public static void Start(this TcpServer server)
    {
        // 使用Task.Run启动服务器，但不等待其完成
        Task.Run(async () => await server.StartAsync());
    }
}
