using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Net;
using Microsoft.Extensions.Logging;
using PulseRPC.Authentication;
using PulseRPC.Server.Transport;
using PulseRPC.Transport;

namespace PulseRPC.Server.Sessions;

/// <summary>
/// 客户端会话适配器 - 业务层到会话层的桥接实现
/// 包装IServerChannel，提供服务端业务功能
/// </summary>
internal class ClientSessionAdapter : IClientSession
{
    private readonly IServerChannel _serverChannel;
    private readonly IClientSessionManager _sessionManager;
    private readonly ILogger<ClientSessionAdapter>? _logger;
    private readonly ClientSessionDescriptor _descriptor;

    private readonly ConcurrentBag<string> _groups = new();
    private readonly ConcurrentDictionary<string, string> _tags = new();
    private readonly SessionStatistics _statistics;

    private SessionHealth _health = SessionHealth.Healthy;
    private readonly Lock _healthLock = new Lock();
    private bool _disposed;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="serverChannel">服务端通道</param>
    /// <param name="descriptor">会话描述符</param>
    /// <param name="sessionManager">会话管理器</param>
    /// <param name="logger">日志记录器</param>
    public ClientSessionAdapter(
        IServerChannel serverChannel,
        ClientSessionDescriptor descriptor,
        IClientSessionManager sessionManager,
        ILogger<ClientSessionAdapter>? logger = null)
    {
        _serverChannel = serverChannel ?? throw new ArgumentNullException(nameof(serverChannel));
        _descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _logger = logger;

        _statistics = new SessionStatistics { CreatedAt = descriptor.CreatedAt };

        // 监听底层会话通道事件并转发
        _serverChannel.StateChanged += OnSessionStateChanged;
        _serverChannel.AuthenticationChanged += OnAuthenticationChanged;
        _serverChannel.DataReceived += OnDataReceived;

        _logger?.LogDebug("客户端会话适配器已创建: SessionId={SessionId}, ConnectionId={ConnectionId}",
            descriptor.Id, _serverChannel.ConnectionId);
    }

    #region IClientSession Implementation

    /// <inheritdoc />
    public ClientSessionDescriptor Descriptor => _descriptor;

    /// <inheritdoc />
    public SessionHealth Health
    {
        get
        {
            lock (_healthLock)
            {
                return _health;
            }
        }
        private set
        {
            SessionHealth previous;
            lock (_healthLock)
            {
                previous = _health;
                _health = value;
            }

            if (previous != value)
            {
                HealthChanged?.Invoke(this, new SessionHealthChangedEventArgs
                {
                    SessionId = _descriptor.Id,
                    PreviousHealth = previous,
                    CurrentHealth = value,
                    Reason = $"State changed from {previous} to {value}"
                });
            }
        }
    }

    /// <inheritdoc />
    public SessionStatistics Statistics => _statistics;

    /// <inheritdoc />
    public bool IsAvailable => IsConnected && Health == SessionHealth.Healthy;

    /// <inheritdoc />
    public IReadOnlyList<string> Groups => _groups.ToArray();

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> Tags => new ReadOnlyDictionary<string, string>(_tags);

