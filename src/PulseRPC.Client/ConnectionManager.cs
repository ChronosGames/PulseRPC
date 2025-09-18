using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC.Client.Transport;
using PulseRPC.Transport;
using System.Collections.Concurrent;

namespace PulseRPC.Client;

/// <summary>
/// 连接管理器实现 - 管理所有连接的创建、维护和销毁
/// </summary>
public sealed class ConnectionManager : IConnectionManager
{
    private readonly ILogger<ConnectionManager> _logger;
    private readonly IServiceDiscovery? _serviceDiscovery;
    private readonly ConcurrentDictionary<string, ConnectionContext> _connections = new();
    private readonly SemaphoreSlim _connectionSemaphore = new(1, 1);
    private volatile bool _disposed;

    /// <summary>
    /// 连接数量
    /// </summary>
    public int Count => _connections.Count;

    /// <summary>
    /// 构造函数
    /// </summary>
    public ConnectionManager(
        IServiceDiscovery? serviceDiscovery = null,
        ILoggerFactory? loggerFactory = null)
    {
        _serviceDiscovery = serviceDiscovery;
        _logger = loggerFactory?.CreateLogger<ConnectionManager>() ?? NullLogger<ConnectionManager>.Instance;
    }

    /// <summary>
    /// 通过配置连接
    /// </summary>
    public async Task<IConnection> ConnectAsync(ConnectionConfig config, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var validation = config.Validate();
        if (!validation.IsValid)
        {
            throw new ArgumentException($"连接配置无效: {validation.GetErrorString()}", nameof(config));
        }

        var descriptor = config.ToDescriptor();
        return await ConnectAsync(descriptor, cancellationToken);
    }

    /// <summary>
    /// 通过描述符连接
    /// </summary>
    public async Task<IConnection> ConnectAsync(ConnectionDescriptor descriptor, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var validation = descriptor.Validate();
        if (!validation.IsValid)
        {
            throw new ArgumentException($"连接描述符无效: {validation.GetErrorString()}", nameof(descriptor));
        }

        // 检查是否已存在相同ID的连接
        if (_connections.ContainsKey(descriptor.Id))
        {
            throw new InvalidOperationException($"连接ID已存在: {descriptor.Id}");
        }

        await _connectionSemaphore.WaitAsync(cancellationToken);
        try
        {
            // 再次检查（双重检查锁定模式）
            if (_connections.ContainsKey(descriptor.Id))
            {
                throw new InvalidOperationException($"连接ID已存在: {descriptor.Id}");
            }

            _logger.LogInformation("开始创建连接: {ConnectionId} ({Name})", descriptor.Id, descriptor.Name);

            // 解析端点地址
            var endpoint = await ResolveEndpointAsync(descriptor, cancellationToken);
            if (endpoint == null)
            {
                throw new InvalidOperationException($"无法解析连接端点: {descriptor.Id}");
            }

            // 创建传输连接
            var transport = CreateTransport(descriptor);
            var connectionContext = new ConnectionContext(descriptor, endpoint, transport, _logger);

            // 注册连接
            if (!_connections.TryAdd(descriptor.Id, connectionContext))
            {
                connectionContext.Dispose();
                throw new InvalidOperationException($"注册连接失败: {descriptor.Id}");
            }

            try
            {
                // 执行连接
                await connectionContext.ConnectAsync(cancellationToken);
                _logger.LogInformation("连接创建成功: {ConnectionId} -> {Endpoint}", descriptor.Id, endpoint);
                return connectionContext;
            }
            catch
            {
                // 连接失败，清理资源
                _connections.TryRemove(descriptor.Id, out _);
                connectionContext.Dispose();
                throw;
            }
        }
        finally
        {
            _connectionSemaphore.Release();
        }
    }

    /// <summary>
    /// 断开连接
    /// </summary>
    public async Task DisconnectAsync(string connectionId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrEmpty(connectionId))
        {
            throw new ArgumentException("连接ID不能为空", nameof(connectionId));
        }

