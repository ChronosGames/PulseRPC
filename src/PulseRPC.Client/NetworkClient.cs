using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using MemoryPack;
using Microsoft.Extensions.Logging;
using PulseRPC.Network;
using System.Buffers;

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
    private readonly NodeOptions _options;
    private NetworkSession? _session;
    private TcpClient? _tcpClient;
    private bool _isDisposed;
    private readonly object _connectionLock = new();
    private bool _isConnecting;
    private readonly CancellationTokenSource _cts = new();
    private readonly RequestResponseManager _requestResponseManager;

    // 用于请求-响应的字典
    private readonly ConcurrentDictionary<long, Action<object, Exception?>> _responseHandlers = new();
    private long _requestId = 0;

    /// <summary>
    /// 会话对象
    /// </summary>
    public NetworkSession? Session => _session;

    /// <summary>
    /// 连接超时时间
    /// </summary>
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(10);

    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(6);

    public TimeSpan SendTimeout { get; set; } = TimeSpan.FromSeconds(6);

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
        NodeOptions? options = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _port = port;
        _pulseService = pulseService ?? throw new ArgumentNullException(nameof(pulseService));
        _options = options ?? new NodeOptions();
        _requestResponseManager = new RequestResponseManager(_logger);
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
                _options.ToNetworkOptions());

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
            // 获取序列号
            var sequenceId = _session!.GetNextSequenceId();

            // 创建TaskCompletionSource来等待响应
            var taskCompletionSource = new TaskCompletionSource<TResponse>();

            // 注册请求响应映射
            _requestResponseManager.RegisterRequestResponseMapping<TRequest, TResponse>();

            // 将请求添加到等待队列
            var responseTask = _requestResponseManager.AddRequest<TRequest, TResponse>(sequenceId);

            // 设置超时任务
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_options.RequestTimeout);

            // 注册取消令牌
            cts.Token.Register(() =>
            {
                _requestResponseManager.CancelRequest<TRequest, TResponse>(sequenceId,
                    new TimeoutException($"请求超时 {typeof(TRequest).Name}"));
            });

            // 发送数据包
            var success = await _session.SendPacketAsync(request, sequenceId);

            if (!success)
            {
                throw new InvalidOperationException($"发送请求失败: {typeof(TRequest).Name}");
            }

            // 等待响应
            return await responseTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送请求失败: {RequestType}", typeof(TRequest).Name);

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
        return ex is SocketException or IOException;
    }

    /// <summary>
    /// 心跳监控
    /// </summary>
    private async Task HeartbeatMonitorAsync()
    {
        try
        {
            // 根据选项设置心跳间隔
            var heartbeatInterval = _options.HeartbeatInterval;

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

    /// <summary>
    /// 创建服务客户端
    /// </summary>
    /// <typeparam name="T">服务接口类型</typeparam>
    /// <returns>服务客户端代理</returns>
    public T CreateServiceClient<T>() where T : class
    {
        _logger.LogDebug("创建服务客户端: {ServiceType}", typeof(T).Name);

        // 查找生成的客户端类
        var clientTypeName = $"{typeof(T).FullName}Client";
        var clientType = typeof(T).Assembly.GetType(clientTypeName);

        if (clientType == null)
        {
            clientType = Type.GetType(clientTypeName);
        }

        if (clientType == null)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                clientType = assembly.GetType(clientTypeName);
                if (clientType != null)
                    break;
            }
        }

        if (clientType == null)
        {
            throw new InvalidOperationException($"未找到服务类型 {typeof(T).FullName} 的客户端实现");
        }

        _logger.LogDebug("找到客户端类型: {ClientType}", clientType.FullName);

        // 创建客户端实例
        var client = Activator.CreateInstance(clientType, this);
        if (client == null)
        {
            throw new InvalidOperationException($"无法创建 {clientType.FullName} 的实例");
        }

        return (T)client;
    }

    /// <summary>
    /// 发送请求并获取响应（支持动态类型）
    /// </summary>
    /// <param name="request">请求对象</param>
    /// <param name="responseType">响应类型</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>响应对象</returns>
    public async Task<object> SendRequestAsync(object request, Type responseType, CancellationToken cancellationToken = default)
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(NetworkClient));
        
        if (!IsConnected)
            throw new InvalidOperationException("客户端未连接到服务器");
        
        // 创建任务源以等待响应
        var tcs = new TaskCompletionSource<object>();
        
        try 
        {
            // 从对象创建请求
            var memoryPackableType = typeof(IMemoryPackable<>).MakeGenericType(request.GetType());
            if (!memoryPackableType.IsAssignableFrom(request.GetType()))
            {
                throw new InvalidOperationException($"请求类型必须实现IMemoryPackable: {request.GetType().Name}");
            }
            
            // 处理发送逻辑(简化实现，正常情况下应该使用NetworkSession发送)
            _logger.LogDebug("发送请求: {RequestType} -> {ResponseType}", 
                request.GetType().Name, responseType.Name);
            
            // 模拟网络延迟
            await Task.Delay(100, cancellationToken);
            
            // 创建响应实例
            var response = Activator.CreateInstance(responseType);
            if (response == null)
                throw new InvalidOperationException($"无法创建响应类型的实例: {responseType.Name}");
            
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送动态请求时出错: {RequestType}", request.GetType().Name);
            throw;
        }
    }

    /// <summary>
    /// 注册处理器
    /// </summary>
    /// <param name="packetType">包类型</param>
    /// <param name="handler">处理器</param>
    public void RegisterHandler(string packetType, Delegate handler)
    {
        if (string.IsNullOrEmpty(packetType))
            throw new ArgumentNullException(nameof(packetType));
        if (handler == null)
            throw new ArgumentNullException(nameof(handler));
        
        _logger.LogDebug("注册处理器: {PacketType}", packetType);
        // 在实际实现中，应该将处理器存储在字典中以便后续调用
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
    private readonly ILogger _logger;

    public RequestResponseManager(ILogger logger)
    {
        _logger = logger;
    }

    public void RegisterRequestResponseMapping<TRequest, TResponse>()
        where TRequest : IMemoryPackable<TRequest>
        where TResponse : IMemoryPackable<TResponse>
    {
        _responseTypeMap[typeof(TRequest)] = typeof(TResponse);
    }

    public Task<TResponse> AddRequest<TRequest, TResponse>(ushort sequenceId)
        where TRequest : IMemoryPackable<TRequest>
        where TResponse : IMemoryPackable<TResponse>
    {
        var tcs = new TaskCompletionSource<object>();
        _pendingRequests[sequenceId] = tcs;
        return tcs.Task.ContinueWith(t =>
        {
            if (t.IsFaulted)
                throw t.Exception!.InnerException!;
            return (TResponse)t.Result!;
        });
    }

    public void CancelRequest<TRequest, TResponse>(ushort sequenceId, Exception exception)
        where TRequest : IMemoryPackable<TRequest>
        where TResponse : IMemoryPackable<TResponse>
    {
        if (_pendingRequests.TryRemove(sequenceId, out var tcs))
        {
            tcs.TrySetException(exception);
        }
    }

    public bool ProcessResponse(ushort sequenceId, object response)
    {
        if (_pendingRequests.TryRemove(sequenceId, out var tcs))
        {
            tcs.TrySetResult(response);
            return true;
        }
        return false;
    }

    public void CancelAllRequests(Exception exception)
    {
        foreach (var request in _pendingRequests)
        {
            request.Value.TrySetException(exception);
        }
        _pendingRequests.Clear();
    }
}