    /// <inheritdoc />
    public async Task<TResult> InvokeAsync<THub, TResult>(string methodName, object?[] args, CancellationToken cancellationToken = default)
        where THub : class, IPulseHub
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ClientSessionAdapter));
        if (string.IsNullOrEmpty(methodName)) throw new ArgumentException("Method name cannot be null or empty", nameof(methodName));

        try
        {
            _statistics.HubInvocations++;
            _statistics.LastActivityAt = DateTime.UtcNow;

            var result = await _sessionManager.InvokeHubMethodAsync<THub, TResult>(this, methodName, args, cancellationToken);

            _logger?.LogDebug("Hub方法调用成功: SessionId={SessionId}, Hub={HubType}, Method={MethodName}",
                _descriptor.Id, typeof(THub).Name, methodName);

            return result;
        }
        catch (Exception ex)
        {
            _statistics.Exceptions++;
            _logger?.LogError(ex, "Hub方法调用失败: SessionId={SessionId}, Hub={HubType}, Method={MethodName}",
                _descriptor.Id, typeof(THub).Name, methodName);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task InvokeAsync<THub>(string methodName, object?[] args, CancellationToken cancellationToken = default)
        where THub : class, IPulseHub
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ClientSessionAdapter));
        if (string.IsNullOrEmpty(methodName)) throw new ArgumentException("Method name cannot be null or empty", nameof(methodName));

        try
        {
            _statistics.HubInvocations++;
            _statistics.LastActivityAt = DateTime.UtcNow;

            await _sessionManager.InvokeHubMethodAsync<THub>(this, methodName, args, cancellationToken);

            _logger?.LogDebug("Hub方法调用成功: SessionId={SessionId}, Hub={HubType}, Method={MethodName}",
                _descriptor.Id, typeof(THub).Name, methodName);
        }
        catch (Exception ex)
        {
            _statistics.Exceptions++;
            _logger?.LogError(ex, "Hub方法调用失败: SessionId={SessionId}, Hub={HubType}, Method={MethodName}",
                _descriptor.Id, typeof(THub).Name, methodName);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<SessionHealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;

        try
        {
            // 检查连接状态
            if (!IsConnected)
            {
                Health = SessionHealth.Failed;
                return new SessionHealthCheckResult
                {
                    SessionId = _descriptor.Id,
                    Health = SessionHealth.Failed,
                    ResponseTime = DateTime.UtcNow - startTime,
                    Message = "连接已断开"
                };
            }

            // 检查会话超时
            var idleTime = DateTime.UtcNow - _statistics.LastActivityAt;
            if (idleTime.TotalMilliseconds > _descriptor.TimeoutMs)
            {
                Health = SessionHealth.Unhealthy;
                return new SessionHealthCheckResult
                {
                    SessionId = _descriptor.Id,
                    Health = SessionHealth.Unhealthy,
                    ResponseTime = DateTime.UtcNow - startTime,
                    Message = $"会话空闲超时: {idleTime.TotalSeconds:F1}秒"
                };
            }

            // 检查异常率
            var totalMessages = _statistics.MessagesSent + _statistics.MessagesReceived;
            if (totalMessages > 0)
            {
                var errorRate = (double)_statistics.Exceptions / totalMessages;
                if (errorRate > 0.1) // 错误率超过10%
                {
                    Health = SessionHealth.Degraded;
                    return new SessionHealthCheckResult
                    {
                        SessionId = _descriptor.Id,
                        Health = SessionHealth.Degraded,
                        ResponseTime = DateTime.UtcNow - startTime,
                        Message = $"错误率过高: {errorRate:P1}"
                    };
                }
            }

            Health = SessionHealth.Healthy;
            return new SessionHealthCheckResult
            {
                SessionId = _descriptor.Id,
                Health = SessionHealth.Healthy,
                ResponseTime = DateTime.UtcNow - startTime,
                Message = "会话健康"
            };
        }
        catch (Exception ex)
        {
            _statistics.Exceptions++;
            Health = SessionHealth.Failed;

            return new SessionHealthCheckResult
            {
                SessionId = _descriptor.Id,
                Health = SessionHealth.Failed,
                ResponseTime = DateTime.UtcNow - startTime,
                Message = $"健康检查异常: {ex.Message}"
            };
        }
    }

    /// <inheritdoc />
    public void SetGroups(IEnumerable<string> groups)
    {
        if (groups == null) throw new ArgumentNullException(nameof(groups));

        var previousGroups = Groups;
        _groups.Clear();
        foreach (var group in groups.Where(g => !string.IsNullOrWhiteSpace(g)))
        {
            _groups.Add(group.Trim());
        }

        var currentGroups = Groups;
        if (!previousGroups.SequenceEqual(currentGroups))
        {
            GroupsChanged?.Invoke(this, new SessionGroupsChangedEventArgs
            {
                SessionId = _descriptor.Id,
                PreviousGroups = previousGroups,
                CurrentGroups = currentGroups
            });

            _logger?.LogDebug("会话组已更新: SessionId={SessionId}, Groups=[{Groups}]",
                _descriptor.Id, string.Join(", ", currentGroups));
        }
    }

    /// <inheritdoc />
    public void SetTag(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Tag key cannot be null or whitespace", nameof(key));
        if (value == null) throw new ArgumentNullException(nameof(value));

        _tags[key.Trim()] = value;

        _logger?.LogDebug("会话标签已设置: SessionId={SessionId}, Key={Key}, Value={Value}",
            _descriptor.Id, key, value);
    }

    /// <inheritdoc />
    public bool RemoveTag(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;

        var removed = _tags.TryRemove(key.Trim(), out _);

        if (removed)
        {
            _logger?.LogDebug("会话标签已移除: SessionId={SessionId}, Key={Key}",
                _descriptor.Id, key);
        }

        return removed;
    }

    /// <inheritdoc />
    public event EventHandler<SessionHealthChangedEventArgs>? HealthChanged;

    /// <inheritdoc />
    public event EventHandler<SessionGroupsChangedEventArgs>? GroupsChanged;

    #endregion

    #region ISessionChannel Delegation

    /// <inheritdoc />
    public string ConnectionId => _serverChannel.ConnectionId;

    /// <inheritdoc />
    public IAuthenticationContext? AuthenticationContext
    {
        get => _serverChannel.AuthenticationContext;
        set => _serverChannel.AuthenticationContext = value;
    }

    /// <inheritdoc />
    public bool IsAuthenticated => _serverChannel.IsAuthenticated;

    /// <inheritdoc />
    public IDictionary<string, object> Properties => _serverChannel.Properties;

    /// <inheritdoc />
    public string RemoteAddress => _serverChannel.RemoteAddress;

    /// <inheritdoc />
    public void SetAuthentication(IAuthenticationContext authContext) => _serverChannel.SetAuthentication(authContext);

    /// <inheritdoc />
    public void ClearAuthentication() => _serverChannel.ClearAuthentication();

    /// <inheritdoc />
    public T? GetProperty<T>(string key) => _serverChannel.GetProperty<T>(key);

    /// <inheritdoc />
    public void SetProperty<T>(string key, T value) => _serverChannel.SetProperty(key, value);

    /// <inheritdoc />
    public bool RemoveProperty(string key) => _serverChannel.RemoveProperty(key);

    /// <inheritdoc />
    public bool HasProperty(string key) => _serverChannel.HasProperty(key);

    #endregion

    #region ITransportConnection Delegation

    /// <inheritdoc />
    public ConnectionState State => _serverChannel.State;

    /// <inheritdoc />
    public EndPoint RemoteEndPoint => _serverChannel.RemoteEndPoint;

    /// <inheritdoc />
    public EndPoint LocalEndPoint => _serverChannel.LocalEndPoint;

    /// <inheritdoc />
    public DateTime ConnectedAt => _serverChannel.ConnectedAt;

    /// <inheritdoc />
    public DateTime LastActivityAt => _serverChannel.LastActivityAt;

    /// <inheritdoc />
    public TransportType TransportType => _serverChannel.TransportType;

    /// <inheritdoc />
    public bool IsConnected => _serverChannel.IsConnected;

    /// <inheritdoc />
    public async Task<bool> SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (_disposed) return false;

        _statistics.MessagesSent++;
        _statistics.BytesSent += data.Length;
        _statistics.LastActivityAt = DateTime.UtcNow;

        return await _serverChannel.SendAsync(data, cancellationToken);
    }

    /// <inheritdoc />
    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return;
        await _serverChannel.CloseAsync(cancellationToken);
    }

    #endregion

    #region Event Forwarding

    /// <inheritdoc />
    public event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;

    /// <inheritdoc />
    public event EventHandler<TransportDataEventArgs>? DataReceived;

    /// <inheritdoc />
    public event EventHandler<AuthenticationChangedEventArgs>? AuthenticationChanged;

    /// <summary>
    /// 处理会话状态变更事件
    /// </summary>
    private void OnSessionStateChanged(object? sender, ConnectionStateChangedEventArgs e)
    {
        // 更新健康状态
        Health = e.CurrentState switch
        {
            ConnectionState.Connected => SessionHealth.Healthy,
            ConnectionState.Connecting => SessionHealth.Degraded,
            ConnectionState.Disconnected => SessionHealth.Failed,
            ConnectionState.Failed => SessionHealth.Failed,
            _ => SessionHealth.Unhealthy
        };

        // 转发状态变更事件
        StateChanged?.Invoke(this, e);

        _logger?.LogDebug("会话状态已变更: SessionId={SessionId}, ConnectionId={ConnectionId}, State={OldState}->{NewState}, Health={Health}",
            _descriptor.Id, e.ConnectionId, e.PreviousState, e.CurrentState, Health);
    }

    /// <summary>
    /// 处理认证变更事件
    /// </summary>
    private void OnAuthenticationChanged(object? sender, AuthenticationChangedEventArgs e)
    {
        AuthenticationChanged?.Invoke(this, e);

        _logger?.LogDebug("会话认证已变更: SessionId={SessionId}, ConnectionId={ConnectionId}, Authenticated={IsAuthenticated}",
            _descriptor.Id, e.ConnectionId, IsAuthenticated);
    }

    /// <summary>
    /// 处理数据接收事件
    /// </summary>
    private void OnDataReceived(object? sender, TransportDataEventArgs e)
    {
        _statistics.MessagesReceived++;
        _statistics.BytesReceived += e.Data.Length;
        _statistics.LastActivityAt = DateTime.UtcNow;

        DataReceived?.Invoke(this, e);
    }

    #endregion

    #region IDisposable Implementation

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // 取消事件订阅
        _serverChannel.StateChanged -= OnSessionStateChanged;
        _serverChannel.AuthenticationChanged -= OnAuthenticationChanged;
        _serverChannel.DataReceived -= OnDataReceived;

        _logger?.LogDebug("客户端会话适配器已释放: SessionId={SessionId}, ConnectionId={ConnectionId}",
            _descriptor.Id, _serverChannel.ConnectionId);
    }

    #endregion
}

/// <summary>
/// 客户端会话管理器接口
/// </summary>
public interface IClientSessionManager
{
    /// <summary>
    /// 调用客户端Hub方法（有返回值）
    /// </summary>
    Task<TResult> InvokeHubMethodAsync<THub, TResult>(IClientSession session, string methodName, object?[] args, CancellationToken cancellationToken)
        where THub : class, IPulseHub;

    /// <summary>
    /// 调用客户端Hub方法（无返回值）
    /// </summary>
    Task InvokeHubMethodAsync<THub>(IClientSession session, string methodName, object?[] args, CancellationToken cancellationToken)
        where THub : class, IPulseHub;
}
