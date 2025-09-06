using System.Collections.Concurrent;
using PulseRPC.Authentication;
using PulseRPC.Transport;
using Microsoft.Extensions.Logging;

namespace PulseRPC.Server.Transport;

public interface IServerChannel : ITransportChannel
{
}

/// <summary>
/// 服务器传输通道实现，包装 IServerConnection 并提供认证和会话管理
/// </summary>
public class ServerTransportChannel : IServerChannel
{
    private readonly IServerTransport _transport;
    private readonly ConcurrentDictionary<string, object> _properties;
    private readonly Lock _authLock = new Lock();
    private readonly ILogger<ServerTransportChannel>? _logger;

    private IAuthenticationContext? _authenticationContext;
    private DateTime _lastActiveTime;
    private bool _disposed;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="transport">底层传输连接</param>
    /// <param name="logger">日志记录器</param>
    public ServerTransportChannel(IServerTransport transport, ILogger<ServerTransportChannel>? logger = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _properties = new ConcurrentDictionary<string, object>();
        ConnectedTime = DateTime.UtcNow;
        _lastActiveTime = ConnectedTime;
        _logger = logger;

        // 转发传输层事件
        _transport.StateChanged += OnTransportStateChanged;
        _transport.DataReceived += OnTransportDataReceived;
    }

    /// <inheritdoc />
    public string ConnectionId => _transport.ConnectionId;

    /// <inheritdoc />
    public IServerTransport Transport => _transport;

    /// <inheritdoc />
    public IAuthenticationContext? AuthenticationContext
    {
        get
        {
            lock (_authLock)
            {
                return _authenticationContext;
            }
        }
        set
        {
            lock (_authLock)
            {
                _authenticationContext = value;
            }
        }
    }

    /// <inheritdoc />
    public bool IsAuthenticated => AuthenticationContext?.IsAuthenticated ?? false;

    /// <inheritdoc />
    public IDictionary<string, object> Properties => _properties;

    /// <inheritdoc />
    public string RemoteAddress => _transport.RemoteEndPoint?.ToString() ?? "Unknown";

    /// <inheritdoc />
    public DateTime ConnectedTime { get; }

    /// <inheritdoc />
    public DateTime LastActiveTime
    {
        get => _lastActiveTime;
        set => _lastActiveTime = value;
    }

    /// <inheritdoc />
    public void SetAuthentication(IAuthenticationContext authContext)
    {
        if (authContext == null) throw new ArgumentNullException(nameof(authContext));

        lock (_authLock)
        {
            _authenticationContext = authContext;
            LastActiveTime = DateTime.UtcNow;
        }
    }

    /// <inheritdoc />
    public void ClearAuthentication()
    {
        lock (_authLock)
        {
            _authenticationContext?.Clear();
            _authenticationContext = null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (_disposed) return false;

        LastActiveTime = DateTime.UtcNow;
        return await _transport.SendAsync(data, cancellationToken);
    }

    /// <inheritdoc />
    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return;

        await _transport.CloseAsync(cancellationToken);
    }

    /// <inheritdoc />
    public event System.EventHandler<TransportStateEventArgs>? StateChanged;

    /// <inheritdoc />
    public event System.EventHandler<TransportDataEventArgs>? DataReceived;

    /// <summary>
    /// 处理传输层状态变更事件
    /// </summary>
    private void OnTransportStateChanged(object? sender, TransportStateEventArgs e)
    {
        StateChanged?.Invoke(this, e);

        // 连接断开时清理认证信息
        if (e.CurrentState == ConnectionState.Disconnected)
        {
            ClearAuthentication();
        }
    }

    /// <summary>
    /// 处理传输层数据接收事件
    /// </summary>
    private void OnTransportDataReceived(object? sender, TransportDataEventArgs e)
    {
        LastActiveTime = DateTime.UtcNow;

        if (sender is IServerTransport connection)
        {
            _logger?.LogDebug("[通道数据转发] {ConnectionId} 接收到传输数据: Size={Size} bytes, Data=[{DataHex}]",
                connection.ConnectionId, e.Data.Length, Convert.ToHexString(e.Data.Span[..Math.Min(e.Data.Length, 64)]));

            _logger?.LogDebug("[通道数据转发] {ConnectionId} 转发给订阅者，订阅者数量: {SubscriberCount}",
                connection.ConnectionId, DataReceived?.GetInvocationList()?.Length ?? 0);
        }
        else
        {
            _logger?.LogWarning("[通道数据转发] 发送者不是IServerConnection类型: {SenderType}", sender?.GetType().Name ?? "null");
        }

        DataReceived?.Invoke(this, e);

        _logger?.LogTrace("[通道数据转发] DataReceived事件已完成转发");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;

        // 取消订阅事件
        _transport.StateChanged -= OnTransportStateChanged;
        _transport.DataReceived -= OnTransportDataReceived;

        // 清理认证信息
        ClearAuthentication();

        // 清理属性
        _properties.Clear();

        // 释放传输资源
        _transport.Dispose();
    }
}
