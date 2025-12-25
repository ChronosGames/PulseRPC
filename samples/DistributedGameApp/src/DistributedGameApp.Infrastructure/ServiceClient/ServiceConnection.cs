using DistributedGameApp.Infrastructure.Consul;
using Microsoft.Extensions.Logging;
using PulseRPC.Authentication;
using PulseRPC.Client;
using PulseRPC.Transport;
using System.Net.Sockets;

namespace DistributedGameApp.Infrastructure.ServiceClient;

/// <summary>
/// 通用服务连接封装 - 支持双向 RPC
/// </summary>
/// <remarks>
/// 统一的连接实现，适用于所有服务间通信（Backend, Battle, Game 等）
/// 利用 PulseRPC.Client 内置的连接状态管理和心跳机制
/// </remarks>
public class ServiceConnection : IAsyncDisposable
{
    private readonly ServiceRegistration _serviceInfo;
    private readonly ILogger<ServiceConnection> _logger;
    private readonly IAuthenticationProvider? _authenticationProvider;
    private readonly SemaphoreSlim _reconnectLock = new(1, 1);
    private IPulseClient? _client;
    private IClientChannel? _channel;
    private bool _isDisposed;

    /// <summary>
    /// 服务信息
    /// </summary>
    public ServiceRegistration ServiceInfo => _serviceInfo;

    /// <summary>
    /// 连接状态
    /// </summary>
    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;

    /// <summary>
    /// 连接建立时间
    /// </summary>
    public DateTime ConnectedAt { get; private set; } = DateTime.MinValue;

    private long _requestCount;

    /// <summary>
    /// 请求计数
    /// </summary>
    public long RequestCount => _requestCount;

    /// <summary>
    /// 连接状态变更事件
    /// </summary>
    public event EventHandler<ConnectionState>? StateChanged;

    public ServiceConnection(
        ServiceRegistration serviceInfo,
        ILogger<ServiceConnection> logger,
        IAuthenticationProvider? authenticationProvider = null)
    {
        _serviceInfo = serviceInfo ?? throw new ArgumentNullException(nameof(serviceInfo));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _authenticationProvider = authenticationProvider;
    }

    /// <summary>
    /// 建立连接
    /// </summary>
    public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(ServiceConnection));

        await _reconnectLock.WaitAsync(cancellationToken);
        try
        {
            if (State == ConnectionState.Connected)
            {
                return true;
            }

            _logger.LogInformation("连接到服务: {ServiceId} at {Host}:{Port}",
                _serviceInfo.ServiceId, _serviceInfo.Host, _serviceInfo.TcpPort);

            ChangeState(ConnectionState.Connecting);

            try
            {
                // 创建连接描述符
                var descriptor = ConnectionDescriptor.CreateTcp(
                    _serviceInfo.ServiceId,
                    "DistributedGameApp",
                    _serviceInfo.Host,
                    _serviceInfo.TcpPort,
                    ConnectionStrategy.Persistent);

                // 创建客户端
                var builder = new PulseClientBuilder()
                    .AddConnection(descriptor)
                    .WithTransportOptions(TransportType.TCP, new TcpTransportOptions
                    {
                        ConnectionTimeout = 10000,
                        NoDelay = true,
                        SendBufferSize = 8192,
                        RecvBufferSize = 8192,
                    });

                // 如果提供了认证提供者，配置认证
                if (_authenticationProvider != null)
                {
                    builder.WithAuthentication(_authenticationProvider);
                }

                _client = builder.Build();

                // 初始化客户端
                await _client.InitializeAsync(cancellationToken);

                // 获取连接
                _channel = _client.Connections.GetConnection(_serviceInfo.ServiceId);
                if (_channel == null)
                {
                    throw new InvalidOperationException($"无法获取连接: {_serviceInfo.ServiceId}");
                }

                ConnectedAt = DateTime.UtcNow;
                ChangeState(ConnectionState.Connected);

                _logger.LogInformation("成功连接到服务: {ServiceId}", _serviceInfo.ServiceId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "连接服务失败: {ServiceId}", _serviceInfo.ServiceId);
                ChangeState(ConnectionState.Disconnected);

                // 清理资源
                await CleanupAsync();
                return false;
            }
        }
        finally
        {
            _reconnectLock.Release();
        }
    }

    /// <summary>
    /// 判断是否为连接错误
    /// </summary>
    private static bool IsConnectionError(Exception ex)
    {
        return ex is SocketException ||
               ex is IOException ||
               ex is TimeoutException ||
               ex.InnerException is SocketException ||
               ex.InnerException is IOException;
    }

    /// <summary>
    /// 清理资源
    /// </summary>
    private Task CleanupAsync()
    {
        // 取消事件订阅
        if (_channel != null)
        {
            _channel = null;
        }

        if (_client != null)
        {
            _client.Dispose();
            _client = null;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 改变连接状态
    /// </summary>
    private void ChangeState(ConnectionState newState)
    {
        if (State != newState)
        {
            var oldState = State;
            State = newState;

            _logger.LogInformation("连接状态变更: {ServiceId} {OldState} -> {NewState}",
                _serviceInfo.ServiceId, oldState, newState);

            StateChanged?.Invoke(this, newState);
        }
    }

    /// <summary>
    /// 获取 Channel 实例
    /// </summary>
    public IClientChannel? Channel => _channel;

    /// <summary>
    /// 断开连接
    /// </summary>
    public async Task DisconnectAsync()
    {
        await _reconnectLock.WaitAsync();
        try
        {
            if (State == ConnectionState.Disconnected)
            {
                return;
            }

            _logger.LogInformation("断开服务连接: {ServiceId}", _serviceInfo.ServiceId);

            ChangeState(ConnectionState.Disconnected);
            await CleanupAsync();
        }
        finally
        {
            _reconnectLock.Release();
        }
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;

        await DisconnectAsync();
        await CleanupAsync();

        _reconnectLock.Dispose();
    }
}

/// <summary>
/// 连接状态
/// </summary>
public enum ConnectionState
{
    /// <summary>
    /// 已断开
    /// </summary>
    Disconnected,

    /// <summary>
    /// 连接中
    /// </summary>
    Connecting,

    /// <summary>
    /// 已连接
    /// </summary>
    Connected
}
