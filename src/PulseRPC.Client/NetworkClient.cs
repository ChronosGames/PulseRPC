using System.Net.Sockets;
using System.Runtime.CompilerServices;
using PulseRPC.Protocol.Messages;
using PulseRPC.Protocol.Network;

namespace PulseRPC.Client;

/// <summary>
/// 网络客户端
/// </summary>
public class NetworkClient : IDisposable
{
    private readonly NetworkOptions _options;
    private NetworkSession? _session;
    private readonly object _connectionLock = new object();
    private CancellationTokenSource? _reconnectCts;
    private bool _isDisposed;

    /// <summary>
    /// 连接状态变更事件
    /// </summary>
    public event Action<bool, Exception?>? ConnectionChanged;

    /// <summary>
    /// 收到命令事件
    /// </summary>
    public event Action<Command>? CommandReceived;

    /// <summary>
    /// 收到消息事件
    /// </summary>
    public event Action<Message>? MessageReceived;

    /// <summary>
    /// 是否已连接
    /// </summary>
    public bool IsConnected => _session != null && !_isDisposed;

    /// <summary>
    /// 构造函数
    /// </summary>
    public NetworkClient(NetworkOptions? options = null)
    {
        _options = options ?? new NetworkOptions();
        _reconnectCts = new CancellationTokenSource();
    }

    /// <summary>
    /// 连接到服务器
    /// </summary>
    public async Task ConnectAsync(string host, int port, bool autoReconnect = false)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(NetworkClient));

        lock (_connectionLock)
        {
            if (_session != null)
            {
                _session.Dispose();
                _session = null;
            }
        }

        try
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            await socket.ConnectAsync(host, port);

            lock (_connectionLock)
            {
                _session = new NetworkSession(socket, _options);
                _session.CommandReceived += OnCommandReceived;
                _session.MessageReceived += OnMessageReceived;
                _session.Disconnected += OnDisconnected;
                _session.Start();
            }

            ConnectionChanged?.Invoke(true, null);

            if (autoReconnect)
            {
                StartReconnectTask(host, port);
            }
        }
        catch (Exception ex)
        {
            ConnectionChanged?.Invoke(false, ex);
            throw;
        }
    }

    /// <summary>
    /// 开始自动重连任务
    /// </summary>
    private void StartReconnectTask(string host, int port)
    {
        _reconnectCts?.Cancel();
        _reconnectCts = new CancellationTokenSource();

        Task.Run(async () =>
        {
            var token = _reconnectCts.Token;
            int retryDelay = 1000;
            const int maxRetryDelay = 30000;

            while (!token.IsCancellationRequested && !IsConnected)
            {
                try
                {
                    await Task.Delay(retryDelay, token);
                    if (!IsConnected && !token.IsCancellationRequested)
                    {
                        await ConnectAsync(host, port, false);
                    }

                    // 重置重试延迟
                    retryDelay = 1000;
                }
                catch
                {
                    // 增加重试延迟
                    retryDelay = Math.Min(retryDelay * 2, maxRetryDelay);
                }
            }
        }, _reconnectCts.Token);
    }

    /// <summary>
    /// 断开连接
    /// </summary>
    public void Disconnect()
    {
        _reconnectCts?.Cancel();

        lock (_connectionLock)
        {
            if (_session != null)
            {
                _session.Dispose();
                _session = null;
            }
        }
    }

    /// <summary>
    /// 发送命令
    /// </summary>
    public Task SendCommandAsync<T>(T command) where T : Command
    {
        EnsureConnected();
        lock (_connectionLock)
        {
            return _session!.SendCommandAsync(command);
        }
    }

    /// <summary>
    /// 发送请求并等待响应
    /// </summary>
    public Task<TResponse> SendRequestAsync<TRequest, TResponse>(TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : Request
        where TResponse : Response
    {
        EnsureConnected();
        lock (_connectionLock)
        {
            return _session!.SendRequestAsync<TRequest, TResponse>(request, cancellationToken);
        }
    }

    /// <summary>
    /// 确保已连接
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureConnected()
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("Client is not connected");
        }
    }

    /// <summary>
    /// 处理收到的命令
    /// </summary>
    private void OnCommandReceived(NetworkSession session, Command command)
    {
        CommandReceived?.Invoke(command);
    }

    /// <summary>
    /// 处理收到的消息
    /// </summary>
    private void OnMessageReceived(NetworkSession session, Message message)
    {
        MessageReceived?.Invoke(message);
    }

    /// <summary>
    /// 处理连接断开
    /// </summary>
    private void OnDisconnected(NetworkSession session, Exception ex)
    {
        ConnectionChanged?.Invoke(false, ex);
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;

            _reconnectCts?.Cancel();
            _reconnectCts?.Dispose();
            _reconnectCts = null;

            Disconnect();
        }
    }
}
