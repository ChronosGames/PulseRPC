using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC.Shared;
using PulseRPC.Shared.Kcp;
namespace PulseRPC.Server.Transport;

/// <summary>
/// KCP服务端连接 - 重构版本，使用组合模式
/// </summary>
public class KcpServerTransport : IServerTransport
{
    private readonly string _id;
    private readonly Socket _sharedSocket;
    private readonly IPEndPoint _remoteEndpoint;
    private readonly KcpCore _kcp;
    private readonly KcpTransportOptions _options;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();
    private readonly DateTime _connectedAt;
    private DateTime _lastActivityAt;

    private PulseRPC.Shared.ConnectionState _state = PulseRPC.Shared.ConnectionState.Connected;
    private Task? _updateTask;
    private bool _disposed;
    private readonly System.Diagnostics.Stopwatch _stopwatch = System.Diagnostics.Stopwatch.StartNew();

    public string Id => _id;
    public TransportType Type => TransportType.KCP;
    public bool IsConnected => _state == PulseRPC.Shared.ConnectionState.Connected;
    PulseRPC.Shared.ConnectionState ITransport.State => _state;
    public EndPoint LocalEndPoint => _sharedSocket.LocalEndPoint!;
    public EndPoint RemoteEndPoint => _remoteEndpoint;
    public DateTime ConnectedAt => _connectedAt;
    public DateTime LastActiveTime => _lastActivityAt;
    internal uint ConversationId { get; }

    public event EventHandler<TransportStateEventArgs>? StateChanged;
    public event EventHandler<TransportDataEventArgs>? DataReceived;

    /// <summary>
    /// 创建KCP服务端连接
    /// </summary>
    public KcpServerTransport(string id, Socket sharedSocket, IPEndPoint remoteEndpoint, uint conv, KcpTransportOptions? options = null, ILogger? logger = null)
    {
        _id = id;
        _sharedSocket = sharedSocket;
        _remoteEndpoint = remoteEndpoint;
        _options = options ?? new KcpTransportOptions();
        ConversationId = conv;
        _logger = logger ?? NullLogger.Instance;
        _connectedAt = DateTime.UtcNow;
        _lastActivityAt = DateTime.UtcNow;

        // 创建KCP实例
        _kcp = new KcpCore(conv, OnKcpOutput, _logger);

        // 配置KCP
        _kcp.NoDelay(_options.NoDelay ? 1 : 0, _options.Interval, _options.Resend, _options.DisableFlowControl);
        _kcp.SetWindowSize(_options.SendWindow, _options.RecvWindow);
        _kcp.SetMtu(1400);

        _logger.LogInformation("创建KCP服务端连接: {ConnectionId} 从 {RemoteEndPoint}",
            _id, remoteEndpoint);
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

        _logger.LogInformation("启动KCP连接更新循环: {ConnectionId}", _id);
    }

    /// <summary>
    /// 发送数据
    /// </summary>
    public virtual Task<bool> SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (!IsConnected || _disposed)
        {
            _logger.LogWarning("KCP连接 {ConnectionId} 无法发送数据: IsConnected={IsConnected}, Disposed={Disposed}",
                _id, IsConnected, _disposed);
            return Task.FromResult(false);
        }

        if (data.IsEmpty || data.Length > _options.MaxPacketSize)
        {
            _logger.LogWarning(
                "KCP连接 {ConnectionId} 拒绝发送无效大小的数据: Size={Size}, MaxPacketSize={MaxPacketSize}",
                _id, data.Length, _options.MaxPacketSize);
            return Task.FromResult(false);
        }