        if (_connections.TryRemove(connectionId, out var connection))
        {
            _logger.LogInformation("断开连接: {ConnectionId}", connectionId);

            try
            {
                await connection.DisconnectAsync(cancellationToken);
            }
            finally
            {
                connection.Dispose();
            }
        }
    }

    /// <summary>
    /// 批量断开连接
    /// </summary>
    public async Task DisconnectAsync(Func<IConnection, bool> predicate, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (predicate == null)
        {
            throw new ArgumentNullException(nameof(predicate));
        }

        var connectionsToDisconnect = _connections.Values
            .Where(predicate)
            .ToList();

        if (connectionsToDisconnect.Count == 0)
        {
            return;
        }

        _logger.LogInformation("批量断开连接: {Count} 个连接", connectionsToDisconnect.Count);

        var disconnectTasks = connectionsToDisconnect.Select(async connection =>
        {
            try
            {
                await DisconnectAsync(connection.Id, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "断开连接失败: {ConnectionId}", connection.Id);
            }
        });

        await Task.WhenAll(disconnectTasks);
    }

    /// <summary>
    /// 获取连接
    /// </summary>
    public IConnection? GetConnection(string connectionId)
    {
        ThrowIfDisposed();

        if (string.IsNullOrEmpty(connectionId))
        {
            return null;
        }

        return _connections.TryGetValue(connectionId, out var connection) ? connection : null;
    }

    public IReadOnlyList<IConnection> GetConnectionsByTag(string key, string? value = null)
    {
        ThrowIfDisposed();

        return _connections.Select(x => x.Value.Tags.TryGetValue(key, out var v) && (value == null || v == value) ? x.Value : null).Cast<IConnection>().ToArray();
    }

    /// <summary>
    /// 获取所有连接
    /// </summary>
    public IReadOnlyList<IConnection> GetAllConnections()
    {
        ThrowIfDisposed();
        return _connections.Values.ToList();
    }

    /// <summary>
    /// 解析端点地址
    /// </summary>
    private async Task<EndpointAddress?> ResolveEndpointAsync(ConnectionDescriptor descriptor, CancellationToken cancellationToken)
    {
        // 如果有直接端点，直接使用
        if (descriptor.Endpoint != null)
        {
            return descriptor.Endpoint;
        }

        // 如果有服务名称，尝试服务发现
        if (!string.IsNullOrEmpty(descriptor.ServiceName) && _serviceDiscovery != null)
        {
            try
            {
                var endpoints = await _serviceDiscovery.DiscoverAsync(descriptor.ServiceName, cancellationToken);
                var healthyEndpoints = endpoints.Where(e => e.IsHealthy && e.Transport == descriptor.Transport).ToList();

                if (healthyEndpoints.Count > 0)
                {
                    // 简单选择第一个健康的端点
                    var selectedEndpoint = healthyEndpoints.First();
                    _logger.LogInformation("通过服务发现解析端点: {ServiceName} -> {Endpoint}",
                        descriptor.ServiceName, selectedEndpoint.Address);
                    return selectedEndpoint.Address;
                }
                else
                {
                    _logger.LogWarning("服务发现未找到健康的端点: {ServiceName}", descriptor.ServiceName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "服务发现失败: {ServiceName}", descriptor.ServiceName);
            }
        }

        return null;
    }

    /// <summary>
    /// 创建传输连接
    /// </summary>
    private IClientTransport CreateTransport(ConnectionDescriptor descriptor)
    {
        var transportOptions = descriptor.TransportOptions;

        return descriptor.Transport switch
        {
            TransportType.Tcp => new TcpClientTransport(
                transportOptions as TcpTransportOptions,
                _logger),

            TransportType.Kcp => new KcpClientTransport(
                transportOptions as KcpTransportOptions,
                _logger),

            _ => throw new NotSupportedException($"不支持的传输类型: {descriptor.Transport}")
        };
    }

    /// <summary>
    /// 检查是否已释放
    /// </summary>
    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ConnectionManager));
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

        _logger.LogInformation("正在关闭连接管理器，当前连接数: {Count}", _connections.Count);

        // 断开所有连接
        var disconnectTasks = _connections.Values.Select(async connection =>
        {
            try
            {
                await connection.DisconnectAsync(CancellationToken.None);
                connection.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "释放连接时发生错误: {ConnectionId}", connection.Id);
            }
        });

        try
        {
            Task.WaitAll(disconnectTasks.ToArray(), TimeSpan.FromSeconds(10));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "等待连接关闭超时");
        }

        _connections.Clear();
        _connectionSemaphore.Dispose();
        _serviceDiscovery?.Dispose();

        _logger.LogInformation("连接管理器已关闭");
    }

    public Task<int> CleanupIdleConnectionsAsync(TimeSpan? maxAge = null, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}
