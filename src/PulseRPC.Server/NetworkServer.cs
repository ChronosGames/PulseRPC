using System.Net;
using System.Net.Sockets;
using MemoryPack;
using Microsoft.Extensions.Logging;
using PulseRPC.Network;

namespace PulseRPC.Server;

/// <summary>
/// 网络服务器
/// </summary>
public class NetworkServer : IDisposable
{
    private readonly ILogger _logger;
    private readonly NetworkOptions _options;
    private readonly IPulseService _pulseService;
    private readonly IClientSessionManager _sessionManager;
    private readonly Socket _listenSocket;
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
    public NetworkServer(ILogger<NetworkServer> logger, IPulseService pulseService,
        IClientSessionManager sessionManager,
        NetworkOptions? options = null)
    {
        _logger = logger;
        _options = options ?? new NetworkOptions();
        _sessionManager = sessionManager;
        _pulseService = pulseService ?? throw new ArgumentNullException(nameof(pulseService));
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
            _ = AcceptClientsAsync();
        }
        catch (Exception e)
        {
            ErrorOccurred?.Invoke(e);
            _logger.LogError(e, $"启动服务器时出错");
            throw;
        }

        // 等待启动完成
        await Task.CompletedTask;
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
                var clientSocket = await _listenSocket.AcceptAsync(_cts.Token);

                // 设置远程地址信息
                var remoteEndPoint = clientSocket.RemoteEndPoint as IPEndPoint;
                var clientId = $"{remoteEndPoint!.Address}:{remoteEndPoint.Port}";

                _logger.LogDebug($"接受新连接: {clientId}");

                // 创建网络会话
                var session = new NetworkSession(_logger, clientSocket, _pulseService, _options);

                // 注册会话到会话管理器
                _sessionManager.RegisterSession(clientId, session);

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
            // 从会话管理器中注销会话
            _sessionManager.UnregisterSession(clientId);

            // 关闭会话
            session.Dispose();

            // 触发事件
            ClientDisconnected?.Invoke(session, null);
            _logger.LogDebug($"客户端已断开: {clientId}");
        }
    }

    /// <summary>
    /// 基于类型ID和客户端ID创建会话组
    /// </summary>
    public void CreateTypeBasedGroup<T>(string groupId) where T : IMemoryPackable<T>
    {
        var typeName = typeof(T).Name;
        _logger.LogInformation($"为类型 {typeName} 创建会话组 {groupId}");
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

        try
        {
            _listenSocket.Dispose();
        }
        catch
        {
            // 忽略异常
        }

        GC.SuppressFinalize(this);
    }
}
