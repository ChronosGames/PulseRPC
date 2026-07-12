using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC.Shared.Security;

namespace PulseRPC.Shared.Kcp;

/// <summary>
/// KCP传输基类
/// </summary>
public abstract class KcpTransport : ITransport
{
    protected KcpCore _kcp;
    protected Socket _socket;
    protected readonly KcpTransportOptions _options;
    protected readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
    protected readonly CancellationTokenSource _cts = new CancellationTokenSource();
    protected readonly ILogger _logger;

    protected ConnectionState _state = ConnectionState.Disconnected;
    protected IPEndPoint? _remoteEndpoint;
    protected IPEndPoint? _localEndpoint;
    protected Task? _updateTask;
    protected bool _disposed;
    private TransportWireOffer _wireOffer;
    private TransportWireSession? _wireSession;

    // KCP时间戳管理
    protected readonly System.Diagnostics.Stopwatch _stopwatch = System.Diagnostics.Stopwatch.StartNew();

    public abstract string Id { get; }
    public TransportType Type => TransportType.KCP;
    public bool IsConnected => _state == ConnectionState.Connected;
    public ConnectionState State => _state;

    public EndPoint LocalEndPoint => _localEndpoint!;
    public EndPoint RemoteEndPoint => _remoteEndpoint!;

    public event EventHandler<TransportStateEventArgs>? StateChanged;
    public event EventHandler<TransportDataEventArgs>? DataReceived;

    public KcpTransport(KcpTransportOptions? options = null, ILogger? logger = null)
    {
        _options = options ?? new KcpTransportOptions();
        TransportWireNegotiator.ValidateOptions(_options);
        _logger = logger ?? NullLogger.Instance;

        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

        // 创建KCP对象
        _kcp = new KcpCore(_options.ConversationId, OnKcpOutput, _logger);

        // 配置KCP
        _kcp.NoDelay(_options.NoDelay ? 1 : 0, _options.Interval, _options.Resend, _options.DisableFlowControl);
        _kcp.SetWindowSize(_options.SendWindow, _options.RecvWindow);
        _kcp.SetMtu(1400); // UDP推荐MTU
    }

    /// <summary>
    /// 发送数据
    /// </summary>
    public virtual async Task<bool> SendAsync(ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            _logger.LogWarning("KCP基类无法发送数据: 未连接");
            return false;
        }

        if (data.IsEmpty || data.Length > _options.MaxPacketSize)
        {
            _logger.LogWarning(
                "KCP基类拒绝发送无效大小的数据: Size={Size}, MaxPacketSize={MaxPacketSize}",
                data.Length, _options.MaxPacketSize);
            return false;
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);

