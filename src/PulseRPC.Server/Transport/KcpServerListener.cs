using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC.Transport;
using PulseRPC.Transport.Kcp;

namespace PulseRPC.Server.Transport;

/// <summary>
/// KCP服务端连接 - 重构版本，使用组合模式
/// </summary>
public class KcpServerConnection : IServerConnection
{
    private readonly string _connectionId;
    private readonly Socket _sharedSocket;
    private readonly IPEndPoint _remoteEndpoint;
    private readonly KcpCore _kcp;
    private readonly TransportOptions _options;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();

    private ConnectionState _state = ConnectionState.Connected;
    private Task? _updateTask;
    private bool _disposed;
    private readonly System.Diagnostics.Stopwatch _stopwatch = System.Diagnostics.Stopwatch.StartNew();

    public string ConnectionId => _connectionId;
    public string Name => "KCP";
    public TransportType Type => TransportType.Kcp;
    public bool IsConnected => _state == ConnectionState.Connected;
    public ConnectionState State => _state;
    public EndPoint LocalEndPoint => _sharedSocket.LocalEndPoint!;
    public EndPoint RemoteEndPoint => _remoteEndpoint;

    public event System.EventHandler<TransportStateEventArgs>? StateChanged;
    public event System.EventHandler<TransportDataEventArgs>? DataReceived;

    /// <summary>
    /// 创建KCP服务端连接
    /// </summary>
    public KcpServerConnection(string connectionId, Socket sharedSocket, IPEndPoint remoteEndpoint, uint conv,
        TransportOptions? options = null, ILogger? logger = null)
    {
        _connectionId = connectionId;
        _sharedSocket = sharedSocket;
        _remoteEndpoint = remoteEndpoint;
        _options = options ?? new TransportOptions();
        _logger = logger ?? NullLogger.Instance;

        // 创建KCP实例
        _kcp = new KcpCore(conv, OnKcpOutput, _logger);

        // 配置KCP
        _kcp.NoDelay(_options.Kcp.NoDelay, _options.Kcp.Interval, _options.Kcp.Resend,
            _options.Kcp.DisableFlowControl);
        _kcp.SetWindowSize(_options.Kcp.SendWindow, _options.Kcp.ReceiveWindow);
        _kcp.SetMtu(1400);

        _logger.LogInformation("创建KCP服务端连接: {ConnectionId} 从 {RemoteEndPoint}",
            _connectionId, remoteEndpoint);
    }

    /// <summary>
    /// 启动连接（延迟启动模式）
    /// </summary>
    public void Start()
    {
        if (_updateTask != null || _disposed)
            return;

        // 启动KCP更新循环
        _updateTask = KcpUpdateLoopAsync(_cts.Token);

        _logger.LogInformation("启动KCP连接更新循环: {ConnectionId}", _connectionId);
    }

    /// <summary>
    /// 发送数据
    /// </summary>
    public virtual Task<bool> SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (!IsConnected || _disposed)
        {
            _logger.LogWarning("KCP连接 {ConnectionId} 无法发送数据: IsConnected={IsConnected}, Disposed={Disposed}",
                _connectionId, IsConnected, _disposed);
            return Task.FromResult(false);
        }

