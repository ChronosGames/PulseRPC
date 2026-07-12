using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MemoryPack;
using Microsoft.Extensions.Logging;
using PulseRPC.Clustering;
using PulseRPC.Messaging;
using PulseRPC.Shared;
using PulseRPC.Shared.Tcp;

namespace PulseRPC.Server.Clustering;

/// <summary>节点 TCP 线路保护的部署模式。</summary>
public enum NodeTransportSecurityMode
{
    /// <summary>尚未声明线路保护；节点传输会拒绝启动。</summary>
    Unspecified = 0,

    /// <summary>节点端口由外部 mTLS service mesh、sidecar 或 TLS 终止层保护。</summary>
    ExternalMutualTls = 1,

    /// <summary>仅本机测试允许明文 TCP；不得用于生产。</summary>
    InsecureDevelopment = 2,
}

/// <summary>
/// 节点 TCP 传输的连接参数。
/// </summary>
public sealed class TcpNodeTransportOptions
{
    /// <summary>
    /// 节点线路保护模式。必须显式设置；生产只能使用 <see cref="NodeTransportSecurityMode.ExternalMutualTls"/>。
    /// </summary>
    public NodeTransportSecurityMode SecurityMode { get; set; }

    /// <summary>TCP 建连与底层握手超时。默认 10 秒。</summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>单次节点 RPC 的默认等待超时。默认 30 秒。</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>单帧最大字节数。默认 4 MiB。</summary>
    public int MaxFrameSize { get; set; } = 4 * 1024 * 1024;

    /// <summary>每条节点连接的有界发送队列容量。默认 4096。</summary>
    public int SendQueueCapacity { get; set; } = 4096;

    /// <summary>一次建连失败后，同目标节点再次拨号前的最短冷却时间。默认 250ms。</summary>
    public TimeSpan ReconnectBackoff { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>是否禁用 Nagle 算法。默认启用低延迟模式。</summary>
    public bool NoDelay { get; set; } = true;

    /// <summary>
    /// 建连成功后必须协商出的能力。默认要求当前生产节点 wire 的全部安全能力。
    /// </summary>
    public NodeTransportCapabilities RequiredCapabilities { get; set; } =
        NodeWireProtocol.SupportedCapabilities;
}

/// <summary>
/// 仅供 <see cref="TcpNodeTransport"/> 使用的 PulseRPC TCP 客户端连接。
/// </summary>
internal sealed class TcpNodeClient : TcpTransport
{
    private readonly string _id;
    private readonly TimeSpan _connectTimeout;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly SemaphoreSlim _writeSlots;
    private readonly TaskCompletionSource<bool> _handshakeCompletion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _receiveObserverTask;
    private int _clientDisposed;

    public TcpNodeClient(
        string id,
        TcpTransportOptions options,
        TimeSpan connectTimeout,
        ILogger logger)
        : base(options, logger)
    {
        _id = id;
        _connectTimeout = connectTimeout;
        _writeSlots = new SemaphoreSlim(options.SendQueueCapacity, options.SendQueueCapacity);
    }

    public override string Id => _id;

