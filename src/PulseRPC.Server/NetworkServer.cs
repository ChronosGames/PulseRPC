using System.Net;
using System.Net.Sockets;
using PulseRPC.Protocol.Messages;
using PulseRPC.Protocol.Network;

namespace PulseRPC.Server;

/// <summary>
/// 网络服务器
/// </summary>
public class NetworkServer : IDisposable
{
    private readonly NetworkOptions _options;
    private readonly Socket _listenSocket;
    private readonly List<NetworkSession> _sessions = new List<NetworkSession>();
    private readonly object _sessionsLock = new object();
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();
    private bool _isDisposed;

    /// <summary>
    /// 客户端连接事件
    /// </summary>
    public event Action<NetworkSession>? ClientConnected;

    /// <summary>
    /// 客户端断开连接事件
    /// </summary>
    public event Action<NetworkSession, Exception?>? ClientDisconnected;

    /// <summary>
    /// 收到命令事件
    /// </summary>
    public event Action<NetworkSession, Command>? CommandReceived;

    /// <summary>
    /// 收到请求事件
    /// </summary>
    public event Action<NetworkSession, Request>? RequestReceived;

    /// <summary>
    /// 构造函数
    /// </summary>
    public NetworkServer(NetworkOptions? options = null)
    {
        _options = options ?? new NetworkOptions();
        _listenSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
    }

    /// <summary>
    /// 启动服务器
    /// </summary>
    public void Start(IPEndPoint endPoint, int backlog = 100)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(NetworkServer));

        _listenSocket.Bind(endPoint);
        _listenSocket.Listen(backlog);

        Task.Run(AcceptClientsAsync);
    }

    /// <summary>
    /// 接受客户端连接
    /// </summary>
    private async Task AcceptClientsAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var clientSocket = await _listenSocket.AcceptAsync();

                // 配置客户端Socket
                clientSocket.NoDelay = true;
                clientSocket.SendBufferSize = _options.SocketBufferSize;
                clientSocket.ReceiveBufferSize = _options.SocketBufferSize;

                // 创建会话
                var session = new NetworkSession(clientSocket, _options);
                session.CommandReceived += OnCommandReceived;
                session.Disconnected += OnClientDisconnected;

                // 添加到会话列表
                lock (_sessionsLock)
                {
                    _sessions.Add(session);
                }

                // 启动会话
                session.Start();

                // 触发连接事件
                ClientConnected?.Invoke(session);
            }
        }
        catch (Exception ex) when (ex is not ObjectDisposedException)
        {
            // 服务器异常
            Console.WriteLine($"Server accept error: {ex.Message}");
        }
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
    /// 处理收到的命令
    /// </summary>
    private void OnCommandReceived(NetworkSession session, Command command)
    {
        if (command is Request request)
        {
            RequestReceived?.Invoke(session, request);
        }
        else
        {
            CommandReceived?.Invoke(session, command);
        }
    }

    /// <summary>
    /// 广播消息给所有客户端
    /// </summary>
    public async Task BroadcastMessageAsync<T>(T message) where T : Message
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(NetworkServer));

        NetworkSession[] sessions;
        lock (_sessionsLock)
        {
            sessions = _sessions.ToArray();
        }

        var tasks = new List<Task>(sessions.Length);
        foreach (var session in sessions)
        {
            tasks.Add(session.SendMessageAsync(message));
        }

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// 向特定客户端发送消息
    /// </summary>
    public Task SendMessageAsync<T>(NetworkSession session, T message) where T : Message
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(NetworkServer));
        return session.SendMessageAsync(message);
    }

    /// <summary>
    /// 发送响应
    /// </summary>
    public Task SendResponseAsync<T>(NetworkSession session, T response, uint requestId) where T : Response
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(NetworkServer));
        return session.SendResponseAsync(response, requestId);
    }

    /// <summary>
    /// 停止服务器
    /// </summary>
    public void Stop()
    {
        _cts.Cancel();

        lock (_sessionsLock)
        {
            foreach (var session in _sessions)
            {
                session.Dispose();
            }

            _sessions.Clear();
        }

        try
        {
            _listenSocket.Close();
        }
        catch
        {
            // 忽略关闭时的异常
        }
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;

            Stop();
            _cts.Dispose();
        }
    }
}
