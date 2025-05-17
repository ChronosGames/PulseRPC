using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using MemoryPack;
using Microsoft.Extensions.Logging;
using PulseRPC.Network;

namespace PulseRPC.Client;

/// <summary>
/// 网络客户端
/// </summary>
public class NetworkClient : IDisposable
{
    private readonly ILogger _logger;
    private readonly string _host;
    private readonly int _port;
    private readonly IPulseService _pulseService;
    private readonly NetworkOptions _options;
    private NetworkSession? _session;
    private TcpClient? _tcpClient;
    private bool _isDisposed;
    private readonly object _connectionLock = new();
    private bool _isConnecting;
    private readonly CancellationTokenSource _cts = new();

    /// <summary>
    /// 会话对象
    /// </summary>
    public NetworkSession? Session => _session;

    /// <summary>
    /// 连接超时时间
    /// </summary>
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 是否已连接
    /// </summary>
    public bool IsConnected => _session != null && _tcpClient?.Connected == true;

    /// <summary>
    /// 连接断开事件
    /// </summary>
    public event EventHandler? Disconnected;

    /// <summary>
    /// 初始化网络客户端
    /// </summary>
    /// <param name="logger">日志</param>
    /// <param name="host">服务器地址</param>
    /// <param name="port">服务器端口</param>
    /// <param name="pulseService">PulseRPC服务</param>
    /// <param name="options">网络选项</param>
    public NetworkClient(
        ILogger logger,
        string host,
        int port,
        IPulseService pulseService,
        NetworkOptions? options = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _port = port;
        _pulseService = pulseService ?? throw new ArgumentNullException(nameof(pulseService));
        _options = options ?? new NetworkOptions();
    }

    /// <summary>
    /// 连接到服务器
    /// </summary>
    /// <returns>异步任务</returns>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        // 防止并发连接
        lock (_connectionLock)
        {
            if (_isConnecting)
                return;

            if (IsConnected)
                return;

            _isConnecting = true;
        }

        try
        {
            _logger.LogDebug("正在连接到服务器 {Host}:{Port}", _host, _port);

            // 创建新的TCP客户端
            _tcpClient = new TcpClient();

            // 使用任务超时确保连接超时处理
            using var timeoutCts = new CancellationTokenSource(ConnectionTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                timeoutCts.Token, cancellationToken, _cts.Token);

            try
            {
            #if NET5_0_OR_GREATER
                await _tcpClient.ConnectAsync(_host, _port, linkedCts.Token);
            #else
                await _tcpClient.ConnectAsync(_host, _port);
            #endif
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                throw new TimeoutException($"连接服务器 {_host}:{_port} 超时");
            }

            // 创建NetworkSession
            var socket = _tcpClient.Client;
            _session = new NetworkSession(
                _logger,
                socket,
                _pulseService,
                _options);

            // 订阅断开连接事件
            _session.Disconnected += OnSessionDisconnected;

            _logger.LogInformation("已连接到服务器 {Host}:{Port}", _host, _port);

            // 开始处理网络消息
            _ = Task.Run(() => _session.ProcessMessagesAsync());

            // 启动心跳检测
            _ = Task.Run(HeartbeatMonitorAsync);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "连接服务器 {Host}:{Port} 失败", _host, _port);

            // 清理资源
            CleanupConnection();