    public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(TcpNodeClient));
        }

        if (IsConnected && _handshakeCompleted)
        {
            return;
        }

        ChangeState(ConnectionState.Connecting);

        using var timeoutCts = new CancellationTokenSource(_connectTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCts.Token,
            _cts.Token);

        try
        {
            _socket = new Socket(SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = _options.NoDelay,
                ReceiveBufferSize = _options.RecvBufferSize,
                SendBufferSize = _options.SendBufferSize,
            };
            _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, _options.KeepAlive);

            await _socket.ConnectAsync(host, port, linkedCts.Token).ConfigureAwait(false);
            _stream = new NetworkStream(_socket, ownsSocket: true);
            ChangeState(ConnectionState.Connected);

            _receiveTask = ReceiveLoopAsync();
            _receiveObserverTask = ObserveReceiveLoopAsync(_receiveTask);

            if (!await SendHandshakeRequestAsync($"PulseRPC-Node-{_id}", linkedCts.Token).ConfigureAwait(false))
            {
                throw new IOException("发送 PulseRPC 节点传输握手失败。");
            }

            var accepted = await _handshakeCompletion.Task.WaitAsync(linkedCts.Token).ConfigureAwait(false);
            if (!accepted)
            {
                throw new IOException("远端拒绝 PulseRPC 节点传输握手。");
            }

            // 节点连接使用下面的确认写路径；不能使用 TcpTransport 的“仅入队即成功”发送队列。
        }
        catch (OperationCanceledException ex)
            when (timeoutCts.IsCancellationRequested
                  && !cancellationToken.IsCancellationRequested
                  && !_cts.IsCancellationRequested)
        {
            var timeout = new TimeoutException(
                $"连接节点 {host}:{port} 在 {_connectTimeout} 内未完成 TCP/握手。",
                ex);
            ChangeState(ConnectionState.Failed, timeout.Message, timeout);
            Dispose();
            throw timeout;
        }
        catch (Exception ex)
        {
            ChangeState(ConnectionState.Failed, ex.Message, ex);
            Dispose();
            throw;
        }
    }

    /// <summary>
    /// 节点数据面只有在完整帧真正写入 NetworkStream 后才报告成功。并发写等待数由
    /// SendQueueCapacity 限制；容量耗尽时 fail fast，避免无界缓冲与“入队成功但写失败”静默丢包。
    /// </summary>
    public override async Task<bool> SendAsync(
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsConnected || _stream is null)
        {
            return false;
        }

        if (data.Length <= 0 || data.Length > _options.MaxPacketSize)
        {
            return false;
        }

        if (!_writeSlots.Wait(0))
        {
            return false;
        }

        try
        {
            await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!IsConnected || _stream is null)
                {
                    return false;
                }

                var transformed = TryEncodeWirePayload(data.Span, out var wirePayload);
                var bodyLength = transformed ? wirePayload.Data.Length : data.Length;
                var buffer = ArrayPool<byte>.Shared.Rent(FrameHeader.Size + bodyLength);
                try
                {
                    var header = buffer.AsSpan(0, FrameHeader.Size);
                    BinaryPrimitives.WriteUInt16LittleEndian(header, ProtocolConstants.ProtocolMagic);
                    BinaryPrimitives.WriteInt32LittleEndian(header[2..], bodyLength);
                    BinaryPrimitives.WriteUInt16LittleEndian(header[6..], 0);
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        header[8..],
                        transformed ? ToFrameFlags(wirePayload.Flags) : FrameHeader.FlagNone);
                    if (transformed)
                        wirePayload.Data.CopyTo(buffer, FrameHeader.Size);
                    else
                        data.Span.CopyTo(buffer.AsSpan(FrameHeader.Size));

                    // 一旦取得写锁，不再用单个调用者的取消令牌中断半帧写入；连接级 Dispose
                    // 仍可通过 _cts 终止写操作。调用者取消会在响应等待阶段被观察。
                    await _stream.WriteAsync(
                        new ReadOnlyMemory<byte>(buffer, 0, FrameHeader.Size + bodyLength),
                        _cts.Token).ConfigureAwait(false);
                    Interlocked.Add(ref _totalBytesSent, FrameHeader.Size + bodyLength);
                    return true;
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException)
            {
                ChangeState(ConnectionState.Failed, $"节点 TCP 写入失败：{ex.Message}", ex);
                throw;
            }
            finally
            {
                _writeLock.Release();
            }
        }
        finally
        {
            _writeSlots.Release();
        }
    }

    protected override Task HandleHandshakeMessageAsync(FrameHeader header, ReadOnlyMemory<byte> data)
    {
        try
        {
            if (header.Flags != ProtocolConstants.HandshakeResponseFlag)
            {
                _handshakeCompletion.TrySetException(
                    new IOException($"收到未知节点握手帧 Flags=0x{header.Flags:X4}。"));
                return Task.CompletedTask;
            }

            var response = HandshakeResponse.FromBytes(data.Span);
            var accepted = response.Accepted &&
                           response.ServerProtocolVersion == ProtocolConstants.CurrentProtocolVersion &&
                           TryCompleteWireHandshake(response.Extensions, out _);
            _handshakeCompleted = accepted;
            _handshakeCompletion.TrySetResult(accepted);
        }
        catch (Exception ex)
        {
            _handshakeCompletion.TrySetException(ex);
        }

        return Task.CompletedTask;
    }

    private async Task ObserveReceiveLoopAsync(Task receiveTask)
    {
        try
        {
            await receiveTask.ConfigureAwait(false);
        }
        catch
        {
            // ReceiveLoopAsync 已负责记录异常；这里只负责补齐状态。
        }
        finally
        {
            if (!_disposed && State == ConnectionState.Connected)
            {
                ChangeState(ConnectionState.Disconnected, "远端关闭节点连接。");
            }
        }
    }

    public override void Dispose()
    {
        if (Interlocked.Exchange(ref _clientDisposed, 1) != 0)
        {
            return;
        }

        base.Dispose();
        _receiveObserverTask?.GetAwaiter().GetResult();
        _writeLock.Dispose();
        _writeSlots.Dispose();
    }
}

/// <summary>
/// 一条已完成底层握手的节点连接，负责并发请求关联与断线清理。
/// </summary>
internal sealed class TcpNodeTransportConnection : IDisposable
{
    private readonly TcpNodeClient _transport;
    private readonly TimeSpan _requestTimeout;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _requestSlots;
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<ReadOnlyMemory<byte>>> _pending = new();
    private int _disposed;

