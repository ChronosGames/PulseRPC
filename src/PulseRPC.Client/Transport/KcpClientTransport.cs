using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using PulseRPC.Transport;
using PulseRPC.Transport.Kcp;

namespace PulseRPC.Client.Transport;

/// <summary>
/// KCP客户端传输
/// </summary>
public class KcpClientTransport : KcpTransport, IClientTransport
{
    private int _reconnectAttempts = 0;
    private Timer? _reconnectTimer;
    private string? _host;
    private int _port;
    private string _connectionId;
    private DateTime _connectedAt;
    private DateTime _lastActivityAt;

    public KcpClientTransport(KcpTransportOptions? options = null, ILogger? logger = null)
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
        if (_state == ConnectionState.Connected || _state == ConnectionState.Connecting)
            return;

        _host = host;
        _port = port;

        ChangeStateWithConnectionEvents(ConnectionState.Connecting);

        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);

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

            // 优化UDP Socket配置
            ConfigureUdpSocket();

            // 执行握手过程（带重试机制）
            await PerformHandshakeWithRetryAsync(linkedCts.Token);

            // 启动KCP更新循环
            _updateTask = KcpUpdateLoopAsync();

            // 更新状态
            _connectedAt = DateTime.UtcNow;
            ChangeStateWithConnectionEvents(ConnectionState.Connected);

            // 重置重连次数
            _reconnectAttempts = 0;

            _logger.LogInformation("已连接到KCP服务器: {Host}:{Port}", host, port);
        }
        catch (HandshakeException)
        {
            // 握手异常已经包含详细信息，直接重新抛出
            ChangeStateWithConnectionEvents(ConnectionState.Failed, "握手失败", null);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "连接到KCP服务器失败: {Host}:{Port}", host, port);

            ChangeStateWithConnectionEvents(ConnectionState.Failed, $"连接失败: {ex.Message}", ex);

            // 如果启用了自动重连，则开始重连
            if (_options.AutoReconnect && _reconnectAttempts < _options.MaxReconnectAttempts)
            {
                StartReconnect();
            }

            throw;
        }
    }

    /// <summary>
    /// 配置UDP Socket选项
    /// </summary>
    private void ConfigureUdpSocket()
    {
        try
        {
            // 设置接收缓冲区大小
            _socket.ReceiveBufferSize = _options.RecvBufferSize;
            _socket.SendBufferSize = ((TransportOptions)_options).SendBufferSize;

            // 设置接收超时
            _socket.ReceiveTimeout = _options.UdpReceiveTimeout;

            // 启用广播（如果需要）
            _socket.EnableBroadcast = false;

            // 注意：保持默认阻塞模式，因为我们使用BeginReceiveFrom/EndReceiveFrom异步模式
            // 非阻塞模式会导致ReceiveFrom在没有数据时立即抛出异常

            _logger.LogDebug("UDP Socket配置完成: ReceiveBufferSize={ReceiveBufferSize}, SendBufferSize={SendBufferSize}, ReceiveTimeout={ReceiveTimeout}ms",
                _socket.ReceiveBufferSize, _socket.SendBufferSize, _socket.ReceiveTimeout);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "配置UDP Socket选项时发生警告");
        }
    }

    /// <summary>
    /// 执行握手过程（带重试机制）
    /// </summary>
    private async Task PerformHandshakeWithRetryAsync(CancellationToken cancellationToken)
    {
        var maxRetries = _options.HandshakeRetryCount;
        var baseDelay = 100; // 基础延迟100ms

        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                // 执行单次握手尝试
                await PerformSingleHandshakeAsync(cancellationToken);

                _logger.LogInformation("KCP握手成功: Conv={Conv}, Attempts={Attempts}", _options.ConversationId, attempt + 1);
                return; // 握手成功，退出重试循环
            }
            catch (TimeoutException) when (attempt < maxRetries - 1)
            {
                // 指数退避延迟
                var delayMs = baseDelay * (int)Math.Pow(2, attempt);
                _logger.LogWarning("KCP握手尝试 {Attempt}/{MaxRetries} 超时，{DelayMs}ms后重试",
                    attempt + 1, maxRetries, delayMs);

                await Task.Delay(delayMs, cancellationToken);
            }
            catch (Exception ex) when (attempt < maxRetries - 1)
            {
                _logger.LogWarning(ex, "KCP握手尝试 {Attempt}/{MaxRetries} 失败: {Error}",
                    attempt + 1, maxRetries, ex.Message);

                await Task.Delay(baseDelay, cancellationToken);
            }
        }

        // 所有重试都失败了，创建详细的握手异常
        await CreateDetailedHandshakeExceptionAsync(maxRetries, cancellationToken);
    }

    /// <summary>
    /// 创建详细的握手异常
    /// </summary>
    private async Task CreateDetailedHandshakeExceptionAsync(int attemptCount, CancellationToken cancellationToken)
    {
        var exception = new HandshakeException(
            $"KCP握手失败，已尝试{attemptCount}次",
            HandshakeStage.WaitingConfirmation,
            _options.ConversationId,
            _remoteEndpoint?.ToString(),
            attemptCount);

        // 添加诊断信息
        exception.AddDiagnosticInfo("LocalEndpoint", _localEndpoint?.ToString() ?? "未知");
        exception.AddDiagnosticInfo("HandshakeTimeout", _options.HandshakeTimeout);
        exception.AddDiagnosticInfo("UdpReceiveTimeout", _options.UdpReceiveTimeout);

        // 如果启用了网络诊断，运行诊断并添加结果
        if (_options.EnableNetworkDiagnostics && _host != null)
        {
            try
            {
                _logger.LogInformation("运行网络诊断: {Host}:{Port}", _host, _port);
                var diagnostics = new NetworkDiagnostics(_logger);
                var result = await diagnostics.RunDiagnosticsAsync(_host, _port, cancellationToken);

                exception.AddDiagnosticInfo("NetworkDiagnostic", result.GetSummary());

                if (!result.PingResult.IsSuccessful)
                {
                    exception.AddTroubleshootingSuggestion($"Ping失败: {result.PingResult.Status}");
                }

                if (!result.UdpConnectivityResult.IsSuccessful)
                {
                    exception.AddTroubleshootingSuggestion($"UDP连通性测试失败: {result.UdpConnectivityResult.ErrorMessage}");
                }

                if (result.FirewallInfo.WindowsFirewallEnabled)
                {
                    exception.AddTroubleshootingSuggestion("Windows防火墙已启用，可能阻止UDP通信");
                }
            }
            catch (Exception diagEx)
            {
                _logger.LogWarning(diagEx, "网络诊断失败");
                exception.AddDiagnosticInfo("DiagnosticError", diagEx.Message);
            }
        }

        _logger.LogError("KCP握手最终失败，详细信息：\n{DetailedDescription}", exception.GetDetailedDescription());

        throw exception;
    }

    /// <summary>
    /// 执行单次握手尝试
    /// </summary>
    private async Task PerformSingleHandshakeAsync(CancellationToken cancellationToken)
    {
        // 创建握手完成任务
        var handshakeCompletion = new TaskCompletionSource<bool>();

        // 设置超时
        var timeoutMs = _options.HandshakeTimeout;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeoutMs);

        // 注册超时取消
        timeoutCts.Token.Register(() =>
        {
            if (!handshakeCompletion.Task.IsCompleted)
            {
                handshakeCompletion.TrySetException(
                    new TimeoutException($"KCP握手超时({timeoutMs}ms)，未收到服务器确认，Conv={_options.ConversationId}"));
            }
        });

        try
        {
            // 发送握手包
            var handshakeData = BitConverter.GetBytes(_options.ConversationId);
            var sentBytes = _socket.SendTo(handshakeData, _remoteEndpoint!);

            // 启动异步接收任务
            var receiveTask = ReceiveHandshakeConfirmationAsync(handshakeCompletion, timeoutCts.Token);

            // 等待握手完成或超时
            await handshakeCompletion.Task;
        }
        catch (SocketException ex)
        {
            var handshakeEx = new HandshakeException(
                $"发送握手包失败: {ex.Message}",
                ex,
                HandshakeStage.SendingHandshake,
                _options.ConversationId,
                _remoteEndpoint?.ToString());

            handshakeEx.AddDiagnosticInfo("SocketErrorCode", ex.SocketErrorCode);
            throw handshakeEx;
        }
    }

    /// <summary>
    /// 接收握手确认
    /// </summary>
    private async Task ReceiveHandshakeConfirmationAsync(TaskCompletionSource<bool> completion, CancellationToken cancellationToken)
    {
        try
        {
            // 使用异步接收模式，与服务器端保持一致
            var receiveBuffer = new byte[64]; // 增大缓冲区以容纳可能的额外数据
            var remoteEp = new IPEndPoint(IPAddress.Any, 0);

            while (!cancellationToken.IsCancellationRequested && !completion.Task.IsCompleted)
            {
                try
                {
                    // 使用异步接收避免阻塞
                    EndPoint tempEndPoint = remoteEp; // 创建可赋值的临时变量
                    var asyncResult = _socket.BeginReceiveFrom(receiveBuffer, 0, receiveBuffer.Length,
                        SocketFlags.None, ref tempEndPoint, null, null);

                    // 等待接收完成或取消
                    var waitHandles = new[] { asyncResult.AsyncWaitHandle, cancellationToken.WaitHandle };
                    var waitResult = WaitHandle.WaitAny(waitHandles, _options.UdpReceiveTimeout);

                    if (waitResult == 0) // 接收完成
                    {
                        try
                        {
                            EndPoint tempEndPoint2 = remoteEp; // 创建另一个可赋值的临时变量
                            var received = _socket.EndReceiveFrom(asyncResult, ref tempEndPoint2);
                            remoteEp = (IPEndPoint)tempEndPoint2; // 更新原始端点

                            if (received >= 4)
                            {
                                uint receivedConv = BitConverter.ToUInt32(receiveBuffer, 0);

                                if (receivedConv == _options.ConversationId && remoteEp.Equals(_remoteEndpoint))
                                {
                                    completion.TrySetResult(true);
                                    return;
                                }
                            }
                        }
                        catch (SocketException ex) when (IsExpectedSocketError(ex))
                        {
                            // 预期的Socket错误，继续等待
                        }
                    }
                    else if (waitResult == 1) // 取消请求
                    {
                        break;
                    }
                }
                catch (SocketException ex) when (IsExpectedSocketError(ex))
                {
                    // 预期的Socket错误，继续等待
                    await Task.Delay(50, cancellationToken); // 稍长的延迟避免忙等待
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "接收握手确认时发生异常");
                    completion.TrySetException(ex);
                    return;
                }

                // 短暂等待避免忙等待
                await Task.Delay(10, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // 取消操作，正常情况
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "握手接收过程中发生未预期异常");
            completion.TrySetException(ex);
        }
    }

    /// <summary>
    /// 判断是否为预期的Socket错误
    /// </summary>
    private static bool IsExpectedSocketError(SocketException ex)
    {
        return ex.SocketErrorCode == SocketError.WouldBlock ||
               ex.SocketErrorCode == SocketError.TimedOut ||
               ex.SocketErrorCode == SocketError.ConnectionReset || // Windows平台常见错误
               ex.SocketErrorCode == SocketError.OperationAborted ||
               ex.SocketErrorCode == SocketError.Interrupted ||
               ex.SocketErrorCode == SocketError.NotConnected; // UDP连接状态错误
    }

    /// <summary>
    /// 断开连接
    /// </summary>
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_state == ConnectionState.Disconnected || _state == ConnectionState.Disconnecting)
            return;

        ChangeStateWithConnectionEvents(ConnectionState.Disconnecting);

        try
        {
            // 取消自动重连
            _reconnectTimer?.Dispose();
            _reconnectTimer = null;

            // 发送断开连接包
            if (_remoteEndpoint != null)
            {
                var disconnectData = BitConverter.GetBytes(0xFFFFFFFF);
                _socket.SendTo(disconnectData, _remoteEndpoint);
            }

            // 等待发送完成
            await Task.Delay(100, cancellationToken);

            // 更新状态
            ChangeStateWithConnectionEvents(ConnectionState.Disconnected);

            _logger.LogInformation("已断开KCP连接");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "断开KCP连接异常");

            // 即使出现异常，也更新状态
            ChangeStateWithConnectionEvents(ConnectionState.Disconnected, $"断开异常: {ex.Message}", ex);

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

            ChangeStateWithConnectionEvents(ConnectionState.Reconnecting,
                $"尝试重连({_reconnectAttempts}/{_options.MaxReconnectAttempts})");

            _reconnectTimer?.Dispose();
            _reconnectTimer = new Timer(async void (_) =>
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
            _connectionId, oldState, newState, reason, exception));
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public override void Dispose()
    {
        base.Dispose();
        _reconnectTimer?.Dispose();
    }

    /// <summary>
    /// 继续接收UDP数据
    /// </summary>
    private void ContinueReceiving()
    {
        if (!_disposed && !_cts.IsCancellationRequested && _socket.IsBound)
        {
            try
            {
                var newBuffer = new byte[4096];
                EndPoint newRemoteEp = new IPEndPoint(IPAddress.Any, 0);
                _socket.BeginReceiveFrom(newBuffer, 0, newBuffer.Length, SocketFlags.None, ref newRemoteEp, OnUdpReceive, newBuffer);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "继续接收UDP数据异常");
            }
        }
    }

    /// <summary>
    /// UDP数据接收回调
    /// </summary>
    private new void OnUdpReceive(IAsyncResult ar)
    {
        if (_disposed || _cts.IsCancellationRequested)
            return;

        try
        {
            var buffer = (byte[])ar.AsyncState!;
            EndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);
            var received = _socket.EndReceiveFrom(ar, ref remoteEp);

            if (received > 0 && _state == ConnectionState.Connected)
            {
                _kcp.Input(new Span<byte>(buffer, 0, received));
            }

            // 继续接收
            ContinueReceiving();
        }
        catch (ObjectDisposedException) when (_disposed)
        {
            // 对象已释放，正常情况
        }
        catch (SocketException ex) when (IsExpectedSocketError(ex))
        {
            // 预期的Socket异常（如连接重置等）
            ChangeStateWithConnectionEvents(ConnectionState.Disconnected, $"Socket异常: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "KCP客户端UDP接收异常");
            ChangeStateWithConnectionEvents(ConnectionState.Disconnected, $"接收异常: {ex.Message}");
        }
    }
}