        var lockTaken = false;
        try
        {
            await _sendLock.WaitAsync(linkedCts.Token);
            lockTaken = true;

            var payload = _wireSession?.Encode(data.Span)
                ?? new TransportWirePayload(data.ToArray(), 0);
            var wireData = new byte[payload.Data.Length + 1];
            wireData[0] = payload.Flags;
            payload.Data.CopyTo(wireData, 1);

            // wire v3 的 KCP 消息首字节固定携带帧标志。
            var result = _kcp.Send(wireData);

            // 立即更新KCP状态以确保数据及时发送
            if (result == 0)
            {
                var currentTime = GetCurrentTimeMs();
                _kcp.Update(currentTime);
            }
            else
            {
                _logger.LogWarning("KCP基类发送数据到KCP协议栈失败: result={Result}", result);
            }

            return result == 0;
        }
        catch (OperationCanceledException) when (linkedCts.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "KCP基类发送数据失败");
            return false;
        }
        finally
        {
            if (lockTaken)
            {
                _sendLock.Release();
            }
        }
    }

    /// <summary>
    /// KCP输出回调
    /// </summary>
    protected virtual void OnKcpOutput(byte[] buffer, int size)
    {
        try
        {
            if (_remoteEndpoint != null)
            {
                _socket.SendTo(buffer, 0, size, SocketFlags.None, _remoteEndpoint);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "KCP输出异常");
        }
    }

    /// <summary>
    /// KCP更新循环
    /// </summary>
    protected async Task KcpUpdateLoopAsync()
    {
        try
        {
            var recvBuffer = new byte[_options.RecvBufferSize];
            uint nextUpdateTime = 0;

            // 启动UDP接收
            EndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);
            _socket.BeginReceiveFrom(recvBuffer, 0, recvBuffer.Length, SocketFlags.None, ref remoteEp, OnUdpReceive, recvBuffer);

            // KCP更新循环
            while (!_disposed && !_cts.IsCancellationRequested && IsConnected)
            {
                try
                {
                    uint currentTime = GetCurrentTimeMs();

                    if (currentTime >= nextUpdateTime)
                    {
                        // 更新KCP
                        _kcp.Update(currentTime);
                        nextUpdateTime = _kcp.Check(currentTime);

                        // 处理接收数据
                        ProcessKcpReceive();
                    }

                    // 等待下次更新
                    var sleepTime = (int)(nextUpdateTime - currentTime);
                    if (sleepTime > 0)
                    {
                        // 使用更安全的延迟方式，避免 CancellationTokenSource 被释放的问题
                        try
                        {
                            await Task.Delay(Math.Min(sleepTime, 5), _cts.Token); // 减少到5ms以提高实时性
                        }
                        catch (ObjectDisposedException) when (_disposed)
                        {
                            // CancellationTokenSource 已被释放，正常退出
                            break;
                        }
                    }
                    else
                    {
                        try
                        {
                            await Task.Delay(1, _cts.Token); // 防止100%CPU占用
                        }
                        catch (ObjectDisposedException) when (_disposed)
                        {
                            // CancellationTokenSource 已被释放，正常退出
                            break;
                        }
                    }
                }
                catch (OperationCanceledException) when (_cts.IsCancellationRequested || _disposed)
                {
                    // 正常取消，退出循环
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "KCP更新循环异常");
                    ChangeState(ConnectionState.Failed, $"更新异常: {ex.Message}");
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "KCP更新循环启动失败");
            ChangeState(ConnectionState.Failed, $"循环启动失败: {ex.Message}");
        }
    }

    /// <summary>
    /// UDP数据接收回调
    /// </summary>
    protected void OnUdpReceive(IAsyncResult ar)
    {
        try
        {
            // 检查对象是否已释放
            if (_disposed || _cts.IsCancellationRequested)
                return;

            EndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);
            var buffer = (byte[])ar.AsyncState!;

            // 使用正确的结束方法
            var recvSize = _socket.EndReceiveFrom(ar, ref remoteEp);

            if (recvSize > 0 && IsConnected)
            {
                var sender = (IPEndPoint)remoteEp;
                if (_remoteEndpoint is not null && !_remoteEndpoint.Equals(sender))
                {
                    _logger.LogWarning(
                        "忽略来自非协商端点的KCP数据包: Expected={Expected}, Actual={Actual}",
                        _remoteEndpoint,
                        sender);
                }
                else
                {
                    _remoteEndpoint ??= sender;
                    _kcp.Input(new Span<byte>(buffer, 0, recvSize));
                }
            }

            // 继续接收
            if (!_disposed && !_cts.IsCancellationRequested && IsConnected && _socket.IsBound)
            {
                EndPoint newRemoteEp = new IPEndPoint(IPAddress.Any, 0);
                _socket.BeginReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None, ref newRemoteEp, OnUdpReceive, buffer);
            }
        }
        catch (ObjectDisposedException)
        {
            // Socket 已被释放，这是正常情况
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.OperationAborted ||
                                         ex.SocketErrorCode == SocketError.ConnectionReset ||
                                         ex.SocketErrorCode == SocketError.Interrupted)
        {
            // 连接被中止或重置，这在网络断开时是正常的
            if (!_cts.IsCancellationRequested && !_disposed)
            {
                ChangeState(ConnectionState.Disconnected, $"网络连接中断: {ex.SocketErrorCode}", ex);
            }
        }
        catch (Exception ex) when (!_cts.IsCancellationRequested && !_disposed)
        {
            _logger.LogError(ex, "UDP接收异常");
            ChangeState(ConnectionState.Disconnected, $"UDP接收异常: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 处理KCP接收数据
    /// </summary>
    private void ProcessKcpReceive()
    {
        try
        {
            while (true)
            {
                var messageSize = _kcp.PeekSize();
                if (messageSize <= 0)
                    break;

                var maxWirePacketSize = _wireSession?.HasTransforms == true
                    ? checked(_options.MaxPacketSize + TransportWireSession.MaxEnvelopeOverhead + 1)
                    : checked(_options.MaxPacketSize + 1);
                if (messageSize > maxWirePacketSize)
                {
                    _logger.LogError(
                        "KCP基类拒绝接收超限消息: Size={Size}, MaxPacketSize={MaxPacketSize}",
                        messageSize, maxWirePacketSize);
                    ChangeState(ConnectionState.Failed,
                        $"接收消息大小 {messageSize} 超过限制 {_options.MaxPacketSize}");
                    break;
                }

                var receivedData = new byte[messageSize];
                var size = _kcp.Recv(receivedData);
                if (size <= 0)
                    break;

                if (size < 2)
                    throw new InvalidDataException("KCP wire v3 业务帧过短。");
                var flags = receivedData[0];
                if ((flags & ~(TransportWireSession.CompressedFlag | TransportWireSession.EncryptedFlag)) != 0)
                    throw new InvalidDataException($"KCP wire v3 未知帧标志 0x{flags:X2}。");
                if (_wireSession == null && flags != 0)
                    throw new InvalidDataException("KCP 未协商 wire 变换却收到变换帧。");
                var decoded = _wireSession?.Decode(receivedData.AsSpan(1, size - 1), flags)
                    ?? receivedData.AsSpan(1, size - 1).ToArray();
                DataReceived?.Invoke(this, new TransportDataEventArgs(decoded));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "KCP基类处理接收数据异常");
            ChangeState(ConnectionState.Failed, "KCP wire 帧验证失败", ex);
        }
    }

    internal byte[] CreateWireHandshakePacket()
    {
        _wireOffer = TransportWireNegotiator.CreateClientOffer(_options);
        return KcpWireHandshake.CreateRequest(
            _options.ConversationId,
            TransportWireNegotiator.SerializeOffer(_wireOffer));
    }

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

    internal void ResetWireHandshake()
    {
        _wireSession?.Dispose();
        _wireSession = null;
    }

    /// <summary>
    /// 获取当前时间戳（毫秒）
    /// </summary>
    protected uint GetCurrentTimeMs()
    {
        return (uint)_stopwatch.ElapsedMilliseconds;
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

        _logger.LogInformation("KCP传输状态变更: {OldState} -> {NewState} ({Reason})",
            oldState, newState, reason ?? "未指定原因");

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

        try
        {
            // 首先取消令牌，停止所有正在进行的操作
            _cts.Cancel();

            // 等待更新任务完成（短时间等待）
            try
            {
                _updateTask?.Wait(TimeSpan.FromSeconds(2));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "等待KCP更新任务完成时发生异常");
            }

            // 释放 Socket 资源
            try
            {
                _socket?.Close();
                _socket?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "关闭Socket时发生异常");
            }

            // 释放 KCP 资源
            try
            {
                _kcp?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "释放KCP资源时发生异常");
            }

            // 释放其他资源
            try
            {
                _sendLock?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "释放发送锁时发生异常");
            }

            try
            {
                _cts.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "释放CancellationTokenSource时发生异常");
            }

            try
            {
                _stopwatch.Stop();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "停止计时器时发生异常");
            }

            _logger.LogDebug("KCP传输资源已释放");
            _wireSession?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "释放KCP传输资源时发生严重异常");
        }
    }
}