    public TcpNodeTransportConnection(
        TcpNodeClient transport,
        TimeSpan requestTimeout,
        int maxPendingRequests,
        ILogger logger)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _requestTimeout = requestTimeout;
        if (maxPendingRequests <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPendingRequests));
        }

        _requestSlots = new SemaphoreSlim(maxPendingRequests, maxPendingRequests);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _transport.DataReceived += OnDataReceived;
        _transport.StateChanged += OnStateChanged;
    }

    public bool IsConnected => Volatile.Read(ref _disposed) == 0 && _transport.IsConnected;

    public async ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ValidateOutboundFrame(frame, requireResponse: false);

        if (!await _transport.SendAsync(frame, cancellationToken).ConfigureAwait(false))
        {
            throw new IOException("节点帧未能写入 TCP 连接（连接已关闭或有界写队列已满）。");
        }
    }

    public async ValueTask<ReadOnlyMemory<byte>> AskFrameAsync(
        ReadOnlyMemory<byte> frame,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var header = ValidateOutboundFrame(frame, requireResponse: true);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_requestSlots.Wait(0))
        {
            throw new InvalidOperationException("节点连接的最大并发请求数已达到上限。");
        }

        try
        {
            var completion = new TaskCompletionSource<ReadOnlyMemory<byte>>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            if (!_pending.TryAdd(header.MessageId, completion))
            {
                throw new InvalidOperationException($"节点请求 MessageId '{header.MessageId}' 已在等待响应。");
            }

            try
            {
                using var timeoutCts = _requestTimeout > TimeSpan.Zero
                    ? new CancellationTokenSource(_requestTimeout)
                    : null;
                using var linkedCts = timeoutCts is null
                    ? null
                    : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                var effectiveToken = linkedCts?.Token ?? cancellationToken;

                try
                {
                    if (!await _transport.SendAsync(frame, effectiveToken).ConfigureAwait(false))
                    {
                        throw new IOException("节点请求未能写入 TCP 连接（连接已关闭或有界写队列已满）。");
                    }

                    return await completion.Task
                        .WaitAsync(effectiveToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException ex)
                    when (timeoutCts?.IsCancellationRequested == true && !cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException(
                        $"节点请求 MessageId '{header.MessageId}' 在 {_requestTimeout} 内未收到响应。",
                        ex);
                }
            }
            finally
            {
                _pending.TryRemove(header.MessageId, out _);
            }
        }
        finally
        {
            _requestSlots.Release();
        }
    }

    private static ReadOnlyEnvelopeHeader ValidateOutboundFrame(
        ReadOnlyMemory<byte> frame,
        bool requireResponse)
    {
        if (!EnvelopeRelay.TryReadHeader(frame, out var header, out _))
        {
            throw new ArgumentException("节点传输只接受合法的 PulseRPC 完整协议帧。", nameof(frame));
        }

        if (header.MessageId == Guid.Empty)
        {
            throw new ArgumentException("节点协议帧必须包含非空 MessageId。", nameof(frame));
        }

        if (requireResponse && header.Type is not MessageType.Request and not MessageType.ReverseRequest)
        {
            throw new ArgumentException("AskFrameAsync 只接受请求类型的节点协议帧。", nameof(frame));
        }

        return header;
    }

    private void OnDataReceived(object? sender, TransportDataEventArgs eventArgs)
    {
        if (!EnvelopeRelay.TryReadHeader(eventArgs.Data, out var header, out var payload))
        {
            _logger.LogWarning("节点连接收到损坏的 PulseRPC 响应帧，已丢弃。");
            return;
        }

        if (header.Type is not MessageType.Response and not MessageType.Error)
        {
            _logger.LogWarning(
                "节点出站连接收到非响应帧 Type={MessageType}, MessageId={MessageId}，已丢弃。",
                header.Type,
                header.MessageId);
            return;
        }

        if (!_pending.TryGetValue(header.MessageId, out var completion))
        {
            _logger.LogDebug("节点连接收到已超时或未知响应 MessageId={MessageId}。", header.MessageId);
            return;
        }

        if (header.Type == MessageType.Error)
        {
            try
            {
                var error = MemoryPackSerializer.Deserialize<ErrorResponse>(payload.Span);
                completion.TrySetException(new InvalidOperationException(
                    $"远端节点调用失败 [{error?.ErrorCode ?? "REMOTE_ERROR"}]: {error?.ErrorMessage ?? "未知错误"}"));
            }
            catch (Exception ex)
            {
                completion.TrySetException(new InvalidOperationException("远端节点返回了无法解析的错误响应。", ex));
            }

            return;
        }

        completion.TrySetResult(payload);
    }

    private void OnStateChanged(object? sender, TransportStateEventArgs eventArgs)
    {
        if (eventArgs.CurrentState is ConnectionState.Disconnected or ConnectionState.Failed)
        {
            FailPending(new IOException(
                $"节点 TCP 连接已断开：{eventArgs.Reason ?? eventArgs.CurrentState.ToString()}",
                eventArgs.Exception));
        }
    }

    private void FailPending(Exception exception)
    {
        foreach (var pair in _pending)
        {
            if (_pending.TryRemove(pair.Key, out var completion))
            {
                completion.TrySetException(exception);
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(TcpNodeTransportConnection));
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _transport.DataReceived -= OnDataReceived;
        _transport.StateChanged -= OnStateChanged;
        FailPending(new ObjectDisposedException(nameof(TcpNodeTransportConnection)));
        _transport.Dispose();
    }
}
