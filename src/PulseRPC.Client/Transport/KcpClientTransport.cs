using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using PulseRPC.Transport;
using PulseRPC.Transport.Kcp;

namespace PulseRPC.Client.Transport
{
    /// <summary>
    /// KCP客户端传输
    /// </summary>
    public class KcpClientTransport : KcpTransport, IClientTransport
    {
        private int _reconnectAttempts = 0;
        private Timer? _reconnectTimer;
        private string? _host;
        private int _port;

        public KcpClientTransport(TransportOptions? options = null, ILogger? logger = null)
            : base(options, logger)
        {
        }

        /// <summary>
        /// 连接到服务器
        /// </summary>
        public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
        {
            if (_state == ConnectionState.Connected || _state == ConnectionState.Connecting)
                return;

            _host = host;
            _port = port;

            ChangeState(ConnectionState.Connecting);

            try
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
                linkedCts.CancelAfter(_options.ConnectionTimeout);

                // 解析服务器地址
#if NET5_0_OR_GREATER
                var addresses = await Dns.GetHostAddressesAsync(host, linkedCts.Token);
#else
                var addresses = await Dns.GetHostAddressesAsync(host);
#endif
                var address = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);

                if (address == null)
                    throw new InvalidOperationException($"无法解析主机: {host}");

                // 设置远程端点
                _remoteEndpoint = new IPEndPoint(address, port);

                // 绑定本地端点
                _socket.Bind(new IPEndPoint(IPAddress.Any, 0));
                _localEndpoint = (IPEndPoint)_socket.LocalEndPoint!;

                // 发送握手包
                byte[] handshakeData = BitConverter.GetBytes(_options.Kcp.ConversationId);
                _socket.SendTo(handshakeData, _remoteEndpoint);

                // 等待连接建立
                await Task.Delay(100, linkedCts.Token);

                // 启动KCP更新循环
                _updateTask = KcpUpdateLoopAsync();

                // 更新状态
                ChangeState(ConnectionState.Connected);

                // 重置重连次数
                _reconnectAttempts = 0;

                _logger.LogInformation("已连接到KCP服务器: {Host}:{Port}", host, port);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "连接到KCP服务器失败: {Host}:{Port}", host, port);

                ChangeState(ConnectionState.Failed, $"连接失败: {ex.Message}", ex);

                // 如果启用了自动重连，则开始重连
                if (_options.AutoReconnect && _reconnectAttempts < _options.MaxReconnectAttempts)
                {
                    StartReconnect();
                }

                throw;
            }
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        public async Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            if (_state == ConnectionState.Disconnected || _state == ConnectionState.Disconnecting)
                return;

            ChangeState(ConnectionState.Disconnecting);

            try
            {
                // 取消自动重连
                _reconnectTimer?.Dispose();
                _reconnectTimer = null;

                // 发送断开连接包
                if (_remoteEndpoint != null)
                {
                    byte[] disconnectData = BitConverter.GetBytes(0xFFFFFFFF);
                    _socket.SendTo(disconnectData, _remoteEndpoint);
                }

                // 等待发送完成
                await Task.Delay(100, cancellationToken);

                // 更新状态
                ChangeState(ConnectionState.Disconnected);

                _logger.LogInformation("已断开KCP连接");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "断开KCP连接异常");

                // 即使出现异常，也更新状态
                ChangeState(ConnectionState.Disconnected, $"断开异常: {ex.Message}", ex);

                throw;
            }
        }

        /// <summary>
        /// 启动重连
        /// </summary>
        private void StartReconnect()
        {
            if (_options.AutoReconnect && _reconnectAttempts < _options.MaxReconnectAttempts)
            {
                _reconnectAttempts++;

                ChangeState(ConnectionState.Reconnecting,
                    $"尝试重连({_reconnectAttempts}/{_options.MaxReconnectAttempts})");

                _reconnectTimer?.Dispose();
                _reconnectTimer = new Timer(async _ =>
                {
                    try
                    {
                        await ConnectAsync(_host!, _port);
                    }
                    catch
                    {
                        // 重连失败，下次继续尝试
                    }
                }, null, _options.ReconnectInterval, Timeout.Infinite);
            }
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
}

