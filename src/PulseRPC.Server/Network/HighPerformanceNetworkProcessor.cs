using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PulseRPC.Messaging;
using PulseRPC.Transport;

namespace PulseRPC.Server.Network;

/// <summary>
/// 高性能网络处理器 - 零拷贝字节流接收和解析
/// 使用 System.IO.Pipelines 和 System.Threading.Channels 优化
/// </summary>
public interface INetworkProcessor : IDisposable
{
    /// <summary>
    /// 启动网络处理器
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止网络处理器
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 处理传输数据
    /// </summary>
    ValueTask ProcessTransportDataAsync(string connectionId, ReadOnlySequence<byte> data);

    /// <summary>
    /// 消息解析完成事件
    /// </summary>
    event EventHandler<MessageParsedEventArgs> MessageParsed;
}

/// <summary>
/// 高性能网络处理器实现
/// </summary>
internal sealed class HighPerformanceNetworkProcessor : INetworkProcessor
{
    private readonly ILogger<HighPerformanceNetworkProcessor> _logger;
    private readonly NetworkProcessorOptions _options;

    // 高性能通道用于消息传递
    private readonly Channel<IncomingDataPacket> _incomingDataChannel;
    private readonly ChannelWriter<IncomingDataPacket> _incomingDataWriter;
    private readonly ChannelReader<IncomingDataPacket> _incomingDataReader;

    // 消息解析状态管理
    private readonly ConcurrentDictionary<string, ConnectionParsingState> _connectionStates = new();

    // 处理任务
    private Task[]? _processingTasks;
    private readonly CancellationTokenSource _shutdownCts = new();

    public event EventHandler<MessageParsedEventArgs>? MessageParsed;

    public HighPerformanceNetworkProcessor(
        NetworkProcessorOptions? options = null,
        ILogger<HighPerformanceNetworkProcessor>? logger = null)
    {
        _options = options ?? new NetworkProcessorOptions();
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<HighPerformanceNetworkProcessor>.Instance;

        // 创建无界高性能通道
        var channelOptions = new UnboundedChannelOptions
        {
            SingleReader = false, // 多个处理器线程
            SingleWriter = false, // 多个连接并发写入
            AllowSynchronousContinuations = false // 避免同步回调阻塞
        };

        _incomingDataChannel = Channel.CreateUnbounded<IncomingDataPacket>(channelOptions);
        _incomingDataWriter = _incomingDataChannel.Writer;
        _incomingDataReader = _incomingDataChannel.Reader;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("启动高性能网络处理器，处理器线程数: {ProcessorCount}", _options.ProcessorThreadCount);

        // 启动多个处理器线程进行并行处理
        _processingTasks = new Task[_options.ProcessorThreadCount];

        for (int i = 0; i < _options.ProcessorThreadCount; i++)
        {
            var processorId = i;
            _processingTasks[i] = Task.Run(async () => await ProcessIncomingDataAsync(processorId, _shutdownCts.Token));
        }

        _logger.LogInformation("网络处理器启动完成");
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("停止网络处理器");

        // 标记写入完成
        _incomingDataWriter.Complete();

        // 取消所有处理任务
        _shutdownCts.Cancel();

        // 等待所有处理任务完成
        if (_processingTasks != null)
        {
            try
            {
                await Task.WhenAll(_processingTasks).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("网络处理器停止超时");
            }
        }

        _logger.LogInformation("网络处理器停止完成");
    }

    /// <summary>
    /// 处理传输数据 - 高性能入口点
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask ProcessTransportDataAsync(string connectionId, ReadOnlySequence<byte> data)
    {
        if (data.IsEmpty)
            return;

        // 创建数据包并发送到处理通道
        var packet = new IncomingDataPacket(connectionId, data, DateTime.UtcNow);

        // 异步写入通道，避免阻塞网络线程
        if (!_incomingDataWriter.TryWrite(packet))
        {
            // 如果通道已满或关闭，记录警告
            _logger.LogWarning("无法写入数据包到处理通道，连接: {ConnectionId}, 数据长度: {Length}",
                connectionId, data.Length);
        }
    }

    /// <summary>
    /// 处理传入数据的主循环
    /// </summary>
    private async Task ProcessIncomingDataAsync(int processorId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("数据处理器 #{ProcessorId} 启动", processorId);

        try
        {
            await foreach (var packet in _incomingDataReader.ReadAllAsync(cancellationToken))
            {
                await ProcessDataPacketAsync(packet, processorId);
            }
        }
        catch (OperationCanceledException)
        {
            // 正常关闭
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "数据处理器 #{ProcessorId} 发生异常", processorId);
        }