        try
        {
            _logger.LogDebug("KCP连接 {ConnectionId} 发送数据: {Size} bytes", _connectionId, data.Length);

            // 直接发送数据到KCP
            var result = _kcp.Send(data.Span);

            // 立即更新KCP状态以确保数据及时发送
            if (result == 0)
            {
                uint currentTime = GetCurrentTimeMs();
                _kcp.Update(currentTime);
                _logger.LogDebug("KCP连接 {ConnectionId} 数据已发送到KCP协议栈并更新", _connectionId);
            }
            else
            {
                _logger.LogWarning("KCP连接 {ConnectionId} 发送数据到KCP协议栈失败: result={Result}", _connectionId, result);
            }

            return Task.FromResult(result == 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "KCP发送数据失败: {ConnectionId}", _connectionId);
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// KCP输出回调
    /// </summary>
    private void OnKcpOutput(byte[] buffer, int size)
    {
        try
        {
            if (!_disposed && _state == ConnectionState.Connected)
            {
                _sharedSocket.SendTo(buffer, 0, size, SocketFlags.None, _remoteEndpoint);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "KCP输出异常: {ConnectionId}", _connectionId);
        }
    }

    /// <summary>
    /// KCP更新循环
    /// </summary>
    private async Task KcpUpdateLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            uint nextUpdateTime = 0;

            _logger.LogDebug("KCP更新循环已启动: {ConnectionId}", _connectionId);

            while (!cancellationToken.IsCancellationRequested && _state == ConnectionState.Connected && !_disposed)
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
                    int sleepTime = (int)(nextUpdateTime - currentTime);
                    if (sleepTime > 0)
                    {
                        await Task.Delay(Math.Min(sleepTime, 5), cancellationToken); // 减少到5ms以提高实时性
                    }
                    else
                    {
                        await Task.Delay(1, cancellationToken); // 防止100%CPU占用
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // 正常取消，退出循环
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "KCP更新循环内部异常: {ConnectionId}", _connectionId);

                    // 短暂延迟后继续，避免忙等待
                    try
                    {
                        await Task.Delay(100, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消
            _logger.LogDebug("KCP更新循环正常取消: {ConnectionId}", _connectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "KCP更新循环异常: {ConnectionId}", _connectionId);
            ChangeState(ConnectionState.Disconnected, $"KCP异常: {ex.Message}", ex);
        }
        finally
        {
            _logger.LogDebug("KCP更新循环已结束: {ConnectionId}", _connectionId);
        }
    }

    /// <summary>
    /// 处理KCP接收数据
    /// </summary>
    private void ProcessKcpReceive()
    {
        try
        {
            const int BufferSize = 4096;
            byte[] buffer = new byte[BufferSize];

            int receiveAttempts = 0;
            const int maxReceiveAttempts = 10; // 防止无限循环

            while (receiveAttempts < maxReceiveAttempts)
            {
                receiveAttempts++;

                int size = _kcp.Recv(buffer);

                if (size <= 0)
                {
                    break;
                }

                // 触发数据接收事件
                var receivedData = new byte[size];
                Array.Copy(buffer, 0, receivedData, 0, size);

                DataReceived?.Invoke(this, new TransportDataEventArgs(receivedData));
            }

            if (receiveAttempts >= maxReceiveAttempts)
            {
                _logger.LogWarning("[KCP应用层] {ConnectionId} 达到最大接收尝试次数限制: {MaxAttempts}", _connectionId, maxReceiveAttempts);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[KCP应用层] {ConnectionId} 处理KCP接收数据异常", _connectionId);
        }
    }

    /// <summary>
    /// 处理接收到的UDP数据
    /// </summary>
    public void ProcessReceivedData(byte[] buffer, int size)
    {
        try
        {
            if (!_disposed && _state == ConnectionState.Connected)
            {
                _kcp.Input(new Span<byte>(buffer, 0, size));
            }
            else
            {
                _logger.LogWarning("[KCP协议栈] {ConnectionId} 状态异常，忽略UDP数据: Disposed={Disposed}, State={State}",
                    _connectionId, _disposed, _state);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[KCP协议栈] {ConnectionId} 处理UDP数据异常", _connectionId);
            ChangeState(ConnectionState.Disconnected, $"处理数据异常: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 获取当前时间戳（毫秒）
    /// </summary>
    private uint GetCurrentTimeMs()
    {
        return (uint)_stopwatch.ElapsedMilliseconds;
    }

    /// <summary>
    /// 更改连接状态
    /// </summary>
    private void ChangeState(ConnectionState newState, string? reason = null, Exception? exception = null)
    {
        var oldState = _state;
        if (oldState == newState)
            return;

        _state = newState;

        _logger.LogInformation("KCP连接状态变更: {ConnectionId} {OldState} -> {NewState} ({Reason})",
            _connectionId, oldState, newState, reason ?? "未指定原因");

        StateChanged?.Invoke(this, new TransportStateEventArgs(oldState, newState, reason, exception));
    }

    /// <summary>
    /// 关闭连接
    /// </summary>
    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        if (_state is ConnectionState.Disconnected or ConnectionState.Disconnecting || _disposed)
            return;

        ChangeState(ConnectionState.Disconnecting);

        try
        {
            // 取消更新循环
            _cts.Cancel();

            // 发送断开连接包
            try
            {
                byte[] disconnectData = BitConverter.GetBytes(0xFFFFFFFF);
                _sharedSocket.SendTo(disconnectData, _remoteEndpoint);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "发送断开连接包失败: {ConnectionId}", _connectionId);
            }

            // 等待更新任务完成
            if (_updateTask != null)
            {
                try
                {
                    await _updateTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                }
                catch (TimeoutException)
                {
                    _logger.LogWarning("等待KCP更新任务完成超时: {ConnectionId}", _connectionId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "等待KCP更新任务完成异常: {ConnectionId}", _connectionId);
                }
            }

            // 更新状态
            ChangeState(ConnectionState.Disconnected);

            _logger.LogInformation("已关闭KCP客户端连接: {ConnectionId}", _connectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "关闭KCP客户端连接异常: {ConnectionId}", _connectionId);
            ChangeState(ConnectionState.Disconnected, $"关闭异常: {ex.Message}", ex);
            throw;
        }
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            // 取消更新循环
            _cts.Cancel();

            // 释放KCP资源
            _kcp?.Dispose();

            // 释放其他资源
            _cts.Dispose();
            _stopwatch.Stop();

            _logger.LogDebug("已释放KCP连接资源: {ConnectionId}", _connectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "释放KCP连接资源异常: {ConnectionId}", _connectionId);
        }
    }
}

/// <summary>
/// KCP服务端监听器
/// </summary>
public class KcpServerListener : IServerListener
{
    private readonly Socket _socket;
    private readonly TransportOptions _options;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();
    private Task? _listenTask;
    private bool _isListening;
    private readonly int _port;
    private readonly ConcurrentDictionary<string, KcpServerConnection> _connections = new();

    public string Name => "KCP";
    public TransportType Type => TransportType.Kcp;
    public EndPoint LocalEndPoint => _socket.LocalEndPoint!;
    public bool IsListening => _isListening;

    public event System.EventHandler<ServerConnectionEventArgs>? ConnectionAccepted;

    public KcpServerListener(int port, TransportOptions? options = null, ILogger? logger = null)
    {
        _port = port;
        _options = options ?? new TransportOptions();
        _logger = logger ?? NullLogger.Instance;
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
    }

    /// <summary>
    /// 启动监听
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isListening)
            return Task.CompletedTask;

        _isListening = true;

        try
        {
            // 绑定端口
            _socket.Bind(new IPEndPoint(IPAddress.Any, _port));

            // 启动监听任务
            _listenTask = ListenAsync(_cts.Token);

            _logger.LogInformation("KCP服务器监听已启动，端口: {Port}", _port);

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _isListening = false;
            _logger.LogError(ex, "启动KCP监听失败，端口: {Port}", _port);
            throw;
        }
    }

    /// <summary>
    /// 停止监听
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_isListening)
            return;

