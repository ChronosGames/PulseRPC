using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PulseRPC.Network;

namespace PulseRPC.Server;

public class PulseServerStartup(IServiceProvider serviceProvider, ILogger<PulseServerStartup> logger)
{
    private HandlerRegistry? _handlerRegistry;
    private HandlerScanner? _handlerScanner;
    private HandlerThreadPoolManager? _threadPoolManager;
    private NetworkServer? _networkServer;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("初始化游戏服务器...");

        // 1. 创建并配置线程池管理器
        _threadPoolManager = serviceProvider.GetRequiredService<HandlerThreadPoolManager>();

        // 设置主线程上下文
        _threadPoolManager.Initialize();

        // 2. 创建处理器注册表
        _handlerRegistry = serviceProvider.GetRequiredService<HandlerRegistry>();

        // 3. 创建处理器扫描器
        _handlerScanner = serviceProvider.GetRequiredService<HandlerScanner>();

        // 4. 扫描并注册所有消息处理器
        _handlerScanner.ScanAllAssemblies();

        // 6. 初始化网络服务器
        _networkServer = serviceProvider.GetRequiredService<NetworkServer>();
        await _networkServer.StartAsync(new IPEndPoint(IPAddress.Any, 8888));

        logger.LogInformation("游戏服务器初始化完成");
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("关闭游戏服务器...");

        // 停止网络服务器
        if (_networkServer != null)
        {
            // await _networkServer.StopAsync(cancellationToken);
            _networkServer.Stop();
        }

        logger.LogInformation("游戏服务器已关闭");
    }
}
