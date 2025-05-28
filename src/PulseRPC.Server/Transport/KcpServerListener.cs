using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC.Transport;
using PulseRPC.Transport.Kcp;

namespace PulseRPC.Server.Transport;

/// <summary>
/// KCP服务端连接
/// </summary>
public class KcpServerConnection : KcpTransport, IServerConnection
{
    private readonly string _connectionId;

    public string ConnectionId => _connectionId;

    /// <summary>
    /// 使用远程端点创建KCP服务端连接
    /// </summary>
    public KcpServerConnection(string connectionId, Socket udpSocket, IPEndPoint remoteEndpoint, uint conv,
        TransportOptions? options = null, ILogger? logger = null)
        : base(options, logger)
    {
        _connectionId = connectionId;

        // 替换Socket和端点
        _socket.Dispose();
        _socket = udpSocket;
        _remoteEndpoint = remoteEndpoint;
        _localEndpoint = (IPEndPoint)udpSocket.LocalEndPoint!;

        // 重新创建KCP对象，使用指定的会话ID
        _kcp?.Dispose();
        var newKcp = new KcpCore(conv, OnKcpOutput, _logger);
        newKcp.NoDelay(_options.Kcp.NoDelay, _options.Kcp.Interval, _options.Kcp.Resend,
            _options.Kcp.DisableFlowControl);
        newKcp.SetWindowSize(_options.Kcp.SendWindow, _options.Kcp.ReceiveWindow);
        newKcp.SetMtu(1400);

        // 设置状态
        _state = ConnectionState.Connected;

        // 启动KCP更新循环
        _updateTask = KcpUpdateLoopAsync();

        _logger.LogInformation("接受KCP客户端连接: {ConnectionId} 从 {RemoteEndPoint}",
            _connectionId, remoteEndpoint);
    }

    /// <summary>
    /// 关闭连接
    /// </summary>
    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        if (_state is ConnectionState.Disconnected or ConnectionState.Disconnecting)
            return;

        ChangeState(ConnectionState.Disconnecting);

        try
        {
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

            _logger.LogInformation("已关闭KCP客户端连接: {ConnectionId}", _connectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "关闭KCP客户端连接异常: {ConnectionId}", _connectionId);

            // 即使出现异常，也更新状态
            ChangeState(ConnectionState.Disconnected, $"关闭异常: {ex.Message}", ex);

            throw;
        }
    }

    /// <summary>
    /// 重写KCP输出回调以支持共享Socket
    /// </summary>
    protected override void OnKcpOutput(byte[] buffer, int size)
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
            _logger.LogError(ex, "KCP服务端输出异常: {ConnectionId}", _connectionId);
        }
    }

    /// <summary>
    /// 处理接收到的数据
    /// </summary>
    public void ProcessReceivedData(byte[] buffer, int size)
    {
        try
        {
            _kcp.Input(new Span<byte>(buffer, 0, size));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理KCP连接数据异常: {ConnectionId}", _connectionId);

            // 如果数据处理失败，考虑移除连接
            ChangeState(ConnectionState.Disconnected, $"处理数据异常: {ex.Message}", ex);
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

        _isListening = false;

        // 取消监听任务
        _cts.Cancel();

        // 关闭Socket
        _socket.Close();

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

        // 关闭所有连接
        foreach (var connection in _connections.Values)
        {
            try
            {
                await connection.CloseAsync(cancellationToken);
            }
            catch
            {
                // 忽略关闭异常
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

            // 开始接收数据 - 使用正确的 BeginReceiveFrom
            _socket.BeginReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None, ref remoteEp, OnUdpReceive, buffer);

            // 保持任务运行
            await Task.Delay(-1, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // 正常取消
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "KCP监听异常");
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
                return;

            EndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);
            byte[] buffer = (byte[])ar.AsyncState!;
            int recvSize = _socket.EndReceiveFrom(ar, ref remoteEp);

            if (recvSize >= 4)
            {
                // 获取远程端点
                var clientEp = (IPEndPoint)remoteEp;
                string clientId = $"{clientEp.Address}:{clientEp.Port}";

                // 检查是否是断开连接包
                if (recvSize == 4 && BitConverter.ToUInt32(buffer, 0) == 0xFFFFFFFF)
                {
                    // 处理断开连接
                    if (_connections.TryRemove(clientId, out var connection))
                    {
                        connection.Dispose();
                    }
                }
                else
                {
                    // 提取会话ID
                    uint conv = BitConverter.ToUInt32(buffer, 0);

                    // 查找或创建连接
                    if (!_connections.TryGetValue(clientId, out var connection))
                    {
                        // 检查是否是握手包
                        if (recvSize == 4)
                        {
                            // 创建新连接
                            connection = new KcpServerConnection(clientId, _socket, clientEp, conv, _options,
                                _logger);

                            if (_connections.TryAdd(clientId, connection))
                            {
                                // 连接事件处理
                                connection.StateChanged += OnConnectionStateChanged;

                                // 触发连接接受事件
                                ConnectionAccepted?.Invoke(this, new ServerConnectionEventArgs(connection));
                            }
                            else
                            {
                                // 连接已存在
                                connection.Dispose();
                            }
                        }
                    }
                    else
                    {
                        // 处理现有连接数据
                        // 将UDP数据包传递给对应的KCP实例进行处理
                        try
                        {
                            connection.ProcessReceivedData(buffer, recvSize);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "处理KCP连接数据异常: {ClientId}", clientId);

                            // 如果数据处理失败，考虑移除连接
                            if (_connections.TryRemove(clientId, out var failedConnection))
                            {
                                failedConnection.Dispose();
                            }
                        }
                    }
                }
            }

            // 继续接收 - 使用正确的 BeginReceiveFrom
            if (_isListening && !_cts.IsCancellationRequested)
            {
                EndPoint newRemoteEp = new IPEndPoint(IPAddress.Any, 0);
                _socket.BeginReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None, ref newRemoteEp, OnUdpReceive,
                    buffer);
            }
        }
        catch (ObjectDisposedException)
        {
            // Socket 已被释放，这是正常情况
            _logger.LogDebug("监听Socket已释放");
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.OperationAborted ||
                                         ex.SocketErrorCode == SocketError.Interrupted)
        {
            // 操作被中止，通常在停止监听时发生
            _logger.LogDebug("监听操作被中止: {ErrorCode}", ex.SocketErrorCode);
        }
        catch (Exception ex) when (!_cts.IsCancellationRequested)
        {
            _logger.LogError(ex, "KCP接收异常");

            // 尝试恢复接收
            try
            {
                if (_isListening && !_cts.IsCancellationRequested)
                {
                    byte[] newBuffer = new byte[4096];
                    EndPoint newRemoteEp = new IPEndPoint(IPAddress.Any, 0);
                    _socket.BeginReceiveFrom(newBuffer, 0, newBuffer.Length, SocketFlags.None, ref newRemoteEp,
                        OnUdpReceive, newBuffer);
                }
            }
            catch
            {
                // 恢复失败，停止监听
                _logger.LogWarning("无法恢复UDP接收，停止监听");
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

            // 移除断开的连接
            if (_connections.TryRemove(connection.ConnectionId, out _))
            {
                // 取消事件订阅
                connection.StateChanged -= OnConnectionStateChanged;
            }
        }
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        _cts.Dispose();
    }
}
