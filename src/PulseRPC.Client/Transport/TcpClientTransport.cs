using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using PulseRPC.Transport;
using PulseRPC.Transport.Tcp;

namespace PulseRPC.Client.Transport;

/// <summary>
/// TCP客户端传输
/// </summary>
public class TcpClientTransport : TcpTransport, IClientTransport
{
    private int _reconnectAttempts;
    private Timer? _reconnectTimer;
    private DateTime _connectedAt;
    private DateTime _lastActivityAt;
    private string _id;
    private readonly SemaphoreSlim _handshakeSemaphore = new SemaphoreSlim(0, 1);
    private bool _handshakeAccepted;
    private string? _handshakeRejectReason;

    public TcpClientTransport(string id, TcpTransportOptions? options = null, ILogger? logger = null)
        : base(options, logger)
    {
        _id = id;
        _connectedAt = DateTime.UtcNow;
        _lastActivityAt = DateTime.UtcNow;
    }

    // ITransportConnection properties
    public override string Id => _id;
    public DateTime ConnectedAt => _connectedAt;
    public DateTime LastActivityAt => _lastActivityAt;
    public TransportType TransportType => Type;

    // ITransportConnection method
    public Task CloseAsync(CancellationToken cancellationToken = default)
    {
        return DisconnectAsync(cancellationToken);
    }

    /// <summary>
    /// 连接到服务器
    /// </summary>
    public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        if (_state == ConnectionState.Connected)
        {
            return;
        }

        ChangeStateWithConnectionEvents(ConnectionState.Connecting);

        try
        {
            // 创建新的Socket
            _socket?.Dispose();
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            // 设置Socket选项（重要：NoDelay 禁用 Nagle 算法，避免小包延迟）
            _socket.NoDelay = _options.NoDelay;
            _socket.ReceiveBufferSize = _options.RecvBufferSize;
            _socket.SendBufferSize = _options.SendBufferSize;

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
            linkedCts.CancelAfter(_options.ConnectionTimeout);

            // 连接到服务器
#if NET5_0_OR_GREATER
            await _socket.ConnectAsync(host, port, linkedCts.Token);
#else
            await _socket.ConnectAsync(host, port);
#endif

            // 创建网络流
            _stream = new NetworkStream(_socket, true);

            // 更新状态
            _connectedAt = DateTime.UtcNow;
            ChangeStateWithConnectionEvents(ConnectionState.Connected);

            // 重置重连次数
            _reconnectAttempts = 0;

            // 启动接收循环
            _receiveTask = ReceiveLoopAsync();

            // 发送握手请求
            var handshakeSuccess = await SendHandshakeRequestAsync($"PulseRPC-Client-{_id}", linkedCts.Token);
            if (!handshakeSuccess)
            {
                _logger.LogError("发送握手请求失败");
                ChangeStateWithConnectionEvents(ConnectionState.Failed, "握手失败");
                throw new InvalidOperationException("握手失败");
            }

            // 等待握手完成（服务端返回握手响应）
            var handshakeCompleted = await WaitForHandshakeAsync(linkedCts.Token);
            if (!handshakeCompleted)
            {
                _logger.LogError("握手超时或被拒绝");
                ChangeStateWithConnectionEvents(ConnectionState.Failed, "握手超时或被拒绝");
                throw new InvalidOperationException("握手超时或被拒绝");
            }

            // 握手完成后启动发送任务（高并发发送优化）
            StartSendTask();

            _logger.LogInformation("已连接到服务器并完成握手: {Host}:{Port}", host, port);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "连接到服务器失败: {Host}:{Port}", host, port);

            ChangeStateWithConnectionEvents(ConnectionState.Failed, $"连接失败: {ex.Message}", ex);

            // 如果启用了自动重连，则开始重连
            if (_options.AutoReconnect && _reconnectAttempts < _options.MaxReconnectAttempts)
            {
                StartReconnect(host, port);
            }