        try
        {
            _logger.LogDebug("KCP连接 {ConnectionId} 发送数据: {Size} bytes", _id, data.Length);

            // 直接发送数据到KCP
            var result = _kcp.Send(data.Span);

            // 立即更新KCP状态以确保数据及时发送
            if (result == 0)
            {
                uint currentTime = GetCurrentTimeMs();
                _kcp.Update(currentTime);
                _lastActivityAt = DateTime.UtcNow; // 更新活动时间
                _logger.LogDebug("KCP连接 {ConnectionId} 数据已发送到KCP协议栈并更新", _id);
            }
            else
            {
                _logger.LogWarning("KCP连接 {ConnectionId} 发送数据到KCP协议栈失败: result={Result}", _id, result);
            }

            return Task.FromResult(result == 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "KCP发送数据失败: {ConnectionId}", _id);
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
            if (!_disposed && _state == PulseRPC.Shared.ConnectionState.Connected)
            {
                _sharedSocket.SendTo(buffer, 0, size, SocketFlags.None, _remoteEndpoint);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "KCP输出异常: {ConnectionId}", _id);
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

            _logger.LogDebug("KCP更新循环已启动: {ConnectionId}", _id);

            while (!cancellationToken.IsCancellationRequested && _state == PulseRPC.Shared.ConnectionState.Connected && !_disposed)
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
                    _logger.LogError(ex, "KCP更新循环内部异常: {ConnectionId}", _id);

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
            _logger.LogDebug("KCP更新循环正常取消: {ConnectionId}", _id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "KCP更新循环异常: {ConnectionId}", _id);
            ChangeState(PulseRPC.Shared.ConnectionState.Disconnected, $"KCP异常: {ex.Message}", ex);
        }
        finally
        {
            _logger.LogDebug("KCP更新循环已结束: {ConnectionId}", _id);
        }
    }

    /// <summary>
    /// 处理KCP接收数据
    /// </summary>
    private void ProcessKcpReceive()
    {
        try
        {
            int receiveAttempts = 0;
            const int maxReceiveAttempts = 10; // 防止无限循环

            while (receiveAttempts < maxReceiveAttempts)
            {
                var messageSize = _kcp.PeekSize();
                if (messageSize <= 0)
                    break;

                if (messageSize > _options.MaxPacketSize)
                {
                    _logger.LogError(
                        "[KCP应用层] {ConnectionId} 拒绝接收超限消息: Size={Size}, MaxPacketSize={MaxPacketSize}",
                        _id, messageSize, _options.MaxPacketSize);
                    ChangeState(PulseRPC.Shared.ConnectionState.Disconnected,
                        $"接收消息大小 {messageSize} 超过限制 {_options.MaxPacketSize}");
                    break;
                }

                receiveAttempts++;
                var receivedData = new byte[messageSize];
                var size = _kcp.Recv(receivedData);
                if (size <= 0)
                    break;

                _lastActivityAt = DateTime.UtcNow; // 更新活动时间
                DataReceived?.Invoke(this, new TransportDataEventArgs(receivedData, size));
            }

            if (receiveAttempts >= maxReceiveAttempts)
            {
                _logger.LogWarning("[KCP应用层] {ConnectionId} 达到最大接收尝试次数限制: {MaxAttempts}", _id, maxReceiveAttempts);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[KCP应用层] {ConnectionId} 处理KCP接收数据异常", _id);
        }
    }

    /// <summary>
    /// 处理接收到的UDP数据
    /// </summary>
    public void ProcessReceivedData(byte[] buffer, int size)
    {
        try
        {
            if (!_disposed && _state == PulseRPC.Shared.ConnectionState.Connected)
            {
                _kcp.Input(new Span<byte>(buffer, 0, size));
            }
            else
            {
                _logger.LogWarning("[KCP协议栈] {ConnectionId} 状态异常，忽略UDP数据: Disposed={Disposed}, State={State}",
                    _id, _disposed, _state);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[KCP协议栈] {ConnectionId} 处理UDP数据异常", _id);
            ChangeState(PulseRPC.Shared.ConnectionState.Disconnected, $"处理数据异常: {ex.Message}", ex);
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
    private void ChangeState(PulseRPC.Shared.ConnectionState newState, string? reason = null, Exception? exception = null)
    {
        var oldState = _state;
        if (oldState == newState)
            return;

        _state = newState;

        _logger.LogInformation("KCP连接状态变更: {ConnectionId} {OldState} -> {NewState} ({Reason})",
            _id, oldState, newState, reason ?? "未指定原因");

        StateChanged?.Invoke(this, new TransportStateEventArgs(_id, oldState, newState, reason, exception));
    }

    /// <summary>
    /// 关闭连接
    /// </summary>
    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        if (_state is PulseRPC.Shared.ConnectionState.Disconnected or PulseRPC.Shared.ConnectionState.Disconnecting || _disposed)
            return;

        ChangeState(PulseRPC.Shared.ConnectionState.Disconnecting);

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
                _logger.LogWarning(ex, "发送断开连接包失败: {ConnectionId}", _id);
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
                    _logger.LogWarning("等待KCP更新任务完成超时: {ConnectionId}", _id);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "等待KCP更新任务完成异常: {ConnectionId}", _id);
                }
            }

            // 更新状态
            ChangeState(PulseRPC.Shared.ConnectionState.Disconnected);

            _logger.LogInformation("已关闭KCP客户端连接: {ConnectionId}", _id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "关闭KCP客户端连接异常: {ConnectionId}", _id);
            ChangeState(PulseRPC.Shared.ConnectionState.Disconnected, $"关闭异常: {ex.Message}", ex);
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

            _logger.LogDebug("已释放KCP连接资源: {ConnectionId}", _id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "释放KCP连接资源异常: {ConnectionId}", _id);
        }
    }
}