        _logger.LogDebug("数据处理器 #{ProcessorId} 停止", processorId);
    }

    /// <summary>
    /// 处理单个数据包 - 支持分包和粘包处理
    /// </summary>
    private async Task ProcessDataPacketAsync(IncomingDataPacket packet, int processorId)
    {
        try
        {
            // 获取或创建连接解析状态
            var state = _connectionStates.GetOrAdd(packet.ConnectionId,
                _ => new ConnectionParsingState(packet.ConnectionId, _options.MaxMessageSize));

            // 将数据添加到连接缓冲区
            state.AppendData(packet.Data);

            // 尝试解析完整消息
            while (state.TryParseMessage(out var messagePacket))
            {
                // 触发消息解析事件
                var eventArgs = new MessageParsedEventArgs(
                    packet.ConnectionId,
                    messagePacket,
                    packet.ReceivedTime,
                    processorId);

                MessageParsed?.Invoke(this, eventArgs);

                _logger.LogTrace("解析消息完成: 连接={ConnectionId}, 消息ID={MessageId}, 处理器={ProcessorId}",
                    packet.ConnectionId, messagePacket.Header.MessageId, processorId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理数据包失败: 连接={ConnectionId}, 处理器={ProcessorId}",
                packet.ConnectionId, processorId);

            // 清理异常连接的状态
            _connectionStates.TryRemove(packet.ConnectionId, out _);
        }
    }

    public void Dispose()
    {
        if (!_shutdownCts.IsCancellationRequested)
        {
            StopAsync().GetAwaiter().GetResult();
        }

        _shutdownCts.Dispose();

        // 清理连接状态
        foreach (var state in _connectionStates.Values)
        {
            state.Dispose();
        }
        _connectionStates.Clear();
    }
}

/// <summary>
/// 传入数据包结构
/// </summary>
internal readonly struct IncomingDataPacket
{
    public readonly string ConnectionId;
    public readonly ReadOnlySequence<byte> Data;
    public readonly DateTime ReceivedTime;

    public IncomingDataPacket(string connectionId, ReadOnlySequence<byte> data, DateTime receivedTime)
    {
        ConnectionId = connectionId;
        Data = data;
        ReceivedTime = receivedTime;
    }
}

/// <summary>
/// 连接解析状态 - 处理分包和粘包
/// </summary>
internal sealed class ConnectionParsingState : IDisposable
{
    private readonly string _connectionId;
    private readonly int _maxMessageSize;
    private readonly Pipe _pipe;
    private readonly PipeWriter _writer;
    private readonly PipeReader _reader;

    public ConnectionParsingState(string connectionId, int maxMessageSize)
    {
        _connectionId = connectionId;
        _maxMessageSize = maxMessageSize;

        var pipeOptions = new PipeOptions(
            pauseWriterThreshold: maxMessageSize * 2,
            resumeWriterThreshold: maxMessageSize,
            readerScheduler: PipeScheduler.ThreadPool,
            writerScheduler: PipeScheduler.ThreadPool,
            useSynchronizationContext: false);

        _pipe = new Pipe(pipeOptions);
        _writer = _pipe.Writer;
        _reader = _pipe.Reader;
    }

    /// <summary>
    /// 追加数据到缓冲区
    /// </summary>
    public void AppendData(ReadOnlySequence<byte> data)
    {
        foreach (var segment in data)
        {
            var span = _writer.GetSpan(segment.Length);
            segment.Span.CopyTo(span);
            _writer.Advance(segment.Length);
        }

        _writer.FlushAsync().AsTask().Wait(TimeSpan.FromMilliseconds(100));
    }

    /// <summary>
    /// 尝试解析完整消息
    /// </summary>
    public bool TryParseMessage(out MessagePacket messagePacket)
    {
        messagePacket = default;

        if (!_reader.TryRead(out var result))
            return false;

        var buffer = result.Buffer;

        // 尝试解析消息包
        if (MessagePacket.TryReadFrom(buffer.ToArray(), out messagePacket))
        {
            // 计算消息总长度 (头部长度字段 + 头部数据 + 负载数据)
            var totalMessageLength = 4 + messagePacket.EstimateSize();

            // 推进读取位置
            _reader.AdvanceTo(buffer.GetPosition(totalMessageLength));
            return true;
        }

        // 数据不足，等待更多数据
        _reader.AdvanceTo(buffer.Start, buffer.End);
        return false;
    }

    public void Dispose()
    {
        _writer.Complete();
        _reader.Complete();
    }
}

/// <summary>
/// 消息解析事件参数
/// </summary>
public sealed class MessageParsedEventArgs : EventArgs
{
    public string ConnectionId { get; }
    public MessagePacket MessagePacket { get; }
    public DateTime ReceivedTime { get; }
    public int ProcessorId { get; }

    public MessageParsedEventArgs(string connectionId, MessagePacket messagePacket, DateTime receivedTime, int processorId)
    {
        ConnectionId = connectionId;
        MessagePacket = messagePacket;
        ReceivedTime = receivedTime;
        ProcessorId = processorId;
    }
}

/// <summary>
/// 网络处理器配置选项
/// </summary>
public sealed class NetworkProcessorOptions
{
    /// <summary>
    /// 处理器线程数量（默认为 CPU 核心数）
    /// </summary>
    public int ProcessorThreadCount { get; set; } = Environment.ProcessorCount;

    /// <summary>
    /// 最大消息大小（字节）
    /// </summary>
    public int MaxMessageSize { get; set; } = 16 * 1024 * 1024; // 16MB

    /// <summary>
    /// 管道缓冲区大小
    /// </summary>
    public int PipeBufferSize { get; set; } = 64 * 1024; // 64KB
}
