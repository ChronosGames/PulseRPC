using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PulseRPC.Transport.Tcp;

/// <summary>
/// 传输层消息头结构（用于大包拆解）
/// 格式: [Magic:2][Length:4][MessageId:2][Flags:2] = 10 bytes
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct MessageHeader
{
    public readonly ushort Magic;      // 协议魔数 0x5052 ('PR')
    public readonly int Length;        // 消息体长度
    public readonly ushort MessageId;  // 消息ID
    public readonly ushort Flags;      // 消息标志

    public const int Size = 10; // 2 + 4 + 2 + 2

    // 标志位定义
    public const ushort FlagNone = 0x0000;
    public const ushort FlagLargePacket = 0x0001;
    public const ushort FlagCompressed = 0x0002;
    public const ushort FlagChunked = 0x0004;
    public const ushort FlagEndOfChunk = 0x0008;

    public MessageHeader(int length, ushort messageId, ushort flags = FlagNone)
        : this(ProtocolConstants.ProtocolMagic, length, messageId, flags)
    {
    }

    public MessageHeader(ushort magic, int length, ushort messageId, ushort flags)
    {
        Magic = magic;
        Length = length;
        MessageId = messageId;
        Flags = flags;
    }

    public bool IsValid => Magic == ProtocolConstants.ProtocolMagic;
    public bool IsHandshake => MessageId == ProtocolConstants.HandshakeMessageId;
    public bool IsLargePacket => (Flags & FlagLargePacket) != 0;
    public bool IsChunked => (Flags & FlagChunked) != 0;
    public bool IsEndOfChunk => (Flags & FlagEndOfChunk) != 0;
    public bool IsCompressed => (Flags & FlagCompressed) != 0;
}

/// <summary>
/// 块传输头
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct ChunkHeader
{
    public readonly int ChunkId;        // 块ID
    public readonly int ChunkIndex;     // 块索引
    public readonly int TotalChunks;    // 总块数
    public readonly int ChunkSize;      // 本块大小

    public const int Size = 16;

    public ChunkHeader(int chunkId, int chunkIndex, int totalChunks, int chunkSize)
    {
        ChunkId = chunkId;
        ChunkIndex = chunkIndex;
        TotalChunks = totalChunks;
        ChunkSize = chunkSize;
    }
}

/// <summary>
/// TCP传输基类，提供TCP连接的基础功能
/// </summary>
public abstract class TcpTransport : ITransport
{
    protected readonly TcpTransportOptions _options;
    protected readonly ILogger _logger;
    protected readonly byte[] _receiveBuffer;
    protected readonly LargePacketHandler _packetHandler;
    protected readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
    protected readonly byte[] _headerBuffer; // 可重用的头部缓冲区

    protected int _nextChunkId;
    protected Socket? _socket;
    protected NetworkStream? _stream;
    protected ConnectionState _state;
    protected CancellationTokenSource _cts;
    protected Task? _receiveTask;
    protected readonly object _stateLock = new object();
    protected long _totalBytesSent;
    protected long _totalBytesReceived;
    protected bool _disposed;
    protected bool _handshakeCompleted;

    public virtual string Id => throw new NotImplementedException();
    public TransportType Type => TransportType.TCP;
    public bool IsConnected => _state == ConnectionState.Connected && _socket?.Connected == true;
    public ConnectionState State => _state;

    public EndPoint LocalEndPoint => _socket?.LocalEndPoint!;
    public EndPoint RemoteEndPoint => _socket?.RemoteEndPoint!;

    public long TotalBytesSent => Interlocked.Read(ref _totalBytesSent);
    public long TotalBytesReceived => Interlocked.Read(ref _totalBytesReceived);

    public event EventHandler<TransportStateEventArgs>? StateChanged;
    public event EventHandler<TransportDataEventArgs>? DataReceived;

    protected TcpTransport(TcpTransportOptions? options = null, ILogger? logger = null)
    {
        _options = options ?? new TcpTransportOptions();
        _logger = logger ?? NullLogger.Instance;
        _receiveBuffer = new byte[_options.RecvBufferSize];
        _headerBuffer = new byte[MessageHeader.Size + ChunkHeader.Size]; // 预分配可重用缓冲区
        _packetHandler = new LargePacketHandler();
        _nextChunkId = 1;
        _cts = new CancellationTokenSource();
    }

    /// <summary>
    /// 发送数据 - 使用流式分片避免大对象分配
    /// </summary>
    public virtual async Task<bool> SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (!IsConnected || _stream == null)
        {
            return false;
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);

        // 小包直接发送
        if (data.Length <= _options.SmallPacketThreshold)
        {
            return await SendSmallPacketAsync(data, linkedCts.Token);
        }

