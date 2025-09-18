using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Generic;
using PulseRPC.Transport;

namespace PulseRPC.Client;

/// <summary>
/// 连接上下文实现 - 表示一个活跃的连接实例
/// </summary>
public sealed class ConnectionContext : IConnectionContext
{
    private readonly IClientTransport _transport;
    private readonly ILogger _logger;
    private readonly ConnectionStateMachine _stateMachine;
    private readonly ConnectionStatistics _statistics;
    private readonly Dictionary<string, string> _tags;
    private readonly object _lock = new();
    private volatile bool _disposed;

    /// <summary>
    /// 连接ID
    /// </summary>
    public string Id => Descriptor.Id;

    /// <summary>
    /// 连接描述符
    /// </summary>
    public ConnectionDescriptor Descriptor { get; }

    /// <summary>
    /// 端点地址
    /// </summary>
    public EndpointAddress Endpoint { get; }

    public string Name => _transport.Name;
    public TransportType Type => _transport.Type;
    public bool IsConnected => _transport.IsConnected;
    ConnectionState ITransport.State => _transport.State;

    /// <summary>
    /// 连接状态
    /// </summary>
    public ExtendedConnectionState State => _stateMachine.CurrentState;

    /// <summary>
    /// 连接统计信息
    /// </summary>
    public ConnectionStatistics Statistics => _statistics;

    /// <summary>
    /// 连接标签
    /// </summary>
    public Dictionary<string, string> Tags => _tags;

    /// <summary>
    /// 远程端点地址
    /// </summary>
    public System.Net.EndPoint RemoteEndPoint => _transport.RemoteEndPoint;

    event EventHandler<TransportStateEventArgs>? ITransport.StateChanged
    {
        add => _transport.StateChanged += value;
        remove => _transport.StateChanged -= value;
    }

    public event EventHandler<TransportDataEventArgs>? DataReceived;

