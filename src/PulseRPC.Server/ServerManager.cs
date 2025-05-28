using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using MemoryPack;
using Microsoft.Extensions.Logging;
using PulseRPC.Messaging;
using PulseRPC.Serialization;
using PulseRPC.Server.Channels;
using PulseRPC.Server.Services;
using PulseRPC.Transport;

namespace PulseRPC.Server;

/// <summary>
/// 服务器管理器接口
/// </summary>
public interface IServerManager : IDisposable
{
    /// <summary>
    /// 添加传输
    /// </summary>
    void AddTransport(
        string channelName,
        TransportType transportType,
        int port,
        TransportOptions? options = null,
        bool isDefault = false);

    /// <summary>
    /// 启动服务器
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止服务器
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 高性能服务器管理器
/// </summary>
public class ServerManager : IServerManager
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ServiceRegistry _serviceRegistry;
    private readonly ISerializerProvider _serializerProvider;
    private readonly ILogger<ServerManager> _logger;
    private readonly Dictionary<string, TransportInfo> _transports = new();
    private readonly IServerChannelManager _channelManager;
    private bool _isRunning;

    // 性能统计
    private long _totalRequests;
    private long _totalBytes;
    private long _successRequests;
    private long _failedRequests;
    private long _processingTimeTotal; // 毫秒

    // 空对象实例，用于替代null
    private static readonly object EmptyObject = new();

    // 消息池 - 减少内存分配
    private readonly ObjectPool<Messaging.MessageHeader> _headerPool;
    private readonly ArrayPool<byte> _arrayPool = ArrayPool<byte>.Shared;

    public ServerManager(
        ServiceRegistry serviceRegistry,
        ISerializerProvider serializerProvider,
        IServerChannelManager serverChannelManager,
        ILoggerFactory loggerFactory)
    {
        _serviceRegistry = serviceRegistry;
        _serializerProvider = serializerProvider;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<ServerManager>();
        _channelManager = serverChannelManager;

        // 初始化对象池
        _headerPool = new ObjectPool<Messaging.MessageHeader>(() => new Messaging.MessageHeader(), 100);
    }

    /// <summary>
    /// 添加传输层
    /// </summary>
    public void AddTransport(
        string channelName,
        TransportType transportType,
        int port,
        TransportOptions? options = null,
        bool isDefault = false)
    {
        if (_isRunning)
        {
            throw new InvalidOperationException("服务器运行中，无法添加传输");
        }

        if (_transports.ContainsKey(channelName))
        {
            throw new ArgumentException($"通道已存在: {channelName}");
        }

        // 创建传输信息
        var transportInfo = new TransportInfo
        {
            Name = channelName,
            Type = transportType,
            Port = port,
            Options = options ?? new TransportOptions(),
            IsDefault = isDefault
        };

        _transports.Add(channelName, transportInfo);

        _logger.LogInformation("已添加 {Type} 传输: {Name}, 端口: {Port}",
            transportType, channelName, port);
    }

    /// <summary>
    /// 启动服务器
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
            return;

        _logger.LogInformation("正在启动服务器...");

        try
        {
            // 创建传输工厂
            var transportFactory = new TransportFactory(_loggerFactory);

            // 启动所有传输
            foreach (var transport in _transports.Values)
            {
                // 创建服务器监听器
                var listener = await transportFactory.CreateServerListenerAsync(
                    transport.Type,
                    transport.Port,
                    transport.Options);

                // 创建服务器通道
                var channel = new ServerTransportChannel(
                    transport.Name,
                    listener,
                    _serializerProvider,
                    _loggerFactory.CreateLogger<ServerTransportChannel>());

                // 添加消息处理器
                channel.MessageReceived += OnMessageReceived;

                // 注册通道
                _channelManager.RegisterChannel(transport.Name, channel, transport.IsDefault);

                // 启动监听器
                await listener.StartAsync(cancellationToken);

                _logger.LogInformation("已启动 {Type} 传输: {Name}, 端口: {Port}",
                    transport.Type, transport.Name, transport.Port);
            }

            _isRunning = true;
            _logger.LogInformation("服务器已启动");

            // 定期报告性能统计
            _ = Task.Run(async () =>
            {
                while (_isRunning)
                {
                    await Task.Delay(10000); // 每10秒报告一次

                    var stats = GetPerformanceStats();
                    if (stats.TotalRequests > 0)
                    {
                        var avgTime = stats.TotalRequests > 0
                            ? (double)stats.ProcessingTimeTotal / stats.TotalRequests
                            : 0;

                        _logger.LogInformation(
                            "性能统计 - 请求: {TotalRequests}, 成功: {SuccessRequests}, " +
                            "失败: {FailedRequests}, 平均处理时间: {AvgTime:F2}ms, 总流量: {TotalMB:F2}MB",
                            stats.TotalRequests,
                            stats.SuccessRequests,
                            stats.FailedRequests,
                            avgTime,
                            stats.TotalBytes / (1024.0 * 1024.0));
                    }
                }
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动服务器失败");

            // 如果启动失败，清理已启动的通道
            await StopAsync(cancellationToken);

            throw;
        }
    }

    /// <summary>
    /// 停止服务器
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_isRunning)
            return;

        _logger.LogInformation("正在停止服务器...");

        _isRunning = false;

        // 释放通道资源
        _channelManager.Dispose();

        _logger.LogInformation("服务器已停止");