            throw;
        }
    }

    /// <summary>
    /// 断开连接
    /// </summary>
    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_state is ConnectionState.Disconnected or ConnectionState.Disconnecting)
        {
            return Task.CompletedTask;
        }

        ChangeStateWithConnectionEvents(ConnectionState.Disconnecting);

        try
        {
            // 取消自动重连
            _reconnectTimer?.Dispose();
            _reconnectTimer = null;

            // 关闭Socket
            if (_socket?.Connected == true)
            {
                _socket.Shutdown(SocketShutdown.Both);
                _socket.Close();
            }

            // 更新状态
            ChangeStateWithConnectionEvents(ConnectionState.Disconnected);

            _logger.LogInformation("已断开连接");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "断开连接异常");

            // 即使出现异常，也更新状态
            ChangeStateWithConnectionEvents(ConnectionState.Disconnected, $"断开异常: {ex.Message}", ex);

            throw;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 启动重连
    /// </summary>
    private void StartReconnect(string host, int port)
    {
        if (!_options.AutoReconnect || _reconnectAttempts >= _options.MaxReconnectAttempts)
        {
            return;
        }

        _reconnectAttempts++;

        ChangeStateWithConnectionEvents(ConnectionState.Reconnecting, $"尝试重连({_reconnectAttempts}/{_options.MaxReconnectAttempts})");

        _reconnectTimer?.Dispose();
        _reconnectTimer = new Timer(async void (_) =>
        {
            try
            {
                await ConnectAsync(host, port);
            }
            catch
            {
                // 重连失败，下次继续尝试
            }
        }, null, _options.ReconnectInterval, Timeout.Infinite);
    }

    /// <summary>
    /// 处理握手消息（服务端响应）
    /// </summary>
    protected override async Task HandleHandshakeMessageAsync(MessageHeader header, ReadOnlyMemory<byte> data)
    {
        try
        {
            if (header.Flags == ProtocolConstants.HandshakeResponseFlag)
            {
                // 解析握手响应
                var response = HandshakeResponse.FromBytes(data.Span);

                _logger.LogInformation(
                    "收到握手响应: Accepted={Accepted}, ServerVersion={Version}, Reason={Reason}",
                    response.Accepted, response.ServerProtocolVersion, response.Reason ?? "N/A");

                _handshakeAccepted = response.Accepted;
                _handshakeRejectReason = response.Reason;

                if (response.Accepted)
                {
                    _handshakeCompleted = true;
                }

                // 释放等待握手的信号量
                try
                {
                    _handshakeSemaphore.Release();
                }
                catch (SemaphoreFullException)
                {
                    // 信号量已经释放过，忽略
                }
            }
            else if (header.Flags == ProtocolConstants.HandshakeRequestFlag)
            {
                _logger.LogWarning("客户端不应该收到握手请求");
            }
            else
            {
                _logger.LogWarning("未知的握手消息类型: Flags=0x{Flags:X4}", header.Flags);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理握手响应异常");
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// 等待握手完成
    /// </summary>
    private async Task<bool> WaitForHandshakeAsync(CancellationToken cancellationToken)
    {
        try
        {
            // 等待握手响应（最多等待5秒）
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(ProtocolConstants.HandshakeTimeoutMs);

            await _handshakeSemaphore.WaitAsync(cts.Token);

            if (_handshakeAccepted)
            {
                _logger.LogInformation("握手成功");
                return true;
            }
            else
            {
                _logger.LogWarning("握手被拒绝: {Reason}", _handshakeRejectReason ?? "未知原因");
                return false;
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("握手超时");
            return false;
        }
    }

    /// <summary>
    /// 重写状态变更方法以触发正确的事件类型
    /// </summary>
    protected void ChangeStateWithConnectionEvents(ConnectionState newState, string? reason = null, Exception? exception = null)
    {
        var oldState = _state;
        if (oldState == newState) return;

        // 更新最后活动时间
        _lastActivityAt = DateTime.UtcNow;

        // 调用基类的状态变更方法
        ChangeState(newState, reason, exception);
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public override void Dispose()
    {
        base.Dispose();
        _reconnectTimer?.Dispose();
        _handshakeSemaphore.Dispose();
    }
}
