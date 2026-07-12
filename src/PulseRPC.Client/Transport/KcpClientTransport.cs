using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using PulseRPC.Shared;
using PulseRPC.Shared.Kcp;

namespace PulseRPC.Client.Transport;

/// <summary>
/// KCP客户端传输
/// </summary>
public class KcpClientTransport : KcpTransport, IClientTransport, IAbortableClientTransport
{
    private int _reconnectAttempts;
    private int _reconnectScheduled;
    private readonly object _reconnectSync = new();
    private Timer? _reconnectTimer;
    private Task? _reconnectTask;
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private bool _manualDisconnect;
    private string? _host;
    private int _port;
    private DateTime _connectedAt;
    private DateTime _lastActivityAt;
    private string _id;

    public KcpClientTransport(string id, KcpTransportOptions? options = null, ILogger? logger = null)
        : base(options, logger)
    {
        _id = id;
        _connectedAt = DateTime.UtcNow;
        _lastActivityAt = DateTime.UtcNow;
        StateChanged += OnTransportStateChanged;
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
        await _connectLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state == ConnectionState.Connected)
                return;

            _manualDisconnect = false;
            _host = host;
            _port = port;

            ChangeStateWithConnectionEvents(ConnectionState.Connecting);

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
            if (!_socket.IsBound)
            {
                _socket.Bind(new IPEndPoint(IPAddress.Any, 0));
            }
            _localEndpoint = (IPEndPoint)_socket.LocalEndPoint!;

            // 优化UDP Socket配置
            ConfigureUdpSocket();

            // 执行握手过程（带重试机制）
            await PerformHandshakeWithRetryAsync(linkedCts.Token);

            // 更新状态
            _connectedAt = DateTime.UtcNow;
            ChangeStateWithConnectionEvents(ConnectionState.Connected);

            // 更新循环只在 Connected 状态运行，必须在状态切换后启动。
            _updateTask = KcpUpdateLoopAsync();

            // 重置重连次数
            _reconnectAttempts = 0;
            CancelReconnect();

