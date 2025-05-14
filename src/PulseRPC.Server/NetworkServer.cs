using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using PulseRPC.Protocol.Messages;
using PulseRPC.Protocol.Network;

namespace PulseRPC.Server;

/// <summary>
/// 网络服务器
/// </summary>
public class NetworkServer : IDisposable
{
    private readonly ILogger _logger;
    private readonly NetworkOptions _options;
    private readonly IMessageDispatcher _dispatcher;
    private readonly IPulseRPCSerializer _serializer;
    private readonly Socket _listenSocket;
    private readonly List<NetworkSession> _sessions = [];
    private readonly Dictionary<string, NetworkSession> _sessionsByName = new();
    private readonly object _sessionsLock = new object();
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();
    private bool _isRunning;
    private bool _isDisposed;

    /// <summary>
    /// 客户端连接事件
    /// </summary>
    public event Action<NetworkSession>? ClientConnected;

    /// <summary>
    /// 客户端断开连接事件
    /// </summary>
    public event Action<NetworkSession, Exception?>? ClientDisconnected;

    public event Action<Exception>? ErrorOccurred;

    /// <summary>
    /// 构造函数
    /// </summary>
    public NetworkServer(ILogger<NetworkServer> logger, IMessageDispatcher dispatcher, IPulseRPCSerializer serializer,
        NetworkOptions? options = null)
    {
        _logger = logger;
        _options = options ?? new NetworkOptions();
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _listenSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
    }

    /// <summary>
    /// 启动服务器
    /// </summary>
    public async Task StartAsync(IPEndPoint endPoint, int backlog = 100)
    {
        if (_isRunning)
        {
            return;
        }

        ObjectDisposedException.ThrowIf(_isDisposed, nameof(NetworkServer));

        try
        {
            // 配置Socket选项
            _listenSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

            _listenSocket.Bind(endPoint);
            _listenSocket.Listen(backlog);
            _isRunning = true;

            _logger.LogDebug("服务器已启动，监听地址: {IPEndPoint}", endPoint);

            // 开始接受客户端连接
            await AcceptClientsAsync();
        }
        catch (Exception e)
        {
            ErrorOccurred?.Invoke(e);
            _logger.LogError(e, $"启动服务器时出错");
            throw;
        }
    }

    /// <summary>
    /// 接受客户端连接
    /// </summary>
    private async Task AcceptClientsAsync()
    {
        while (_isRunning && !_cts.IsCancellationRequested)
        {
            try
            {
                var clientSocket = await _listenSocket.AcceptAsync();

                // 生成客户端ID
                var clientId = Guid.NewGuid().ToString();

                // 创建会话
                var session = new NetworkSession(_logger, clientSocket, OnMessageReceived, _serializer, _options);
                session.Disconnected += OnClientDisconnected;

                // 添加到会话列表
                lock (_sessionsLock)
                {
                    _sessions.Add(session);
                    _sessionsByName[clientId] = session;
                }

                // 开始处理消息
                _ = ProcessClientAsync(clientId, session);

                // 触发连接事件
                ClientConnected?.Invoke(session);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (_isRunning)
                {
                    ErrorOccurred?.Invoke(ex);
                    _logger.LogError($"接受客户端连接时出错: {ex.Message}");

                    // 短暂延迟避免CPU占用过高
                    await Task.Delay(100);
                }
            }
        }
    }

    /// <summary>
    /// 处理客户端会话
    /// </summary>
    private async Task ProcessClientAsync(string clientId, NetworkSession session)
    {
        try
        {
            // 处理客户端消息
            await session.ProcessMessagesAsync();
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(ex);
            _logger.LogError($"处理客户端 {clientId} 时出错: {ex.Message}");
        }
        finally
        {
            // 移除客户端
            lock (_sessionsLock)
            {
                _sessions.Remove(session);
                _sessionsByName.Remove(clientId);
            }

            // 关闭会话
            session.Dispose();

            // 触发事件
            ClientDisconnected?.Invoke(session, null);
            _logger.LogDebug($"客户端已断开: {clientId}");
        }
    }

    private Task OnMessageReceived(NetworkSession session, IPacket packet, CancellationToken cancellationToken)
    {
        return _dispatcher.DispatchAsync(session, packet, cancellationToken);
    }

    /// <summary>
    /// 处理客户端断开连接
    /// </summary>
    private void OnClientDisconnected(NetworkSession session, Exception ex)
    {
        lock (_sessionsLock)
        {
            _sessions.Remove(session);
        }

        ClientDisconnected?.Invoke(session, ex);
    }

    /// <summary>
    /// 广播消息给所有客户端
    /// </summary>
    public async Task BroadcastMessageAsync<T>(T message) where T : Message
    {
        ObjectDisposedException.ThrowIf(_isDisposed, nameof(NetworkServer));

        NetworkSession[] sessions;
        lock (_sessionsLock)
        {
            sessions = _sessions.ToArray();
        }

        await Task.WhenAll(sessions.Select(session => session.SendPacketAsync(message)));
    }

    /// <summary>
    /// 向特定客户端发送消息
    /// </summary>
    public Task SendToClientAsync<T>(string clientId, T message) where T : IPacket
    {
        lock (_sessionsLock)
        {
            if (_sessionsByName.TryGetValue(clientId, out var session))
            {
                return session.SendPacketAsync(message);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 获取连接的客户端数量
    /// </summary>
    public int GetConnectedClientCount()
    {
        lock (_sessionsLock)
        {
            return _sessions.Count;
        }
    }

    /// <summary>
    /// 获取所有客户端ID
    /// </summary>
    public string[] GetConnectedClientIds()
    {
        lock (_sessionsLock)
        {
            var ids = new string[_sessions.Count];
            _sessionsByName.Keys.CopyTo(ids, 0);
            return ids;
        }
    }

    /// <summary>
    /// 停止服务器
    /// </summary>
    public void Stop()
    {
        if (!_isRunning)
        {
            return;
        }

        _isRunning = false;

        _cts.Cancel();

        try
        {
            _listenSocket.Close();
        }
        catch
        {
            // 忽略关闭时的异常
        }

        lock (_sessionsLock)
        {
            foreach (var session in _sessions)
            {
                try
                {
                    session.Close();
                }
                catch (Exception)
                {
                    // 忽略关闭时的异常
                }
            }

            _sessions.Clear();
        }
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        Stop();
        _cts.Dispose();
    }
}
