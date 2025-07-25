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

    public TcpClientTransport(TransportOptions? options = null, ILogger? logger = null)
        : base(options, logger)
    { }

    /// <summary>
    /// 连接到服务器
    /// </summary>
    public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        if (_state == ConnectionState.Connected)
        {
            return;
        }

        ChangeState(ConnectionState.Connecting);

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
            ChangeState(ConnectionState.Connected);

            // 重置重连次数
            _reconnectAttempts = 0;

            // 启动接收循环
            _receiveTask = ReceiveLoopAsync();

            _logger.LogInformation("已连接到服务器: {Host}:{Port}", host, port);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "连接到服务器失败: {Host}:{Port}", host, port);

            ChangeState(ConnectionState.Failed, $"连接失败: {ex.Message}", ex);

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

        ChangeState(ConnectionState.Disconnecting);

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
            ChangeState(ConnectionState.Disconnected);

            _logger.LogInformation("已断开连接");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "断开连接异常");

            // 即使出现异常，也更新状态
            ChangeState(ConnectionState.Disconnected, $"断开异常: {ex.Message}", ex);

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

        ChangeState(ConnectionState.Reconnecting, $"尝试重连({_reconnectAttempts}/{_options.MaxReconnectAttempts})");

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
    /// 释放资源
    /// </summary>
    public override void Dispose()
    {
        base.Dispose();
        _reconnectTimer?.Dispose();
    }
}