        _logger.LogInformation("正在停止KCP服务器监听，端口: {Port}", _port);

        _isListening = false;

        // 取消监听任务
        _cts.Cancel();

        // 关闭Socket
        try
        {
            _socket.Close();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "关闭监听Socket时发生异常");
        }

        // 等待监听任务完成
        try
        {
            if (_listenTask != null)
                await _listenTask;
        }
        catch (OperationCanceledException)
        {
            // 忽略取消异常
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "等待监听任务完成时发生异常");
        }

        // 关闭所有连接
        var connectionList = _connections.Values.ToList();
        foreach (var connection in connectionList)
        {
            try
            {
                await connection.CloseAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "关闭KCP连接时发生异常: {ConnectionId}", connection.ConnectionId);
            }
        }

        _connections.Clear();

        _logger.LogInformation("KCP服务器监听已停止，端口: {Port}", _port);
    }

    /// <summary>
    /// 监听循环
    /// </summary>
    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        try
        {
            byte[] buffer = new byte[4096];
            EndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);

            // 开始接收数据
            _socket.BeginReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None, ref remoteEp, OnUdpReceive, buffer);

            _logger.LogDebug("KCP监听循环已启动，端口: {Port}", _port);

            // 保持任务运行
            await Task.Delay(-1, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // 正常取消
            _logger.LogDebug("KCP监听循环正常取消");
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "KCP监听异常");
        }
        finally
        {
            _logger.LogDebug("KCP监听循环已结束");
        }
    }

    /// <summary>
    /// UDP数据接收回调
    /// </summary>
    private void OnUdpReceive(IAsyncResult ar)
    {
        try
        {
            // 检查是否已停止监听
            if (!_isListening || _cts.IsCancellationRequested)
            {
                _logger.LogDebug("[UDP接收] 监听已停止，忽略数据包");
                return;
            }

            EndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);
            byte[] buffer = (byte[])ar.AsyncState!;
            int recvSize = _socket.EndReceiveFrom(ar, ref remoteEp);

            _logger.LogDebug("[UDP接收] 收到UDP数据包: From={RemoteEndpoint}, Size={Size} bytes, Data=[{DataHex}]",
                remoteEp, recvSize, Convert.ToHexString(buffer, 0, Math.Min(recvSize, 32)));

            if (recvSize >= 4)
            {
                // 获取远程端点
                var clientEp = (IPEndPoint)remoteEp;
                string clientId = $"{clientEp.Address}:{clientEp.Port}";

                _logger.LogDebug("[UDP接收] 处理客户端数据: ClientId={ClientId}, Size={Size}", clientId, recvSize);

                // 检查是否是断开连接包
                if (recvSize == 4 && BitConverter.ToUInt32(buffer, 0) == 0xFFFFFFFF)
                {
                    _logger.LogInformation("[UDP接收] 收到断开连接包: ClientId={ClientId}", clientId);
                    // 处理断开连接
                    HandleClientDisconnection(clientId);
                }
                else
                {
                    // 提取会话ID
                    uint conv = BitConverter.ToUInt32(buffer, 0);
                    _logger.LogDebug("[UDP接收] 解析会话ID: ClientId={ClientId}, Conv={Conv}", clientId, conv);

                    // 查找或创建连接
                    if (!_connections.TryGetValue(clientId, out var connection))
                    {
                        // 检查是否是握手包（只有4字节）
                        if (recvSize == 4)
                        {
                            _logger.LogInformation("[UDP接收] 收到新的握手包: ClientId={ClientId}, Conv={Conv}", clientId, conv);
                            HandleNewHandshake(clientId, clientEp, conv);
                        }
                        else
                        {
                            _logger.LogWarning("[UDP接收] 收到未知连接的非握手数据包，忽略: ClientId={ClientId}, Size={Size}, Conv={Conv}",
                                clientId, recvSize, conv);
                        }
                    }
                    else
                    {
                        _logger.LogDebug("[UDP接收] 转发数据到现有连接: ClientId={ClientId}, Size={Size}, Conv={Conv}",
                            clientId, recvSize, conv);
                        // 处理现有连接数据
                        try
                        {
                            connection.ProcessReceivedData(buffer, recvSize);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "[UDP接收] 处理KCP连接数据异常: ClientId={ClientId}", clientId);
                            HandleClientDisconnection(clientId);
                        }
                    }
                }
            }
            else
            {
                _logger.LogWarning("[UDP接收] 收到过短的UDP数据包: From={RemoteEndpoint}, Size={Size} bytes", remoteEp, recvSize);
            }

            // 继续接收
            ContinueReceiving();
        }
        catch (ObjectDisposedException)
        {
            // Socket 已被释放，这是正常情况
            _logger.LogDebug("[UDP接收] 监听Socket已释放");
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.OperationAborted ||
                                         ex.SocketErrorCode == SocketError.Interrupted)
        {
            // 操作被中止，通常在停止监听时发生
            _logger.LogDebug("[UDP接收] 监听操作被中止: {ErrorCode}", ex.SocketErrorCode);
        }
        catch (Exception ex) when (!_cts.IsCancellationRequested)
        {
            _logger.LogError(ex, "[UDP接收] KCP接收异常");

            // 尝试恢复接收
            try
            {
                ContinueReceiving();
            }
            catch (Exception recoverEx)
            {
                _logger.LogWarning(recoverEx, "[UDP接收] 无法恢复UDP接收，停止监听");
                // 停止监听
                _ = Task.Run(async () => await StopAsync());
            }
        }
    }

    /// <summary>
    /// 处理新的握手请求
    /// </summary>
    private void HandleNewHandshake(string clientId, IPEndPoint clientEp, uint conv)
    {
        try
        {
            _logger.LogDebug("收到KCP握手包: ClientId={ClientId}, Conv={Conv}, RemoteEndpoint={RemoteEndpoint}",
                clientId, conv, clientEp);

            // 创建新连接
            var connection = new KcpServerConnection(clientId, _socket, clientEp, conv, _options, _logger);

            if (_connections.TryAdd(clientId, connection))
            {
                // 订阅连接事件
                connection.StateChanged += OnConnectionStateChanged;

                // 启动连接（延迟启动模式）
                connection.Start();

                // 发送握手确认包
                try
                {
                    byte[] handshakeConfirmation = BitConverter.GetBytes(conv);
                    int sentBytes = _socket.SendTo(handshakeConfirmation, clientEp);
                    _logger.LogDebug("已发送KCP握手确认: ClientId={ClientId}, Conv={Conv}, Bytes={Bytes}",
                        clientId, conv, sentBytes);

                    // 触发连接接受事件
                    ConnectionAccepted?.Invoke(this, new ServerConnectionEventArgs(connection));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "发送KCP握手确认失败: ClientId={ClientId}, Conv={Conv}", clientId, conv);
                    HandleClientDisconnection(clientId);
                }
            }
            else
            {
                // 连接已存在，处理重复握手包
                connection.Dispose();
                _logger.LogDebug("KCP连接已存在，丢弃重复握手包: ClientId={ClientId}", clientId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理KCP握手异常: ClientId={ClientId}, Conv={Conv}", clientId, conv);
        }
    }

    /// <summary>
    /// 处理客户端断开连接
    /// </summary>
    private void HandleClientDisconnection(string clientId)
    {
        try
        {
            if (_connections.TryRemove(clientId, out var connection))
            {
                connection.StateChanged -= OnConnectionStateChanged;

                _logger.LogDebug("移除KCP连接: {ClientId}", clientId);

                // 异步关闭连接，避免阻塞接收循环
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await connection.CloseAsync();
                        connection.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "关闭KCP连接时发生异常: {ClientId}", clientId);
                    }
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理客户端断开连接异常: {ClientId}", clientId);
        }
    }

    /// <summary>
    /// 继续接收UDP数据
    /// </summary>
    private void ContinueReceiving()
    {
        if (_isListening && !_cts.IsCancellationRequested)
        {
            try
            {
                byte[] newBuffer = new byte[4096];
                EndPoint newRemoteEp = new IPEndPoint(IPAddress.Any, 0);
                _socket.BeginReceiveFrom(newBuffer, 0, newBuffer.Length, SocketFlags.None, ref newRemoteEp,
                    OnUdpReceive, newBuffer);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "继续接收UDP数据异常");
                throw;
            }
        }
    }

    /// <summary>
    /// 连接状态变化处理
    /// </summary>
    private void OnConnectionStateChanged(object? sender, TransportStateEventArgs e)
    {
        if (e.CurrentState == ConnectionState.Disconnected)
        {
            var connection = (KcpServerConnection)sender!;
            HandleClientDisconnection(connection.ConnectionId);
        }
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        try
        {
            StopAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "释放KCP监听器资源异常");
        }
        finally
        {
            try
            {
                _socket?.Dispose();
                _cts.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "释放底层资源异常");
            }
        }
    }
}