            _logger.LogInformation("已连接到KCP服务器: {Host}:{Port}", host, port);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "连接到KCP服务器失败: {Host}:{Port}", host, port);

            ChangeStateWithConnectionEvents(
                ConnectionState.Failed,
                ex is HandshakeException ? "握手失败" : $"连接失败: {ex.Message}",
                ex);

            throw;
        }
        finally
        {
            _connectLock.Release();
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
            _socket.SendBufferSize = _options.SendBufferSize;

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
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "KCP握手最后一次尝试 {Attempt}/{MaxRetries} 失败: {Error}",
                    attempt + 1, maxRetries, ex.Message);
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
        var timeoutMs = _options.HandshakeTimeout;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeoutMs);

        try
        {
            var handshakeData = CreateHandshakePacket(_options.ConversationId);
            _socket.SendTo(handshakeData, _remoteEndpoint!);
            await ReceiveHandshakeConfirmationAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"KCP握手超时({timeoutMs}ms)，未收到服务器确认，Conv={_options.ConversationId}");
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
    private async Task ReceiveHandshakeConfirmationAsync(CancellationToken cancellationToken)
    {
        var receiveBuffer = new byte[64];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // Poll 不会留下超时后无人 EndReceiveFrom 的悬挂接收；因此晚于单次 UDP
                // receive timeout、但仍处于整体握手窗口内的确认包仍可被当前尝试消费。
                if (!_socket.Poll(10_000, SelectMode.SelectRead))
                {
                    await Task.Delay(10, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                EndPoint sender = new IPEndPoint(IPAddress.Any, 0);
                var received = _socket.ReceiveFrom(
                    receiveBuffer,
                    0,
                    receiveBuffer.Length,
                    SocketFlags.None,
                    ref sender);
                if (!_remoteEndpoint!.Equals(sender))
                {
                    _logger.LogWarning(
                        "忽略来自非目标端点的KCP握手确认: Expected={Expected}, Actual={Actual}",
                        _remoteEndpoint,
                        sender);
                    continue;
                }

                if (received != sizeof(uint) + sizeof(byte) ||
                    BitConverter.ToUInt32(receiveBuffer, 0) != _options.ConversationId ||
                    receiveBuffer[sizeof(uint)] != ProtocolConstants.CurrentProtocolVersion)
                {
                    throw new HandshakeException(
                        $"KCP握手确认与 wire v{ProtocolConstants.CurrentProtocolVersion} 不兼容",
                        HandshakeStage.WaitingConfirmation,
                        _options.ConversationId,
                        sender.ToString());
                }

                return;
            }
            catch (SocketException ex) when (IsExpectedSocketError(ex))
            {
                await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static byte[] CreateHandshakePacket(uint conversationId)
    {
        var packet = new byte[sizeof(uint) + sizeof(byte)];
        BitConverter.GetBytes(conversationId).CopyTo(packet, 0);
        packet[sizeof(uint)] = ProtocolConstants.CurrentProtocolVersion;
        return packet;
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
        _manualDisconnect = true;
        CancelReconnect();

        if (_state == ConnectionState.Disconnected || _state == ConnectionState.Disconnecting)
            return;

        ChangeStateWithConnectionEvents(ConnectionState.Disconnecting);

        try
        {
            // 取消自动重连
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

    void IAbortableClientTransport.Abort()
    {
        _manualDisconnect = true;
        CancelReconnect();

        if (_state is not ConnectionState.Disconnected and not ConnectionState.Disconnecting)
        {
            ChangeStateWithConnectionEvents(ConnectionState.Disconnecting);
        }

        _cts.Cancel();
        try
        {
            _socket.Close(0);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Abortive KCP socket close failed");
        }
        finally
        {
            ChangeStateWithConnectionEvents(ConnectionState.Disconnected, "Abortive disconnect");
        }
    }

    /// <summary>
    /// 启动重连
    /// </summary>
    private void StartReconnect()
    {
        if (!_options.AutoReconnect || _manualDisconnect || _disposed || _host is null ||
            (_options.MaxReconnectAttempts != 0 && _reconnectAttempts >= _options.MaxReconnectAttempts) ||
            Interlocked.CompareExchange(ref _reconnectScheduled, 1, 0) != 0)
        {
            return;
        }

        _reconnectAttempts++;
        var maxAttempts = _options.MaxReconnectAttempts == 0
            ? "∞"
            : _options.MaxReconnectAttempts.ToString();

        ChangeStateWithConnectionEvents(ConnectionState.Reconnecting,
            $"尝试重连({_reconnectAttempts}/{maxAttempts})");

        lock (_reconnectSync)
        {
            if (_manualDisconnect || _disposed)
            {
                Interlocked.Exchange(ref _reconnectScheduled, 0);
                return;
            }

            _reconnectTimer?.Dispose();
            _reconnectTimer = new Timer(
                static state => ((KcpClientTransport)state!).ScheduleReconnect(),
                this,
                _options.ReconnectInterval,
                Timeout.Infinite);
        }
    }

    private void ScheduleReconnect()
    {
        lock (_reconnectSync)
        {
            if (_manualDisconnect || _disposed)
            {
                return;
            }

            _reconnectTask = ReconnectAsync();
        }
    }

    private async Task ReconnectAsync()
    {
        Interlocked.Exchange(ref _reconnectScheduled, 0);
        _reconnectTimer?.Dispose();
        _reconnectTimer = null;
        if (_manualDisconnect || _disposed || _host is null)
        {
            return;
        }

        try
        {
            await ConnectAsync(_host, _port).ConfigureAwait(false);
        }
        catch
        {
            // ConnectAsync 的状态转换会安排下一次受上限约束的重连。
        }
    }

    private void OnTransportStateChanged(object? sender, TransportStateEventArgs args)
    {
        if (args.CurrentState is ConnectionState.Failed or ConnectionState.Disconnected)
        {
            StartReconnect();
        }
    }

    private void CancelReconnect()
    {
        lock (_reconnectSync)
        {
            Interlocked.Exchange(ref _reconnectScheduled, 0);
            _reconnectTimer?.Dispose();
            _reconnectTimer = null;
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
        _manualDisconnect = true;
        StateChanged -= OnTransportStateChanged;
        CancelReconnect();
        base.Dispose();
        Task? reconnectTask;
        lock (_reconnectSync)
        {
            reconnectTask = _reconnectTask;
        }
        reconnectTask?.GetAwaiter().GetResult();
        _connectLock.Dispose();
    }
}