/// <summary>
/// KCP服务端监听器
/// </summary>
public class KcpServerListener : IServerListener
{
    private readonly Socket _socket;
    private readonly KcpTransportOptions _options;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();
    private Task? _listenTask;
    private bool _isListening;
    private readonly int _port;
    private readonly ConcurrentDictionary<string, KcpServerTransport> _connections = new();
    private readonly ConcurrentDictionary<uint, string> _conversationOwners = new();
    private readonly ConcurrentDictionary<string, Task> _connectionCloseTasks = new();
    private readonly object _lifecycleLock = new();
    private Task? _failureStopTask;

    public string Name => "KCP";
    public TransportType Type => TransportType.KCP;
    public EndPoint LocalEndPoint => _socket.LocalEndPoint!;
    public bool IsListening => _isListening;

    public event System.EventHandler<ServerConnectionEventArgs>? ConnectionAccepted;

    public KcpServerListener(int port, KcpTransportOptions? options = null, ILogger? logger = null)
    {
        _port = port;
        _options = options ?? new KcpTransportOptions();
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
                _logger.LogWarning(ex, "关闭KCP连接时发生异常: {ConnectionId}", connection.Id);
            }
            finally
            {
                connection.Dispose();
            }
        }

        _connections.Clear();
        _conversationOwners.Clear();

        while (true)
        {
            var closeTasks = _connectionCloseTasks.ToArray();
            if (closeTasks.Length == 0)
            {
                break;
            }

            await Task.WhenAll(closeTasks.Select(item => item.Value)).ConfigureAwait(false);
            foreach (var closeTask in closeTasks)
            {
                if (_connectionCloseTasks.TryGetValue(closeTask.Key, out var current) &&
                    ReferenceEquals(current, closeTask.Value))
                {
                    _connectionCloseTasks.TryRemove(closeTask.Key, out _);
                }
            }
        }

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
                        // wire v2 握手固定为 conv(4) + protocol version(1)。旧 4 字节握手不再兼容。
                        if (recvSize == sizeof(uint) + sizeof(byte) &&
                            buffer[sizeof(uint)] == ProtocolConstants.CurrentProtocolVersion)
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
                        if (recvSize == sizeof(uint) + sizeof(byte) &&
                            conv == connection.ConversationId &&
                            buffer[sizeof(uint)] == ProtocolConstants.CurrentProtocolVersion)
                        {
                            SendHandshakeConfirmation(clientEp, conv);
                        }
                        else
                        {
                            _logger.LogDebug("[UDP接收] 转发数据到现有连接: ClientId={ClientId}, Size={Size}, Conv={Conv}",
                                clientId, recvSize, conv);
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
                ScheduleFailureStop();
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

            if (_conversationOwners.TryGetValue(conv, out var existingOwner)
                && !string.Equals(existingOwner, clientId, StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    "拒绝未认证的KCP端点迁移: Conv={Conv}, ExistingClient={ExistingClient}, ReboundClient={ReboundClient}",
                    conv,
                    existingOwner,
                    clientId);
                return;
            }

            if (!_conversationOwners.TryAdd(conv, clientId))
            {
                _logger.LogDebug("KCP会话已存在，忽略重复握手: Conv={Conv}, ClientId={ClientId}", conv, clientId);
                return;
            }

            // 创建新连接
            var connection = new KcpServerTransport(clientId, _socket, clientEp, conv, _options, _logger);

            if (_connections.TryAdd(clientId, connection))
            {
                // 订阅连接事件
                connection.StateChanged += OnConnectionStateChanged;

                // 启动连接（延迟启动模式）
                connection.Start();

                // 发送握手确认包
                try
                {
                    SendHandshakeConfirmation(clientEp, conv);

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
                _conversationOwners.TryRemove(conv, out _);
                connection.Dispose();
                _logger.LogDebug("KCP连接已存在，丢弃重复握手包: ClientId={ClientId}", clientId);
            }
        }
        catch (Exception ex)
        {
            if (_conversationOwners.TryGetValue(conv, out var owner)
                && string.Equals(owner, clientId, StringComparison.Ordinal))
            {
                _conversationOwners.TryRemove(conv, out _);
            }
            _logger.LogError(ex, "处理KCP握手异常: ClientId={ClientId}, Conv={Conv}", clientId, conv);
        }
    }

    private void SendHandshakeConfirmation(IPEndPoint clientEndpoint, uint conversationId)
    {
        var confirmation = new byte[sizeof(uint) + sizeof(byte)];
        BitConverter.GetBytes(conversationId).CopyTo(confirmation, 0);
        confirmation[sizeof(uint)] = ProtocolConstants.CurrentProtocolVersion;
        var sentBytes = _socket.SendTo(confirmation, clientEndpoint);
        _logger.LogDebug(
            "已发送KCP wire v{Version}握手确认: Remote={RemoteEndpoint}, Conv={Conv}, Bytes={Bytes}",
            ProtocolConstants.CurrentProtocolVersion,
            clientEndpoint,
            conversationId,
            sentBytes);
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
                if (_conversationOwners.TryGetValue(connection.ConversationId, out var owner)
                    && string.Equals(owner, clientId, StringComparison.Ordinal))
                {
                    _conversationOwners.TryRemove(connection.ConversationId, out _);
                }
                connection.StateChanged -= OnConnectionStateChanged;

                _logger.LogDebug("移除KCP连接: {ClientId}", clientId);

                var closeTask = CloseRemovedConnectionAsync(clientId, connection);
                _connectionCloseTasks[clientId] = closeTask;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理客户端断开连接异常: {ClientId}", clientId);
        }
    }

    private void ScheduleFailureStop()
    {
        lock (_lifecycleLock)
        {
            if (_failureStopTask is null || _failureStopTask.IsCompleted)
            {
                _failureStopTask = StopAsync();
            }
        }
    }

    private async Task CloseRemovedConnectionAsync(string clientId, KcpServerTransport connection)
    {
        try
        {
            await connection.CloseAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "关闭KCP连接时发生异常: {ClientId}", clientId);
        }
        finally
        {
            connection.Dispose();
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
        if (e.CurrentState == PulseRPC.Shared.ConnectionState.Disconnected)
        {
            var connection = (KcpServerTransport)sender!;
            HandleClientDisconnection(connection.Id);
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
            Task? failureStopTask;
            lock (_lifecycleLock)
            {
                failureStopTask = _failureStopTask;
            }
            failureStopTask?.GetAwaiter().GetResult();
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