        await Task.CompletedTask;
    }

    /// <summary>
    /// 处理接收到的消息
    /// </summary>
    private async void OnMessageReceived(object? sender, MessageReceivedEventArgs e)
    {
        var startTime = DateTime.UtcNow;
        bool success = false;

        try
        {
            var channel = sender as IServerChannel;
            if (channel == null) return;

            var message = e.Message;

            // 更新统计信息
            Interlocked.Increment(ref _totalRequests);
            Interlocked.Add(ref _totalBytes, message.Body?.Length ?? 0);

            // 处理不同类型消息
            switch (message.Header.Type)
            {
                case MessageType.Request:
                    await HandleRequestAsync(channel, e.ClientId, message);
                    success = true;
                    break;

                case MessageType.Ping:
                    await HandlePingAsync(channel, e.ClientId, message);
                    success = true;
                    break;
            }
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _failedRequests);
            _logger.LogError(ex, "处理消息异常");
        }
        finally
        {
            if (success)
            {
                Interlocked.Increment(ref _successRequests);
            }

            // 更新处理时间统计
            var processingTime = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
            Interlocked.Add(ref _processingTimeTotal, processingTime);
        }
    }

    /// <summary>
    /// 处理请求消息 - 高性能路径
    /// </summary>
    private async Task HandleRequestAsync(IServerChannel channel, string clientId, NetworkMessage message)
    {
        var header = message.Header;

        try
        {
            // 直接访问消息体字节数组，避免重复序列化/反序列化
            var requestBytes = message.Body;

            // 未知服务或方法 - 使用高性能服务注册中心
            var result = await _serviceRegistry.InvokeMethodAsync(
                header.ServiceName,
                header.MethodName,
                requestBytes,
                CancellationToken.None);

            // 创建响应头 - 使用对象池
            var responseHeader = GetHeaderFromPool(MessageType.Response, header);

            // 发送响应 - 使用安全方法
            await channel.SendMessageAsync(clientId, responseHeader, result ?? EmptyObject);

            // 返回对象到池
            ReturnHeaderToPool(responseHeader);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "执行方法异常: {ServiceName}.{MethodName}",
                header.ServiceName, header.MethodName);

            // 创建错误响应 - 使用对象池
            var errorHeader = GetHeaderFromPool(MessageType.Response, header);

            var errorResponse = new ErrorResponse { ErrorCode = "SERVER_ERROR", ErrorMessage = ex.Message };

            // 发送错误响应
            await channel.SendMessageAsync(clientId, errorHeader, errorResponse);

            // 返回对象到池
            ReturnHeaderToPool(errorHeader);
        }
    }

    /// <summary>
    /// 从对象池获取响应头
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Messaging.MessageHeader GetHeaderFromPool(MessageType type, Messaging.MessageHeader requestHeader)
    {
        var header = _headerPool.Get();
        header.Type = type;
        header.MessageId = requestHeader.MessageId;
        header.ServiceName = requestHeader.ServiceName;
        header.MethodName = requestHeader.MethodName;
        return header;
    }

    /// <summary>
    /// 将对象返回到池
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ReturnHeaderToPool(Messaging.MessageHeader header)
    {
        _headerPool.Return(header);
    }

    /// <summary>
    /// 处理Ping消息
    /// </summary>
    private async Task HandlePingAsync(IServerChannel channel, string clientId, NetworkMessage message)
    {
        try
        {
            // 创建Pong响应
            var header = GetHeaderFromPool(MessageType.Pong, message.Header);

            // 发送Pong响应
            await channel.SendMessageAsync(clientId, header, EmptyObject);

            // 返回对象到池
            ReturnHeaderToPool(header);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送Pong响应失败");
        }
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (_isRunning)
        {
            // 停止服务器
            _ = StopAsync();
        }

        // 释放通道资源
        _channelManager.Dispose();
    }

    /// <summary>
    /// 获取性能统计
    /// </summary>
    public (long TotalRequests, long TotalBytes, long SuccessRequests, long FailedRequests, long ProcessingTimeTotal)
        GetPerformanceStats()
    {
        return (
            Interlocked.Read(ref _totalRequests),
            Interlocked.Read(ref _totalBytes),
            Interlocked.Read(ref _successRequests),
            Interlocked.Read(ref _failedRequests),
            Interlocked.Read(ref _processingTimeTotal)
        );
    }

    /// <summary>
    /// 传输信息
    /// </summary>
    private class TransportInfo
    {
        public required string Name { get; init; }
        public required TransportType Type { get; init; }
        public required int Port { get; init; }
        public required TransportOptions Options { get; init; }
        public required bool IsDefault { get; init; }
    }

    /// <summary>
    /// 错误响应
    /// </summary>
    private class ErrorResponse
    {
        public required string ErrorCode { get; init; }
        public required string ErrorMessage { get; init; }
    }

    /// <summary>
    /// 简单对象池实现
    /// </summary>
    private class ObjectPool<T> where T : class
    {
        private readonly ConcurrentStack<T> _objects = new();
        private readonly Func<T> _objectFactory;
        private readonly int _maxSize;

        public ObjectPool(Func<T> objectFactory, int maxSize)
        {
            _objectFactory = objectFactory ?? throw new ArgumentNullException(nameof(objectFactory));
            _maxSize = maxSize;
        }

        public T Get()
        {
            if (_objects.TryPop(out var item))
            {
                return item;
            }

            return _objectFactory();
        }

        public void Return(T obj)
        {
            if (obj == null) return;

            // 如果池已满，则不返回对象
            if (_objects.Count < _maxSize)
            {
                _objects.Push(obj);
            }
        }
    }
}
