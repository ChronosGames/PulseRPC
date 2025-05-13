using System.Buffers.Binary;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PulseRPC.Protocol.Messages;
using PulseRPC.Protocol.Network;

namespace PulseRPC.Server;

public class PulseServerStartup(IServiceProvider serviceProvider, ILogger<PulseServerStartup> logger)
{
    private HandlerRegistry? _handlerRegistry;
    private HandlerScanner? _handlerScanner;
    private IMessageDispatcher? _messageDispatcher;
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

        // 5. 创建消息分发器
        _messageDispatcher = serviceProvider.GetRequiredService<IMessageDispatcher>();

        // 6. 初始化网络服务器
        _networkServer = serviceProvider.GetRequiredService<NetworkServer>();
        // await _networkServer.StartAsync(cancellationToken);
        _networkServer.Start(new IPEndPoint(IPAddress.Any, 8888));

        // 7. 注册网络消息处理回调
        // _networkServer.SetMessageHandler(HandleNetworkMessageAsync);

        logger.LogInformation("游戏服务器初始化完成");
    }

    // private async Task HandleNetworkMessageAsync(
    //     IPacket message, NetworkSession session, CancellationToken cancellationToken)
    // {
    //     try
    //     {
    //         // 解析消息头
    //         ReadOnlySpan<byte> headerSpan = message.Data.Span.Slice(0, 4);
    //
    //         // 前2字节为消息ID
    //         var messageId = BinaryPrimitives.ReadUInt16LittleEndian(headerSpan[..2]);
    //
    //         // 第3字节包含标志位（例如是否为请求/响应消息）
    //         var flags = headerSpan[2];
    //         var isRequest = (flags & 0x01) != 0;
    //
    //         // 从第4字节开始是实际消息内容
    //         Memory<byte> messageContent = message.Data.Slice(4);
    //
    //         // 分发消息到处理器
    //         await _messageDispatcher.DispatchMessageAsync(
    //             messageId, messageContent, session, isRequest, cancellationToken);
    //     }
    //     catch (Exception ex)
    //     {
    //         _logger.LogError(ex, "处理网络消息时出错");
    //     }
    // }

    public void Update()
    {
        // 在主线程更新循环中处理主线程队列中的任务
        //_threadPoolManager!.ProcessMainThreadTasks(20); // 每帧最多处理20个任务
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
