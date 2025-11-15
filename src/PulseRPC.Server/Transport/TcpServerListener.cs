using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC.Transport;
using PulseRPC.Transport.Tcp;

namespace PulseRPC.Server.Transport;

/// <summary>
/// TCP服务端连接
/// </summary>
public class TcpServerTransport : TcpTransport, IServerTransport
{
    private readonly string _id;
    private readonly DateTime _connectedAt;
    private DateTime _lastActivityAt;

    public override string Id => _id;

    /// <summary>
    /// 使用已连接的Socket创建服务端连接
    /// </summary>
    public TcpServerTransport(string id, Socket socket, TcpTransportOptions? options = null,
        ILogger? logger = null)
        : base(options, logger)
    {
        _id = id;
        _connectedAt = DateTime.UtcNow;
        _lastActivityAt = DateTime.UtcNow;

        // 替换Socket
        _socket?.Dispose();
        _socket = socket;

        // 创建网络流
        _stream = new NetworkStream(socket, true);

        // 设置状态
        _state = ConnectionState.Connected;

        // 启动接收循环
        _receiveTask = ReceiveLoopAsync();

        // 订阅基类事件并转发到ITransportConnection事件
        this.StateChanged += OnBaseStateChanged;
        this.DataReceived += OnBaseDataReceived;

        _logger.LogInformation("接受客户端连接: {ConnectionId} 从 {RemoteEndPoint}", _id, socket.RemoteEndPoint);
    }

    /// <summary>
    /// 处理基类状态变化事件
    /// </summary>
    private void OnBaseStateChanged(object? sender, TransportStateEventArgs e)
    {
        ChangeState(e.CurrentState, e.Reason, e.Exception);
    }

    /// <summary>
    /// 处理基类数据接收事件
    /// </summary>
    private void OnBaseDataReceived(object? sender, TransportDataEventArgs e)
    {
        // 更新最后活动时间
        _lastActivityAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 发送数据时也要更新活动时间
    /// </summary>
    public override async Task<bool> SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        var result = await base.SendAsync(data, cancellationToken);
        if (result)
        {
            _lastActivityAt = DateTime.UtcNow;
        }
        return result;
    }

    /// <summary>
    /// 处理握手消息
    /// </summary>
    protected override async Task HandleHandshakeMessageAsync(MessageHeader header, ReadOnlyMemory<byte> data)
    {
        try
        {
            // 检查是否为握手请求
            if (header.Flags == ProtocolConstants.HandshakeRequestFlag)
            {
                // 解析握手请求
                var handshake = HandshakeMessage.FromBytes(data.Span);

                _logger.LogInformation(
                    "收到握手请求: ClientName={ClientName}, ProtocolVersion={Version}, ConnectionId={ConnectionId}",
                    handshake.ClientName, handshake.ProtocolVersion, _id);

                // 验证协议版本
                bool accepted = handshake.ProtocolVersion >= ProtocolConstants.MinSupportedProtocolVersion &&
                                handshake.ProtocolVersion <= ProtocolConstants.CurrentProtocolVersion;

                string? reason = null;
                if (!accepted)
                {
                    reason = $"不支持的协议版本 {handshake.ProtocolVersion}，支持的版本范围: " +
                             $"{ProtocolConstants.MinSupportedProtocolVersion}-{ProtocolConstants.CurrentProtocolVersion}";
                    _logger.LogWarning(reason + $", ConnectionId={_id}");
                }

                // 发送握手响应
                await SendHandshakeResponseAsync(accepted, reason);

                if (accepted)
                {
                    _handshakeCompleted = true;
                    _logger.LogInformation("握手成功: ConnectionId={ConnectionId}, ClientName={ClientName}",
                        _id, handshake.ClientName);
                }
                else
                {
                    // 握手失败，延迟断开连接以确保响应发送成功
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(100);
                        await CloseAsync();
                    });
                }
            }
            else if (header.Flags == ProtocolConstants.HandshakeResponseFlag)
            {
                _logger.LogWarning("服务端不应该收到握手响应，ConnectionId={ConnectionId}", _id);
            }
            else
            {
                _logger.LogWarning("未知的握手消息类型: Flags=0x{Flags:X4}, ConnectionId={ConnectionId}",
                    header.Flags, _id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理握手消息异常, ConnectionId={ConnectionId}", _id);
            await CloseAsync();
        }
    }

    /// <summary>
    /// 关闭连接
    /// </summary>
    public Task CloseAsync(CancellationToken cancellationToken = default)
    {
        if (_state == ConnectionState.Disconnected)
            return Task.CompletedTask;

        try
        {
            ChangeState(ConnectionState.Disconnecting);

            // 关闭Socket
            if (_socket?.Connected == true)
            {
                _socket.Shutdown(SocketShutdown.Both);
                _socket.Close();
            }

            ChangeState(ConnectionState.Disconnected);
            _logger.LogInformation("客户端连接已关闭: {ConnectionId}", _id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "关闭连接异常: {ConnectionId}", _id);
            ChangeState(ConnectionState.Disconnected, $"关闭异常: {ex.Message}", ex);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// TCP服务端监听器
/// </summary>
public class TcpServerListener : IServerListener
{
    private readonly TcpListener _listener;
    private readonly TcpTransportOptions _options;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();
    private Task? _acceptTask;
    private bool _isListening;
    private readonly int _port;

    public string Name => "TCP";
    public TransportType Type => TransportType.TCP;
    public EndPoint LocalEndPoint => _listener.LocalEndpoint;
    public bool IsListening => _isListening;

    public event EventHandler<ServerConnectionEventArgs>? ConnectionAccepted;

    public TcpServerListener(int port, TcpTransportOptions? options = null, ILogger? logger = null)
    {
        _port = port;
        _options = options ?? new TcpTransportOptions();
        _logger = logger ?? NullLogger.Instance;
        _listener = new TcpListener(IPAddress.Any, port);
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
            // 启动监听
            _listener.Start();

            // 启动接受连接任务
            _acceptTask = AcceptConnectionsAsync(_cts.Token);

            _logger.LogInformation("TCP服务器监听已启动，端口: {Port}", _port);

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _isListening = false;
            _logger.LogError(ex, "启动TCP监听失败，端口: {Port}", _port);
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

        // 取消接受连接任务
        await _cts.CancelAsync();

        // 停止监听
        _listener.Stop();

        // 等待接受连接任务完成
        try
        {
            if (_acceptTask != null)
                await _acceptTask;
        }
        catch (OperationCanceledException)
        {
            // 忽略取消异常
        }

        _logger.LogInformation("TCP服务器监听已停止，端口: {Port}", _port);
    }

    /// <summary>
    /// 接受连接循环
    /// </summary>
    private async Task AcceptConnectionsAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // 接受新连接
                var tcpClient = await _listener.AcceptTcpClientAsync(cancellationToken);

                // 设置TCP选项
                tcpClient.NoDelay = _options.NoDelay;
                tcpClient.ReceiveBufferSize = _options.RecvBufferSize;
                tcpClient.SendBufferSize = _options.SendBufferSize;

                // 生成连接ID
                var connectionId = Guid.NewGuid().ToString("N");

                // 创建服务端连接
                var connection = new TcpServerTransport(connectionId, tcpClient.Client, _options, _logger);

                // 触发连接接受事件
                ConnectionAccepted?.Invoke(this, new ServerConnectionEventArgs(connection));
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "接受TCP连接异常");
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
