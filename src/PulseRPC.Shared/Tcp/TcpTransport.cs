using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC.Shared.Security;
using PulseRPC.Diagnostics;

namespace PulseRPC.Shared.Tcp;

/// <summary>
/// 传输层消息头结构（用于大包拆解）
/// 格式: [Magic:2][Length:4][MessageId:2][Flags:2] = 10 bytes
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct FrameHeader
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
    public const ushort FlagEncrypted = 0x0010;

    public FrameHeader(int length, ushort messageId)
        : this(length, messageId, FlagNone)
    {
    }

    public FrameHeader(int length, ushort messageId, ushort flags)
        : this(ProtocolConstants.ProtocolMagic, length, messageId, flags)
    {
    }

    public FrameHeader(ushort magic, int length, ushort messageId, ushort flags)
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
    public bool IsEncrypted => (Flags & FlagEncrypted) != 0;
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
/// 发送项 - 封装一个完整帧（单帧收发，不再有应用层分片）。
/// 使用 readonly struct 避免堆分配。
/// </summary>
/// <remarks>
/// 缓冲区布局为「头部预留区 + 消息体」：
/// <c>Buffer[0 .. FrameHeader.Size)</c> 预留给帧头（由发送循环写入），
/// <c>Buffer[FrameHeader.Size .. FrameHeader.Size + BodyLength)</c> 为消息体。
/// 发送时一次性写出 <c>Buffer[0 .. FrameHeader.Size + BodyLength)</c>，
/// 避免头/体两次写入产生的额外 TCP 分段。
/// </remarks>
internal readonly struct TcpSendItem
{
    /// <summary>池化缓冲区（含头部预留区 + 消息体）。</summary>
    public readonly byte[] Buffer;
    /// <summary>消息体长度（不含头部）。</summary>
    public readonly int BodyLength;
    /// <summary>帧头 MessageId（业务消息为 0，握手为特殊值）。</summary>
    public readonly ushort MessageId;
    /// <summary>帧头标志位。</summary>
    public readonly ushort Flags;
    private readonly ArrayPool<byte> _bufferPool;
    private readonly TaskCompletionSource<bool> _completion;

    public Task<bool> Completion => _completion.Task;

    public TcpSendItem(
        byte[] buffer,
        int bodyLength,
        ArrayPool<byte> bufferPool,
        ushort messageId = 0,
        ushort flags = FrameHeader.FlagNone)
    {
        Buffer = buffer;
        BodyLength = bodyLength;
        MessageId = messageId;
        Flags = flags;
        _bufferPool = bufferPool;
        _completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>
    /// 归还缓冲区到池
    /// </summary>
    public void ReturnBuffer()
    {
        if (Buffer != null)
        {
            _bufferPool.Return(Buffer);
        }
    }

    public void Complete(bool sent) => _completion.TrySetResult(sent);
}

/// <summary>
/// TCP传输基类，提供TCP连接的基础功能
/// 使用发送队列替代发送锁，支持高并发发送
/// </summary>
public abstract class TcpTransport : ITransport
{
    protected readonly TcpTransportOptions _options;
    protected readonly ILogger _logger;
    protected readonly byte[] _receiveBuffer;
    protected readonly LargePacketHandler _packetHandler;
    protected readonly byte[] _headerBuffer; // 可重用的头部缓冲区（仅发送任务使用）

    // 发送队列（替代 _sendLock）
    private readonly Channel<TcpSendItem> _sendQueue;
    private readonly ArrayPool<byte> _sendBufferPool;
    private Task? _sendTask;
    private readonly IRuntimeQueueMetricsRegistration _sendQueueMetrics;

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
    private TransportWireOffer _wireOffer;
    private TransportWireSession? _wireSession;

    public abstract string Id { get; }
    public TransportType Type => TransportType.TCP;
    public bool IsConnected => _state == ConnectionState.Connected && _socket?.Connected == true;
    public ConnectionState State => _state;

    public EndPoint LocalEndPoint => _socket?.LocalEndPoint!;
    public EndPoint RemoteEndPoint => _socket?.RemoteEndPoint!;

    public long TotalBytesSent => Interlocked.Read(ref _totalBytesSent);
    public long TotalBytesReceived => Interlocked.Read(ref _totalBytesReceived);

    public event EventHandler<TransportStateEventArgs>? StateChanged;
    public event EventHandler<TransportDataEventArgs>? DataReceived;

    protected TcpTransport()
        : this(null, null, ArrayPool<byte>.Shared)
    {
    }

    protected TcpTransport(TcpTransportOptions? options)
        : this(options, null, ArrayPool<byte>.Shared)
    {
    }

    protected TcpTransport(TcpTransportOptions? options, ILogger? logger)
        : this(options, logger, ArrayPool<byte>.Shared)
    {
    }

    protected TcpTransport(
        TcpTransportOptions? options,
        ILogger? logger,
        ArrayPool<byte> sendBufferPool)
    {
        _options = options ?? new TcpTransportOptions();
        TransportWireNegotiator.ValidateOptions(_options);
        _logger = logger ?? NullLogger.Instance;
        _sendBufferPool = sendBufferPool ?? throw new ArgumentNullException(nameof(sendBufferPool));
        _receiveBuffer = new byte[_options.RecvBufferSize];
        _headerBuffer = new byte[FrameHeader.Size + ChunkHeader.Size]; // 预分配可重用缓冲区（仅发送任务使用）
        _packetHandler = new LargePacketHandler(
            bufferPool: null,
            maxConcurrentPackets: 64,
            maxPacketSize: _options.MaxPacketSize);
        _cts = new CancellationTokenSource();

        // 初始化发送队列
        _sendQueue = Channel.CreateBounded<TcpSendItem>(new BoundedChannelOptions(_options.SendQueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,   // 单线程发送
            SingleWriter = false,  // 多线程入队
            AllowSynchronousContinuations = false
        });
        _sendQueueMetrics = RuntimeQueueMetrics.Register(
            "transport.tcp.send",
            $"{GetType().Name}:{Guid.NewGuid():N}",
            _options.SendQueueCapacity,
            () => _sendQueue.Reader.Count);
    }

    /// <summary>
    /// 启动发送任务（子类在连接建立后调用）
    /// </summary>
    protected void StartSendTask()
    {
        if (_sendTask is { IsCompleted: false })
        {
            return;
        }

        _sendTask = Task.Run(SendLoopAsync);
    }

    /// <summary>
    /// 发送数据 - 使用有界发送队列实现并发背压，并等待底层写入完成
    /// </summary>
    public virtual async Task<bool> SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (!IsConnected || _stream == null)
        {
            return false;
        }

        // 如果发送任务尚未启动（握手阶段），使用直接发送
        if (_sendTask == null)
        {
            return await SendDirectAsync(data, cancellationToken);
        }

        // 单包最大尺寸上限校验：拒绝超大消息，避免接收端因超限而断开连接
        if (data.Length <= 0 || data.Length > _options.MaxPacketSize)
        {
            _logger.LogError(
                "发送数据长度非法: {Length} 字节 (允许范围 1..{Max})，已拒绝发送",
                data.Length, _options.MaxPacketSize);
            return false;
        }

        byte[]? buffer = null;
        try
        {
            // 统一以单帧发送：传输层不再做应用层分片。
            // 租用「头部预留区 + 消息体」缓冲，消息体拷贝至头部之后，
            // 由单线程发送任务写入帧头并一次性写出（header+body 合并）。
            var transformed = _wireSession?.HasTransforms == true;
            var wirePayload = transformed
                ? _wireSession!.Encode(data.Span)
                : default;
            var bodyLength = transformed ? wirePayload.Data.Length : data.Length;
            buffer = _sendBufferPool.Rent(FrameHeader.Size + bodyLength);
            if (transformed)
                wirePayload.Data.CopyTo(buffer, FrameHeader.Size);
            else
                data.Span.CopyTo(buffer.AsSpan(FrameHeader.Size));
            var frameFlags = transformed ? ToFrameFlags(wirePayload.Flags) : FrameHeader.FlagNone;
            var item = new TcpSendItem(buffer, bodyLength, _sendBufferPool, flags: frameFlags);

            // 尝试同步入队（避免异步开销）
            if (_sendQueue.Writer.TryWrite(item))
            {
                buffer = null;
                _sendQueueMetrics.Observe();
            }
            else
            {
                // 队列满时异步等待。只有成功入队后才转移缓冲区所有权。
                _sendQueueMetrics.Observe();
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
                var waitStart = Stopwatch.GetTimestamp();
                await _sendQueue.Writer.WriteAsync(item, linkedCts.Token);
                _sendQueueMetrics.RecordEnqueueWait(TimeSpan.FromSeconds(
                    (double)(Stopwatch.GetTimestamp() - waitStart) / Stopwatch.Frequency));
                buffer = null;
            }

            // true 表示底层 NetworkStream 写入已完成，而不只是进入发送队列。
            return await item.Completion.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送数据入队失败");
            return false;
        }
        finally
        {
            if (buffer != null)
            {
                _sendBufferPool.Return(buffer);
            }
        }
    }

    /// <summary>
    /// 直接发送（握手阶段使用，发送任务启动前）
    /// </summary>
    private async Task<bool> SendDirectAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        if (_stream == null) return false;

        try
        {
            if (_wireSession?.HasTransforms != true)
            {
                var plainHeader = new FrameHeader(data.Length, 0, FrameHeader.FlagNone);
                var plainSuccess = await AsyncSpanHelper.SendSmallPacketAsync(
                    _stream, _headerBuffer, data, plainHeader, cancellationToken);
                if (plainSuccess)
                    Interlocked.Add(ref _totalBytesSent, FrameHeader.Size + data.Length);
                return plainSuccess;
            }

            var wirePayload = _wireSession.Encode(data.Span);
            var header = new FrameHeader(wirePayload.Data.Length, 0, ToFrameFlags(wirePayload.Flags));
            var success = await AsyncSpanHelper.SendSmallPacketAsync(
                _stream, _headerBuffer, wirePayload.Data, header, cancellationToken);

            if (success)
            {
                Interlocked.Add(ref _totalBytesSent, FrameHeader.Size + wirePayload.Data.Length);
            }
            return success;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _cts.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "直接发送数据失败");
            return false;
        }
    }

    /// <summary>
    /// 发送循环 - 单线程顺序发送（保证 TCP 字节流顺序）
    /// </summary>
    private async Task SendLoopAsync()
    {
        _logger.LogDebug("发送任务启动");

        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                TcpSendItem item;
                try
                {
                    item = await _sendQueue.Reader.ReadAsync(_cts.Token);
                    _sendQueueMetrics.Observe();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ChannelClosedException)
                {
                    break;
                }

                var sent = false;
                try
                {
                    sent = await SendFrameInternalAsync(item).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "发送数据失败");
                }
                finally
                {
                    item.Complete(sent);
                    // 归还缓冲区
                    item.ReturnBuffer();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送循环异常退出");
        }
        finally
        {
            while (_sendQueue.Reader.TryRead(out var pendingItem))
            {
                pendingItem.Complete(false);
                pendingItem.ReturnBuffer();
            }
        }

        _logger.LogDebug("发送任务停止");
    }

    /// <summary>
    /// 内部发送单帧（由发送任务调用，无锁）。
    /// 缓冲区已按「头部预留区 + 消息体」布局，此处写入帧头后一次性写出整帧。
    /// </summary>
    private async Task<bool> SendFrameInternalAsync(TcpSendItem item)
    {
        if (_stream == null) return false;

        try
        {
            var header = new FrameHeader(item.BodyLength, item.MessageId, item.Flags);

            // 将帧头写入缓冲区头部预留区（同步、无分配）
            AsyncSpanHelper.WriteFrameHeaderSync(
                item.Buffer.AsSpan(0, FrameHeader.Size), header);

            var totalLength = FrameHeader.Size + item.BodyLength;

            // 一次性写出整帧（header+body 合并，减少 TCP 分段；不再逐包 Flush）
            await _stream.WriteAsync(new ReadOnlyMemory<byte>(item.Buffer, 0, totalLength), _cts.Token);

            Interlocked.Add(ref _totalBytesSent, totalLength);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送帧数据失败");
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
            var headerBuffer = new byte[FrameHeader.Size];

            while (!_cts.IsCancellationRequested && IsConnected)
            {
                // 读取消息头
                if (!await ReadExactBytesAsync(headerBuffer, 0, FrameHeader.Size))
                {
                    break;
                }

                var header = AsyncSpanHelper.ReadFrameHeaderSync(headerBuffer.AsSpan());

                // 验证魔数
                if (!header.IsValid)
                {
                    var remoteEndpoint = _socket?.RemoteEndPoint?.ToString() ?? "Unknown";
                    _logger.LogWarning(
                        "收到无效的协议魔数: 0x{Magic:X4} (期望: 0x{Expected:X4}) 来自 {RemoteEndpoint}，可能是协议不匹配或探针连接，断开连接",
                        header.Magic, ProtocolConstants.ProtocolMagic, remoteEndpoint);
                    break;  // 断开连接
                }

                // 验证长度：以单包最大尺寸上限为界，允许大消息以单帧收发
                var maxWirePacketSize = _wireSession?.HasTransforms == true
                    ? checked(_options.MaxPacketSize + TransportWireSession.MaxEnvelopeOverhead)
                    : _options.MaxPacketSize;
                if (header.Length <= 0 || header.Length > maxWirePacketSize)
                {
                    var remoteEndpoint = _socket?.RemoteEndPoint?.ToString() ?? "Unknown";
                    _logger.LogWarning(
                        "收到无效的消息长度: {Length} (允许上限 {Max}) 来自 {RemoteEndpoint}，断开连接",
                        header.Length, maxWirePacketSize, remoteEndpoint);
                    break;  // 断开连接
                }

                // 发送端已统一使用单帧协议，不再生成 legacy chunk 帧。继续解析该格式会让
                // 未受信任的 TotalChunks/ChunkSize 进入分片状态分配，因此默认 fail closed。
                if (header.IsChunked)
                {
                    var remoteEndpoint = _socket?.RemoteEndPoint?.ToString() ?? "Unknown";
                    _logger.LogWarning(
                        "收到已停用的 TCP 分片帧，拒绝连接: Length={Length}, RemoteEndpoint={RemoteEndpoint}",
                        header.Length,
                        remoteEndpoint);
                    break;
                }

                // 读取消息内容
                var messageBuffer = header.Length <= _receiveBuffer.Length
                    ? _receiveBuffer : new byte[header.Length];
                // 只有显式为大帧分配的新数组才可把所有权交给事件订阅方。长度刚好等于
                // RecvBufferSize 时 messageBuffer 仍是共享缓冲，必须复制，否则下一帧会覆盖响应。
                var ownsMessageBuffer = header.Length > _receiveBuffer.Length;

                if (!await ReadExactBytesAsync(messageBuffer, 0, header.Length))
                {
                    break;
                }

                Interlocked.Add(ref _totalBytesReceived, FrameHeader.Size + header.Length);

                // 检查是否为握手消息
                if (header.IsHandshake)
                {
                    if (header.IsCompressed || header.IsEncrypted)
                    {
                        _logger.LogWarning("握手帧不得压缩或加密，断开连接");
                        break;
                    }
                    await HandleHandshakeMessageAsync(header, new ReadOnlyMemory<byte>(messageBuffer, 0, header.Length));
                    continue;
                }

                // 如果握手未完成，拒绝处理业务消息
                if (!_handshakeCompleted)
                {
                    _logger.LogWarning("收到业务消息但握手未完成，断开连接");
                    break;
                }

                const ushort allowedBusinessFlags =
                    FrameHeader.FlagCompressed | FrameHeader.FlagEncrypted | FrameHeader.FlagLargePacket;
                if ((header.Flags & ~allowedBusinessFlags) != 0)
                {
                    _logger.LogWarning("收到未知 TCP 业务帧标志: 0x{Flags:X4}", header.Flags);
                    break;
                }

                byte[]? decoded = null;
                try
                {
                    if (_wireSession?.HasTransforms == true)
                    {
                        decoded = _wireSession.Decode(
                            new ReadOnlySpan<byte>(messageBuffer, 0, header.Length),
                            ToWireFlags(header));
                    }
                }
                catch (Exception ex) when (ex is InvalidDataException or CryptographicException)
                {
                    _logger.LogWarning(ex, "TCP wire 帧验证失败，断开连接");
                    break;
                }

                if (_wireSession?.HasTransforms != true && (header.IsCompressed || header.IsEncrypted))
                {
                    _logger.LogWarning("未协商 wire 变换却收到变换帧，断开连接");
                    break;
                }

                var eventArgs = decoded != null
                    ? new TransportDataEventArgs(decoded)
                    : ownsMessageBuffer
                        ? new TransportDataEventArgs(messageBuffer, header.Length)
                        : new TransportDataEventArgs(new ReadOnlyMemory<byte>(messageBuffer, 0, header.Length));
                DataReceived?.Invoke(this, eventArgs);
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
        finally
        {
            // EOF、非法帧长度/魔数等分支会正常 break；它们同样表示该字节流不能继续复用。
            // 必须发布断线状态，让服务端移除 channel、节点客户端失败所有 pending 请求。
            if (!_cts.IsCancellationRequested && _state == ConnectionState.Connected)
            {
                ChangeState(ConnectionState.Disconnected, "TCP 接收循环已结束。");
            }
        }
    }

    /// <summary>
    /// 处理分片消息 - 使用优化的处理器
    /// </summary>
    private Task ProcessChunkedMessageAsync(FrameHeader header, ReadOnlyMemory<byte> data)
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
    protected virtual Task HandleHandshakeMessageAsync(FrameHeader header, ReadOnlyMemory<byte> data)
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
            _wireOffer = TransportWireNegotiator.CreateClientOffer(_options);
            var handshake = new HandshakeMessage(
                ProtocolConstants.CurrentProtocolVersion,
                clientName,
                TransportWireNegotiator.SerializeOffer(_wireOffer));
            var handshakeData = handshake.ToBytes();

            var header = new FrameHeader(
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
    protected Task SendHandshakeResponseAsync(
        bool accepted,
        string? reason = null,
        CancellationToken cancellationToken = default)
        => SendHandshakeResponseWithExtensionsAsync(accepted, reason, "{}", cancellationToken);

    internal async Task SendHandshakeResponseWithExtensionsAsync(
        bool accepted,
        string? reason,
        string? extensions,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = HandshakeResponse.WithExtensions(
                accepted,
                ProtocolConstants.CurrentProtocolVersion,
                reason,
                extensions);
            var responseData = response.ToBytes();

            var header = new FrameHeader(
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

    /// <summary>服务端验证客户端 wire v3 能力并创建会话。</summary>
    internal bool TryAcceptWireHandshake(string extensions, out string responseExtensions, out string? reason)
    {
        var accepted = TransportWireNegotiator.TryAcceptServerOffer(
            _options, extensions, out responseExtensions, out var session, out reason);
        if (accepted)
        {
            _wireSession?.Dispose();
            _wireSession = session;
        }
        return accepted;
    }

    /// <summary>客户端验证服务端选择，拒绝能力降级并创建会话。</summary>
    internal bool TryCompleteWireHandshake(string extensions, out string? reason)
    {
        var accepted = TransportWireNegotiator.TryCompleteClient(
            _options, _wireOffer, extensions, out var session, out reason);
        if (accepted)
        {
            _wireSession?.Dispose();
            _wireSession = session;
        }
        return accepted;
    }

    /// <summary>清除上一次连接固定的 wire 会话。</summary>
    internal void ResetWireHandshake()
    {
        _wireSession?.Dispose();
        _wireSession = null;
    }

    internal bool TryEncodeWirePayload(ReadOnlySpan<byte> data, out TransportWirePayload payload)
    {
        if (_wireSession?.HasTransforms == true)
        {
            payload = _wireSession.Encode(data);
            return true;
        }

        payload = default;
        return false;
    }

    internal static ushort ToFrameFlags(byte wireFlags)
    {
        ushort flags = FrameHeader.FlagNone;
        if ((wireFlags & TransportWireSession.CompressedFlag) != 0)
            flags |= FrameHeader.FlagCompressed;
        if ((wireFlags & TransportWireSession.EncryptedFlag) != 0)
            flags |= FrameHeader.FlagEncrypted;
        return flags;
    }

    private static byte ToWireFlags(FrameHeader header)
    {
        byte flags = 0;
        if (header.IsCompressed)
            flags |= TransportWireSession.CompressedFlag;
        if (header.IsEncrypted)
            flags |= TransportWireSession.EncryptedFlag;
        return flags;
    }

    /// <summary>
    /// 发送消息（带消息头）- 用于握手等控制消息
    /// 注意：在发送任务启动前，直接发送；启动后，入队发送
    /// </summary>
    private async Task SendMessageAsync(FrameHeader header, byte[] data, CancellationToken cancellationToken = default)
    {
        if (_stream == null)
            throw new InvalidOperationException("Stream is not available");

        // 如果发送任务尚未启动（握手阶段），直接发送
        if (_sendTask == null)
        {
            await SendMessageDirectAsync(header, data, cancellationToken);
            return;
        }

        // 发送任务已启动，通过队列发送，并等待底层写入完成。
        // 按「头部预留区 + 消息体」布局：消息体拷贝至头部之后，
        // 帧头（含 MessageId/Flags）由发送循环统一写入。
        byte[]? buffer = null;
        try
        {
            buffer = _sendBufferPool.Rent(FrameHeader.Size + data.Length);
            data.CopyTo(buffer, FrameHeader.Size);

            var item = new TcpSendItem(
                buffer,
                data.Length,
                _sendBufferPool,
                header.MessageId,
                header.Flags);

            // 尝试同步入队
            if (!_sendQueue.Writer.TryWrite(item))
            {
                await _sendQueue.Writer.WriteAsync(item, cancellationToken);
            }

            buffer = null;
            if (!await item.Completion.ConfigureAwait(false))
            {
                throw new IOException("TCP 控制帧写入失败。");
            }
        }
        finally
        {
            if (buffer != null)
            {
                _sendBufferPool.Return(buffer);
            }
        }
    }

    /// <summary>
    /// 直接发送消息（握手阶段使用，发送任务启动前）
    /// </summary>
    private async Task SendMessageDirectAsync(FrameHeader header, byte[] data, CancellationToken cancellationToken)
    {
        // 发送消息头
        var headerBytes = new byte[FrameHeader.Size];
        AsyncSpanHelper.WriteFrameHeaderSync(headerBytes, header);
        await _stream!.WriteAsync(headerBytes, 0, FrameHeader.Size, cancellationToken);

        // 发送消息体
        await _stream.WriteAsync(data, 0, data.Length, cancellationToken);
        await _stream.FlushAsync(cancellationToken);

        Interlocked.Add(ref _totalBytesSent, FrameHeader.Size + data.Length);
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

        // 标记发送队列完成，停止发送任务
        _sendQueue.Writer.TryComplete();

        _cts.Cancel();

        try
        {
            // 等待发送任务完成
            if (_sendTask != null)
            {
                try
                {
                    _sendTask.Wait(TimeSpan.FromSeconds(5));
                }
                catch (AggregateException)
                {
                    // 忽略任务取消异常
                }
            }

            while (_sendQueue.Reader.TryRead(out var pendingItem))
            {
                pendingItem.Complete(false);
                pendingItem.ReturnBuffer();
            }

            _stream?.Dispose();
            _socket?.Dispose();
            _packetHandler?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "关闭资源异常");
        }

        _cts.Dispose();
        _wireSession?.Dispose();
        _sendQueueMetrics.Dispose();
    }
}