    public Task<bool> SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        return _transport.SendAsync(data, cancellationToken);
    }

    /// <summary>
    /// 本地端点地址
    /// </summary>
    public System.Net.EndPoint LocalEndPoint => _transport.LocalEndPoint;

    /// <summary>
    /// 连接创建时间
    /// </summary>
    public DateTime CreatedAt => _statistics.CreatedAt;

    /// <summary>
    /// 最后活动时间
    /// </summary>
    public DateTime LastActivityAt => _statistics.LastActiveAt;

    /// <summary>
    /// 构造函数
    /// </summary>
    public ConnectionContext(
        ConnectionDescriptor descriptor,
        EndpointAddress endpoint,
        IClientTransport transport,
        ILogger? logger = null)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _logger = logger ?? NullLogger.Instance;

        // 初始化状态机
        _stateMachine = new ConnectionStateMachine(descriptor.Id);
        _stateMachine.StateChanged += OnStateMachineStateChanged;

        // 初始化统计信息
        _statistics = new ConnectionStatistics
        {
            ConnectionId = descriptor.Id,
            CreatedAt = DateTime.UtcNow,
            LastActiveAt = DateTime.UtcNow
        };

        // 初始化标签（从描述符复制）
        _tags = new Dictionary<string, string>(descriptor.Tags);

        // 监听传输层状态变化
        _transport.StateChanged += OnTransportStateChanged;
    }


    /// <summary>
    /// 连接到服务器
    /// </summary>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        lock (_lock)
        {
            if (!_stateMachine.TryTransition(ExtendedConnectionState.Initializing, "开始连接"))
            {
                throw new InvalidOperationException($"无法从 {State} 状态开始连接");
            }
        }

        try
        {
            _stateMachine.TryTransition(ExtendedConnectionState.Connecting, "正在连接传输层");

            await _transport.ConnectAsync(Endpoint.Host, Endpoint.Port, cancellationToken);

            _stateMachine.TryTransition(ExtendedConnectionState.Connected, "传输层连接成功");
            _statistics.ConnectedAt = DateTime.UtcNow;

            _logger.LogInformation("连接建立成功: {ConnectionId} -> {Endpoint}", Id, Endpoint);
        }
        catch (Exception ex)
        {
            _stateMachine.TryTransition(ExtendedConnectionState.Failed, "连接失败", ex);
            _logger.LogError(ex, "连接失败: {ConnectionId} -> {Endpoint}", Id, Endpoint);
            throw;
        }
    }

    /// <summary>
    /// 断开连接
    /// </summary>
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed || State == ExtendedConnectionState.Disconnected)
        {
            return;
        }

        lock (_lock)
        {
            if (!_stateMachine.TryTransition(ExtendedConnectionState.Disconnecting, "主动断开连接"))
            {
                // 如果无法转换到断开状态，强制设置
                _stateMachine.ForceSetState(ExtendedConnectionState.Disconnecting, "强制断开连接");
            }
        }

        try
        {
            await _transport.DisconnectAsync(cancellationToken);
            _stateMachine.TryTransition(ExtendedConnectionState.Disconnected, "传输层断开成功");
            _logger.LogInformation("连接已断开: {ConnectionId}", Id);
        }
        catch (Exception ex)
        {
            _stateMachine.TryTransition(ExtendedConnectionState.Failed, "断开连接失败", ex);
            _logger.LogError(ex, "断开连接失败: {ConnectionId}", Id);
            throw;
        }
    }


    /// <summary>
    /// 更新活跃时间
    /// </summary>
    private void UpdateActivity()
    {
        _statistics.LastActiveAt = DateTime.UtcNow;

        // 如果当前是空闲状态，转换为活跃状态
        if (State == ExtendedConnectionState.Idle)
        {
            _stateMachine.TryTransition(ExtendedConnectionState.Active, "检测到活动");
        }
    }

    /// <summary>
    /// 处理状态机状态变化
    /// </summary>
    private void OnStateMachineStateChanged(object? sender, ConnectionStateChangedEventArgs e)
    {
        this.ConnectionStateChanged?.Invoke(this, e);
    }

    /// <summary>
    /// 处理传输层状态变化
    /// </summary>
    private void OnTransportStateChanged(object? sender, TransportStateEventArgs e)
    {
        ExtendedConnectionState newState;
        switch (e.CurrentState)
        {
            case ConnectionState.Connecting:
                newState = ExtendedConnectionState.Connecting;
                break;
            case ConnectionState.Connected:
                newState = ExtendedConnectionState.Connected;
                break;
            case ConnectionState.Disconnected:
                newState = ExtendedConnectionState.Disconnected;
                break;
            case ConnectionState.Failed:
                newState = ExtendedConnectionState.Failed;
                break;
            default:
                newState = ExtendedConnectionState.Uninitialized;
                break;
        }

        var reason = e.Reason ?? "传输层状态变化";
        _stateMachine.TryTransition(newState, reason, e.Exception);
    }

    /// <summary>
    /// 检查是否已释放
    /// </summary>
    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ConnectionContext));
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

        _stateMachine.TryTransition(ExtendedConnectionState.Disposed, "连接上下文被释放");

        // 取消事件订阅
        _stateMachine.StateChanged -= OnStateMachineStateChanged;
        _transport.StateChanged -= OnTransportStateChanged;

        // 释放传输层资源
        try
        {
            _transport.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "释放传输层资源时发生错误: {ConnectionId}", Id);
        }


        _logger.LogDebug("连接上下文已释放: {ConnectionId}", Id);
    }

    public override string ToString()
    {
        return $"Connection[{Id}]: {State} -> {Endpoint}";
    }

    /// <summary>
    /// 发送消息（用于 Source Generator 生成的代理）
    /// </summary>
    public ValueTask SendAsync<T>(string hubName, string methodName, in T message, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!_stateMachine.IsAvailable())
        {
            throw new InvalidOperationException($"连接不可用，当前状态: {State}");
        }

        // 序列化消息并通过传输层发送
        // 这里需要根据实际的消息序列化和传输协议来实现
        // 暂时抛出 NotImplementedException，等待具体的传输层实现
        throw new NotImplementedException("消息发送功能需要传输层的具体实现");
    }

    /// <summary>
    /// 发送消息并接收响应（用于 Source Generator 生成的代理）
    /// </summary>
    public ValueTask<TResponse> InvokeAsync<TResponse>(string hubName, string methodName, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!_stateMachine.IsAvailable())
        {
            throw new InvalidOperationException($"连接不可用，当前状态: {State}");
        }

        // 发送消息并等待响应
        // 这里需要根据实际的请求-响应模式来实现
        // 暂时抛出 NotImplementedException，等待具体的传输层实现
        throw new NotImplementedException("请求-响应功能需要传输层的具体实现");
    }

    /// <summary>
    /// 发送消息并接收响应（用于 Source Generator 生成的代理）
    /// </summary>
    public ValueTask<TResponse> InvokeAsync<TRequest, TResponse>(string hubName, string methodName, in TRequest request,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!_stateMachine.IsAvailable())
        {
            throw new InvalidOperationException($"连接不可用，当前状态: {State}");
        }

        // 发送消息并等待响应
        // 这里需要根据实际的请求-响应模式来实现
        // 暂时抛出 NotImplementedException，等待具体的传输层实现
        throw new NotImplementedException("请求-响应功能需要传输层的具体实现");
    }

    /// <summary>
    /// 注册事件监听器的内部实现（用于 Source Generator 生成的扩展方法）
    /// </summary>
    public Task<ISubscriptionToken> RegisterReceiverAsync<T>(T listener, CancellationToken cancellationToken = default) where T : class, IPulseReceiver
    {
        ThrowIfDisposed();

        if (!_stateMachine.IsAvailable())
        {
            throw new InvalidOperationException($"连接不可用，当前状态: {State}");
        }

        // 注册事件监听器
        // 这里需要根据实际的事件订阅机制来实现
        // 暂时抛出 NotImplementedException，等待具体的事件系统实现
        throw new NotImplementedException("事件监听器注册功能需要事件系统的具体实现");
    }

    public event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;
}