        // 大包使用流式分片发送
        return await SendLargePacketStreamingAsync(data, linkedCts.Token);
    }

    /// <summary>
    /// 发送小包数据
    /// </summary>
    private async Task<bool> SendSmallPacketAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            // 创建消息头
            var header = new MessageHeader(data.Length, 0, MessageHeader.FlagNone);

            // 使用 AsyncSpanHelper 避免在 async 方法中直接操作 Span
            var success = await AsyncSpanHelper.SendSmallPacketAsync(
                _stream!, _headerBuffer, data, header, cancellationToken);

            if (success)
            {
                Interlocked.Add(ref _totalBytesSent, MessageHeader.Size + data.Length);
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送小包数据失败");
            return false;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>
    /// 流式发送大包 - 避免创建临时分片数组
    /// </summary>
    private async Task<bool> SendLargePacketStreamingAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        var chunkId = Interlocked.Increment(ref _nextChunkId);
        var totalChunks = (data.Length + _options.ChunkSize - 1) / _options.ChunkSize;

        try
        {
            for (var i = 0; i < totalChunks; i++)
            {
                var offset = i * _options.ChunkSize;
                var chunkSize = Math.Min(_options.ChunkSize, data.Length - offset);
                var chunkData = data.Slice(offset, chunkSize);

                var flags = MessageHeader.FlagChunked;
                if (i == totalChunks - 1)
                    flags |= MessageHeader.FlagEndOfChunk;

                var messageHeader = new MessageHeader(
                    ChunkHeader.Size + chunkSize,
                    0, // messageId
                    flags);

                var chunkHeader = new ChunkHeader(chunkId, i, totalChunks, chunkSize);

                await _sendLock.WaitAsync(cancellationToken);
                try
                {
                    // 使用 AsyncSpanHelper 发送分片数据
                    var success = await AsyncSpanHelper.SendChunkAsync(
                        _stream!, _headerBuffer, chunkData, messageHeader, chunkHeader, cancellationToken);

                    if (success)
                    {
                        Interlocked.Add(ref _totalBytesSent, MessageHeader.Size + ChunkHeader.Size + chunkSize);
                    }
                    else
                    {
                        return false;
                    }
                }
                finally
                {
                    _sendLock.Release();
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "流式发送大包数据失败");
            return false;
        }
    }

    /// <summary>
    /// 接收循环 - 使用优化的分片处理器
    /// </summary>
    protected async Task ReceiveLoopAsync()
    {
        try
        {
            var headerBuffer = new byte[MessageHeader.Size];

            while (!_cts.IsCancellationRequested && IsConnected)
            {
                // 读取消息头
                if (!await ReadExactBytesAsync(headerBuffer, 0, MessageHeader.Size))
                {
                    break;
                }

                var header = AsyncSpanHelper.ReadMessageHeaderSync(headerBuffer.AsSpan());

                // 验证魔数
                if (!header.IsValid)
                {
                    var remoteEndpoint = _socket?.RemoteEndPoint?.ToString() ?? "Unknown";
                    _logger.LogWarning(
                        "收到无效的协议魔数: 0x{Magic:X4} (期望: 0x{Expected:X4}) 来自 {RemoteEndpoint}，可能是协议不匹配或探针连接，断开连接",
                        header.Magic, ProtocolConstants.ProtocolMagic, remoteEndpoint);
                    break;  // 断开连接
                }

                // 验证长度
                if (header.Length <= 0 || header.Length > _options.RecvBufferSize * 2)
                {
                    var remoteEndpoint = _socket?.RemoteEndPoint?.ToString() ?? "Unknown";
                    _logger.LogWarning(
                        "收到无效的消息长度: {Length} 来自 {RemoteEndpoint}，断开连接",
                        header.Length, remoteEndpoint);
                    break;  // 断开连接
                }

                // 读取消息内容
                var messageBuffer = header.Length <= _receiveBuffer.Length
                    ? _receiveBuffer : new byte[header.Length];

                if (!await ReadExactBytesAsync(messageBuffer, 0, header.Length))
                {
                    break;
                }

                Interlocked.Add(ref _totalBytesReceived, MessageHeader.Size + header.Length);

                // 检查是否为握手消息
                if (header.IsHandshake)
                {
                    await HandleHandshakeMessageAsync(header, new ReadOnlyMemory<byte>(messageBuffer, 0, header.Length));
                    continue;
                }

                // 如果握手未完成，拒绝处理业务消息
                if (!_handshakeCompleted)
                {
                    _logger.LogWarning("收到业务消息但握手未完成，断开连接");
                    break;
                }

                // 处理消息
                if (header.IsChunked)
                {
                    await ProcessChunkedMessageAsync(header, new ReadOnlyMemory<byte>(messageBuffer, 0, header.Length));
                }
                else
                {
                    // 直接触发数据接收事件
                    DataReceived?.Invoke(this, new TransportDataEventArgs(new ReadOnlyMemory<byte>(messageBuffer, 0, header.Length)));
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消
        }
        catch (Exception ex) when (ex is SocketException || ex is IOException)
        {
            if (!_cts.IsCancellationRequested)
            {
                ChangeState(ConnectionState.Disconnected, $"连接断开: {ex.Message}", ex);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "接收循环异常");
            ChangeState(ConnectionState.Disconnected, $"接收异常: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 处理分片消息 - 使用优化的处理器
    /// </summary>
    private Task ProcessChunkedMessageAsync(MessageHeader header, ReadOnlyMemory<byte> data)
    {
        try
        {
            if (data.Length < ChunkHeader.Size)
            {
                _logger.LogWarning("分片数据太小，无法包含块头");
                return Task.CompletedTask;
            }

            // 读取块头
            var chunkHeader = AsyncSpanHelper.ReadChunkHeaderSync(data.Span[..ChunkHeader.Size]);
            var chunkData = data[ChunkHeader.Size..];

            // 验证块大小
            if (chunkData.Length != chunkHeader.ChunkSize)
            {
                _logger.LogWarning("分块大小不匹配: 期望 {Expected}, 实际 {Actual}", chunkHeader.ChunkSize, chunkData.Length);
                return Task.CompletedTask;
            }

            // 使用优化的分片处理器
            if (_packetHandler.ProcessChunk(chunkHeader, chunkData.Span, out var completeData))
            {
                // 大包重组完成，触发数据接收事件
                DataReceived?.Invoke(this, new TransportDataEventArgs(completeData));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理分片消息异常");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 处理握手消息 - 由子类实现
    /// </summary>
    protected virtual Task HandleHandshakeMessageAsync(MessageHeader header, ReadOnlyMemory<byte> data)
    {
        // 默认实现：忽略握手消息（用于不需要握手的场景）
        _handshakeCompleted = true;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 发送握手请求
    /// </summary>
    protected async Task<bool> SendHandshakeRequestAsync(string clientName, CancellationToken cancellationToken = default)
    {
        try
        {
            var handshake = new HandshakeMessage(ProtocolConstants.CurrentProtocolVersion, clientName);
            var handshakeData = handshake.ToBytes();

            var header = new MessageHeader(
                handshakeData.Length,
                ProtocolConstants.HandshakeMessageId,
                ProtocolConstants.HandshakeRequestFlag);

            await SendMessageAsync(header, handshakeData, cancellationToken);
            _logger.LogDebug("已发送握手请求: ClientName={ClientName}, ProtocolVersion={Version}",
                clientName, ProtocolConstants.CurrentProtocolVersion);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送握手请求失败");
            return false;
        }
    }

    /// <summary>
    /// 发送握手响应
    /// </summary>
    protected async Task SendHandshakeResponseAsync(bool accepted, string? reason = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = new HandshakeResponse(accepted, ProtocolConstants.CurrentProtocolVersion, reason);
            var responseData = response.ToBytes();

            var header = new MessageHeader(
                responseData.Length,
                ProtocolConstants.HandshakeMessageId,
                ProtocolConstants.HandshakeResponseFlag);

            await SendMessageAsync(header, responseData, cancellationToken);
            _logger.LogDebug("已发送握手响应: Accepted={Accepted}, Reason={Reason}", accepted, reason ?? "N/A");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送握手响应失败");
        }
    }

    /// <summary>
    /// 发送消息（带消息头）
    /// </summary>
    private async Task SendMessageAsync(MessageHeader header, byte[] data, CancellationToken cancellationToken = default)
    {
        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            // 发送消息头
            var headerBytes = new byte[MessageHeader.Size];
            AsyncSpanHelper.WriteMessageHeaderSync(headerBytes, header);
            await _stream!.WriteAsync(headerBytes, 0, MessageHeader.Size, cancellationToken);

            // 发送消息体
            await _stream.WriteAsync(data, 0, data.Length, cancellationToken);
            await _stream.FlushAsync(cancellationToken);

            Interlocked.Add(ref _totalBytesSent, MessageHeader.Size + data.Length);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>
    /// 启动清理任务
    /// </summary>
    protected void StartCleanupTask()
    {
        _ = Task.Run(async () =>
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), _cts.Token);
                    _packetHandler.CleanupExpiredPackets(TimeSpan.FromMinutes(5));
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "清理过期包异常");
                }
            }
        }, _cts.Token);
    }

    /// <summary>
    /// 读取指定字节数
    /// </summary>
    protected async Task<bool> ReadExactBytesAsync(byte[] buffer, int offset, int count)
    {
        if (_stream == null)
        {
            return false;
        }

        var totalRead = 0;
        while (totalRead < count)
        {
            var read = await _stream.ReadAsync(buffer, offset + totalRead, count - totalRead, _cts.Token);
            if (read == 0)
            {
                return false; // 连接关闭
            }

            totalRead += read;
        }

        return true;
    }


    /// <summary>
    /// 更改连接状态
    /// </summary>
    protected void ChangeState(ConnectionState newState, string? reason = null, Exception? exception = null)
    {
        var oldState = _state;
        if (oldState == newState)
            return;

        _state = newState;

        _logger.LogInformation("传输状态变更: {OldState} -> {NewState} ({Reason})", oldState, newState, reason ?? "未指定原因");

        StateChanged?.Invoke(this, new TransportStateEventArgs(this.Id, oldState, newState, reason, exception));
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public virtual void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _cts.Cancel();

        try
        {
            _stream?.Dispose();
            _socket?.Dispose();
            _packetHandler?.Dispose();
            _sendLock?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "关闭资源异常");
        }

        _cts.Dispose();
    }
}
