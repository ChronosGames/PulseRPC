using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC.Client.Transport;
using System.Collections.Concurrent;
using PulseRPC.Transport;

namespace PulseRPC.Client.Core;

/// <summary>
/// 连接上下文实现 - 表示一个活跃的连接实例
/// </summary>
public sealed class ConnectionContext : IConnectionContext
{
    private readonly IClientTransport _transport;
    private readonly ILogger _logger;
    private readonly ConnectionStateMachine _stateMachine;
    private readonly ConnectionStatistics _statistics;
    private readonly ConcurrentDictionary<Type, object> _serviceProxyCache = new();
    private readonly object _lock = new();
    private volatile bool _disposed;

    /// <summary>
    /// 连接ID
    /// </summary>
    public string Id => Descriptor.Id;

    /// <summary>
    /// 连接配置（从描述符转换而来）
    /// </summary>
    public ConnectionConfig Config { get; }

    /// <summary>
    /// 连接描述符
    /// </summary>
    public ConnectionDescriptor Descriptor { get; }

    /// <summary>
    /// 端点地址
    /// </summary>
    public EndpointAddress Endpoint { get; }

    /// <summary>
    /// 连接状态
    /// </summary>
    public ExtendedConnectionState State => _stateMachine.CurrentState;

    /// <summary>
    /// 连接统计信息
    /// </summary>
    public ConnectionStatistics Statistics => _statistics;

    /// <summary>
    /// 远程端点地址
    /// </summary>
    public System.Net.EndPoint? RemoteEndPoint => _transport.RemoteEndPoint;

    /// <summary>
    /// 本地端点地址
    /// </summary>
    public System.Net.EndPoint? LocalEndPoint => _transport.LocalEndPoint;

    /// <summary>
    /// 连接创建时间
    /// </summary>
    public DateTime CreatedAt => _transport.ConnectedAt;

    /// <summary>
    /// 最后活动时间
    /// </summary>
    public DateTime LastActivityAt => _transport.LastActivityAt;

    /// <summary>
    /// 连接状态变化事件
    /// </summary>
    public event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;

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

        // 从描述符创建配置
        Config = new ConnectionConfig
        {
            Name = descriptor.Name,
            ServiceName = descriptor.ServiceName,
            Host = endpoint.Host,
            Port = endpoint.Port,
            Transport = descriptor.Transport,
            Lifetime = descriptor.Strategy switch
            {
                ConnectionStrategy.Persistent => ConnectionLifetime.Persistent,
                ConnectionStrategy.Session => ConnectionLifetime.Session,
                ConnectionStrategy.Transient => ConnectionLifetime.Transient,
                ConnectionStrategy.Pooled => ConnectionLifetime.Session,
                _ => ConnectionLifetime.Session
            },
            AutoReconnect = descriptor.AutoReconnect,
            IdleTimeout = descriptor.IdleTimeout,
            Tags = new Dictionary<string, string>(descriptor.Tags),
            ConnectTimeout = descriptor.ConnectTimeout,
            Options = descriptor.TransportOptions
        };

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
    /// 获取服务代理
    /// </summary>
    public async Task<T> GetServiceAsync<T>() where T : class, IPulseHub
    {
        ThrowIfDisposed();

        if (!_stateMachine.IsAvailable())
        {
            throw new InvalidOperationException($"连接不可用，当前状态: {State}");
        }

        // 使用缓存避免重复创建代理
        var serviceType = typeof(T);
        if (_serviceProxyCache.TryGetValue(serviceType, out var cachedProxy))
        {
            UpdateActivity();
            return (T)cachedProxy;
        }

        // 这里应该通过 Source Generator 生成的扩展方法来创建代理
        // 暂时抛出异常提示需要实现
        throw new NotImplementedException(
            $"服务代理 {serviceType.Name} 需要通过 Source Generator 生成。" +
            "请确保项目正确引用了 PulseRPC.Client.SourceGenerator。");
    }

    /// <summary>
    /// 注册事件监听器
    /// </summary>
    public async Task<ISubscriptionToken> RegisterEventListenerAsync<T>(T listener) where T : class, IPulseEventHandler
    {
        ThrowIfDisposed();

        if (!_stateMachine.IsAvailable())
        {
            throw new InvalidOperationException($"连接不可用，当前状态: {State}");
        }

        // 这里应该通过 Source Generator 生成的扩展方法来注册监听器
        // 暂时抛出异常提示需要实现
        throw new NotImplementedException(
            $"事件监听器 {typeof(T).Name} 需要通过 Source Generator 生成。" +
            "请确保项目正确引用了 PulseRPC.Client.SourceGenerator。");
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
        StateChanged?.Invoke(this, e);
    }

    /// <summary>
    /// 处理传输层状态变化
    /// </summary>
    private void OnTransportStateChanged(object? sender, PulseRPC.Transport.ConnectionStateChangedEventArgs e)
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
            default:
                throw new NotSupportedException();
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

        // 清理代理缓存
        _serviceProxyCache.Clear();

        _logger.LogDebug("连接上下文已释放: {ConnectionId}", Id);
    }

    public override string ToString()
    {
        return $"Connection[{Id}]: {State} -> {Endpoint}";
    }
}

/// <summary>
/// 简单的订阅令牌实现
/// </summary>
// public sealed class SubscriptionToken : ISubscriptionToken
// {
//     private readonly Action _unsubscribeAction;
//     private volatile bool _isDisposed;
//
//     public SubscriptionToken(Action unsubscribeAction)
//     {
//         _unsubscribeAction = unsubscribeAction ?? throw new ArgumentNullException(nameof(unsubscribeAction));
//     }
//
//     /// <summary>
//     /// 取消订阅
//     /// </summary>
//     public async Task UnsubscribeAsync()
//     {
//         if (_isDisposed)
//             return;
//
//         _isDisposed = true;
//         _unsubscribeAction?.Invoke();
//         await Task.CompletedTask;
//     }
//
//     /// <summary>
//     /// 释放资源
//     /// </summary>
//     public void Dispose()
//     {
//         UnsubscribeAsync().Wait(TimeSpan.FromSeconds(5));
//     }
// }

/// <summary>
/// 订阅令牌接口
/// </summary>
// public interface ISubscriptionToken : IDisposable
// {
//     /// <summary>
//     /// 取消订阅
//     /// </summary>
//     Task UnsubscribeAsync();
// }
