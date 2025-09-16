using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using PulseRPC.Client.Core;
using PulseRPC.Transport;
using PulseRPC.Transport.Tcp;
using ConnectionStateChangedEventArgs = PulseRPC.Transport.ConnectionStateChangedEventArgs;

namespace PulseRPC.Client.Transport;

/// <summary>
/// TCP客户端传输
/// </summary>
public class TcpClientTransport : TcpTransport, IClientTransport
{
    private int _reconnectAttempts;
    private Timer? _reconnectTimer;
    private string _connectionId;
    private DateTime _connectedAt;
    private DateTime _lastActivityAt;

    public TcpClientTransport(TcpTransportOptions? options = null, ILogger? logger = null)
        : base(options, logger)
    {
        _connectionId = Guid.NewGuid().ToString();
        _connectedAt = DateTime.UtcNow;
        _lastActivityAt = DateTime.UtcNow;
    }

    // ITransportConnection properties
    public string ConnectionId => _connectionId;
    public DateTime ConnectedAt => _connectedAt;
    public DateTime LastActivityAt => _lastActivityAt;
    public TransportType TransportType => Type;

    // ITransportConnection events with proper signatures
    public new event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;
    public new event EventHandler<TransportDataEventArgs>? DataReceived;

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

            _logger.LogInformation("已连接到服务器: {Host}:{Port}", host, port);
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
    /// 重写状态变更方法以触发正确的事件类型
    /// </summary>
    protected void ChangeStateWithConnectionEvents(ConnectionState newState, string? reason = null, Exception? exception = null)
    {
        var oldState = _state;
        if (oldState == newState) return;

        // 更新最后活动时间
        _lastActivityAt = DateTime.UtcNow;

        // 调用基类的状态变更方法
        base.ChangeState(newState, reason, exception);

        // 触发 ITransportConnection 接口的事件
        StateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(
            _connectionId, ConvertToExtendedState(oldState), ConvertToExtendedState(newState), reason, exception));
    }

    /// <summary>
    /// 转换到扩展连接状态
    /// </summary>
    private static ExtendedConnectionState ConvertToExtendedState(ConnectionState state)
    {
        return state switch
        {
            ConnectionState.Disconnected => Core.ExtendedConnectionState.Disconnected,
            ConnectionState.Connecting => Core.ExtendedConnectionState.Connecting,
            ConnectionState.Connected => Core.ExtendedConnectionState.Connected,
            ConnectionState.Disconnecting => Core.ExtendedConnectionState.Disconnecting,
            ConnectionState.Failed => Core.ExtendedConnectionState.Failed,
            ConnectionState.Reconnecting => Core.ExtendedConnectionState.Reconnecting,
            _ => Core.ExtendedConnectionState.Uninitialized
        };
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public override void Dispose()
    {
        base.Dispose();
        _reconnectTimer?.Dispose();
    }
}