            throw;
        }
        finally
        {
            lock (_connectionLock)
            {
                _isConnecting = false;
            }
        }
    }

    /// <summary>
    /// 发送请求
    /// </summary>
    /// <param name="request">请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>响应</returns>
    public async Task<TResponse> SendRequestAsync<TRequest, TResponse>(
        TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : IMemoryPackable<TRequest>
        where TResponse : IMemoryPackable<TResponse>
    {
        // 检查连接状态
        EnsureConnected();

        // 使用NetworkSession发送消息
        try
        {
            var sequenceId = _session!.GetNextSequenceId();
            var success = await _session.SendPacketAsync(request, sequenceId);

            if (!success)
            {
                throw new InvalidOperationException("发送请求失败");
            }

            // 实际实现中需要等待响应
            // 这里仅作示例，实际实现应使用TaskCompletionSource等待响应
            return default!;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送请求失败");

            // 检查是否为连接异常
            if (IsConnectionException(ex))
            {
                // 标记连接断开，触发事件
                OnDisconnected();
            }

            throw;
        }
    }

    /// <summary>
    /// 发送单向消息
    /// </summary>
    public async Task SendOneWayAsync<T>(T message, CancellationToken cancellationToken = default)
        where T : IMemoryPackable<T>
    {
        EnsureConnected();

        try
        {
            var sequenceId = _session!.GetNextSequenceId();
            await _session.SendPacketAsync(message, sequenceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送单向消息失败");

            // 检查是否为连接异常
            if (IsConnectionException(ex))
            {
                // 标记连接断开，触发事件
                OnDisconnected();
            }

            throw;
        }
    }

    /// <summary>
    /// 处理连接断开
    /// </summary>
    private void OnSessionDisconnected(NetworkSession session, Exception ex)
    {
        _logger.LogInformation("会话断开: {Message}", ex?.Message ?? "未知原因");
        OnDisconnected();
    }

    /// <summary>
    /// 处理连接断开
    /// </summary>
    private void OnDisconnected()
    {
        try
        {
            CleanupConnection();

            // 触发断开事件
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理连接断开时出错");
        }
    }

    /// <summary>
    /// 清理连接资源
    /// </summary>
    private void CleanupConnection()
    {
        try
        {
            if (_session != null)
            {
                _session.Disconnected -= OnSessionDisconnected;
                _session.Dispose();
                _session = null;
            }

            if (_tcpClient != null)
            {
                _tcpClient.Dispose();
                _tcpClient = null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "清理连接资源时出错");
        }
    }

    /// <summary>
    /// 确保已连接
    /// </summary>
    private void EnsureConnected()
    {
        if (!IsConnected || _session == null)
        {
            throw new InvalidOperationException("客户端未连接到服务器");
        }
    }

    /// <summary>
    /// 判断是否为连接异常
    /// </summary>
    /// <param name="ex">异常</param>
    /// <returns>是否为连接异常</returns>
    private bool IsConnectionException(Exception ex)
    {
        return ex is SocketException ||
               ex is IOException ||
               ex is ObjectDisposedException;
    }

    /// <summary>
    /// 心跳监控
    /// </summary>
    private async Task HeartbeatMonitorAsync()
    {
        try
        {
            // 根据选项设置心跳间隔
            var heartbeatInterval = TimeSpan.FromMilliseconds(_options.HeartbeatInterval);

            while (IsConnected && !_isDisposed && !_cts.IsCancellationRequested)
            {
                try
                {
                    // 等待下一个心跳时间
                    await Task.Delay(heartbeatInterval, _cts.Token);

                    if (!IsConnected)
                    {
                        _logger.LogWarning("心跳检测发现连接已断开");
                        OnDisconnected();
                        break;
                    }

                    // 发送心跳包
                    // 这里需要实现心跳包的发送，具体实现取决于协议
                    // await SendOneWayAsync(new HeartbeatMessage(), _cts.Token);
                }
                catch (OperationCanceledException) when (_cts.IsCancellationRequested)
                {
                    // 正常取消
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "心跳检测出错");

                    if (IsConnectionException(ex))
                    {
                        OnDisconnected();
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "心跳监控线程异常");
        }
    }

    /// <summary>
    /// 关闭连接
    /// </summary>
    public void Close()
    {
        _cts.Cancel();
        CleanupConnection();
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

        _cts.Cancel();
        _cts.Dispose();

        CleanupConnection();

        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// 心跳消息
/// </summary>
[MemoryPackable]
public partial class HeartbeatMessage
{
    /// <summary>
    /// 时间戳
    /// </summary>
    public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

/// <summary>
/// 请求-响应管理器
/// </summary>
internal class RequestResponseManager
{
    private readonly ConcurrentDictionary<ushort, TaskCompletionSource<object>> _pendingRequests = new();
    private readonly ConcurrentDictionary<Type, Type> _responseTypeMap = new();

    /// <summary>
    /// 注册请求类型到响应类型的映射
    /// </summary>
    public void RegisterRequestResponseMapping<TRequest, TResponse>()
        where TRequest : IMemoryPackable<TRequest>
        where TResponse : IMemoryPackable<TResponse>
    {
        _responseTypeMap[typeof(TRequest)] = typeof(TResponse);
    }

    /// <summary>
    /// 添加待处理请求
    /// </summary>
    public Task<TResponse> AddRequest<TRequest, TResponse>(ushort sequenceId)
        where TRequest : IMemoryPackable<TRequest>
        where TResponse : IMemoryPackable<TResponse>
    {
        var tcs = new TaskCompletionSource<object>();
        _pendingRequests[sequenceId] = tcs;
        return tcs.Task.ContinueWith(t => (TResponse)t.Result);
    }

    /// <summary>
    /// 处理响应
    /// </summary>
    public bool ProcessResponse(ushort sequenceId, object response)
    {
        if (_pendingRequests.TryRemove(sequenceId, out var tcs))
        {
            tcs.TrySetResult(response);
            return true;
        }
        return false;
    }

    /// <summary>
    /// 取消所有待处理请求
    /// </summary>
    public void CancelAllRequests(Exception exception)
    {
        foreach (var kvp in _pendingRequests)
        {
            kvp.Value.TrySetException(exception);
        }
        _pendingRequests.Clear();
    }
}
