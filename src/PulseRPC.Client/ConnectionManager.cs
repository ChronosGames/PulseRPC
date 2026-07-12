using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC.Client.Transport;
using PulseRPC.Client.Health;
using PulseRPC.Shared;
using System.Collections.Concurrent;
using PulseRPC.Client.Channels;
using PulseRPC.Client.Configuration;
using PulseRPC.Messaging;
using PulseRPC.Serialization;

namespace PulseRPC.Client;

/// <summary>
/// 连接管理器实现 - 统一管理连接的创建、销毁、路由和生命周期
/// </summary>
public sealed class ConnectionManager : IContextualConnectionManager
{
    private readonly ILogger<ConnectionManager> _logger;
    private readonly ISerializerProvider _serializerProvider;
    private readonly ILoadBalancer _loadBalancer;
    private readonly ConcurrentDictionary<string, IClientChannel> _connections = new();
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
        ISerializerProvider? serializerProvider = null,
        ILoggerFactory? loggerFactory = null,
        ILoadBalancer? loadBalancer = null)
    {
        _serializerProvider = serializerProvider ?? PulseRPCSerializerProvider.Instance;
        _logger = loggerFactory?.CreateLogger<ConnectionManager>() ?? NullLogger<ConnectionManager>.Instance;
        _loadBalancer = loadBalancer ?? new ConnectionLoadBalancer(LoadBalancingStrategy.RoundRobin, loggerFactory?.CreateLogger<ConnectionLoadBalancer>() ?? NullLogger<ConnectionLoadBalancer>.Instance);
    }

    /// <summary>
    /// 通过配置连接
    /// </summary>
    public async Task<IClientChannel> ConnectAsync(ConnectionConfig config, CancellationToken cancellationToken = default)
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
    public async Task<IClientChannel> ConnectAsync(ConnectionDescriptor descriptor, CancellationToken cancellationToken = default)
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
            var connectionContext = new TransportChannel(transport, _serializerProvider);

            // 注册连接
            if (!_connections.TryAdd(descriptor.Id, connectionContext))
            {
                connectionContext.Dispose();
                throw new InvalidOperationException($"注册连接失败: {descriptor.Id}");
            }

            try
            {
                // 执行连接
                await connectionContext.ConnectAsync(endpoint.Host, endpoint.Port, cancellationToken);
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
        => await DisconnectAsync(connectionId, graceful: true, cancellationToken).ConfigureAwait(false);

    internal async Task DisconnectAsync(
        string connectionId,
        bool graceful,
        CancellationToken cancellationToken = default)
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
                if (graceful)
                {
                    await connection.DisconnectAsync(cancellationToken).ConfigureAwait(false);
                }
                else if (connection is TransportChannel transportChannel)
                {
                    transportChannel.Abort();
                    return;
                }
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
    public async Task DisconnectAsync(Func<IClientChannel, bool> predicate, CancellationToken cancellationToken = default)
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
    public IClientChannel? GetConnection(string connectionId)
    {
        ThrowIfDisposed();

        if (string.IsNullOrEmpty(connectionId))
        {
            return null;
        }

        return _connections.TryGetValue(connectionId, out var connection) ? connection : null;
    }

    public IReadOnlyList<IClientChannel> GetConnectionsByTag(string key, string? value = null)
    {
        ThrowIfDisposed();

        return _connections.Values.Where(x => x.Tags.TryGetValue(key, out var v) && (value == null || v == value)).ToList();
    }

    /// <summary>
    /// 获取所有连接
    /// </summary>
    public IReadOnlyList<IClientChannel> GetAllConnections()
    {
        ThrowIfDisposed();
        return _connections.Values.ToList();
    }

    /// <summary>
    /// 解析端点地址
    /// </summary>
    private Task<EndpointAddress?> ResolveEndpointAsync(ConnectionDescriptor descriptor, CancellationToken cancellationToken)
    {
        // 直接使用端点地址
        return Task.FromResult(descriptor.Endpoint);
    }

    /// <summary>
    /// 创建传输连接
    /// </summary>
    private IClientTransport CreateTransport(ConnectionDescriptor descriptor)
    {
        var transportOptions = descriptor.TransportOptions;

        return descriptor.Transport switch
        {
            TransportType.TCP => new TcpClientTransport(
                descriptor.Id,
                transportOptions as TcpTransportOptions,
                _logger),

            TransportType.KCP => new KcpClientTransport(
                descriptor.Id,
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

        _logger.LogInformation("连接管理器已关闭");
    }

    #region 路由功能

    /// <summary>
    /// 根据服务名称路由到最佳连接
    /// </summary>
    public Task<IClientChannel?> RouteAsync(string serviceName, CancellationToken cancellationToken = default)
        => RouteAsync(serviceName, new LoadBalancingContext(), cancellationToken);

    /// <summary>
    /// 根据服务名称和稳定路由上下文选择最佳连接
    /// </summary>
    public Task<IClientChannel?> RouteAsync(
        string serviceName,
        LoadBalancingContext context,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrEmpty(serviceName))
        {
            throw new ArgumentException("服务名称不能为空", nameof(serviceName));
        }

        // 1. 获取服务相关的连接（按服务名或标签匹配）
        var candidates = GetServiceConnections(serviceName);

        if (candidates.Count == 0)
        {
            // 如果没有匹配的服务连接，返回任意健康连接（降级策略）
            candidates = _connections.Values
                .Where(c => c.State == ExtendedConnectionState.Connected)
                .ToList();

            if (candidates.Count > 0)
            {
                _logger.LogDebug("未找到服务 {ServiceName} 的专用连接，使用降级策略返回可用连接", serviceName);
            }
        }

        if (candidates.Count == 0)
        {
            _logger.LogWarning("未找到服务 {ServiceName} 的可用连接", serviceName);
            return Task.FromResult<IClientChannel?>(null);
        }

        // 2. 使用负载均衡器选择最佳连接
        var selected = _loadBalancer is IContextualLoadBalancer contextualLoadBalancer
            ? contextualLoadBalancer.SelectConnection(candidates, context)
            : SelectWithoutContext(candidates, context);
        return Task.FromResult(selected);
    }

    private IClientChannel? SelectWithoutContext(
        IReadOnlyList<IClientChannel> candidates,
        LoadBalancingContext context)
    {
        if (context.Hint != LoadBalancingHint.None || !string.IsNullOrWhiteSpace(context.StickyKey))
        {
            throw new NotSupportedException(
                $"Load balancer '{_loadBalancer.GetType().FullName}' does not support contextual load balancing.");
        }

        return _loadBalancer.SelectConnection(candidates, LoadBalancingHint.None);
    }

    /// <summary>
    /// 获取指定服务的所有可用连接
    /// </summary>
    public IReadOnlyList<IClientChannel> GetServiceConnections(string serviceName)
    {
        ThrowIfDisposed();

        if (string.IsNullOrEmpty(serviceName))
        {
            return Array.Empty<IClientChannel>();
        }

        // 按服务名标签匹配，或者按连接名称前缀匹配
        return _connections.Values
            .Where(c => c.State == ExtendedConnectionState.Connected &&
                       (c.Tags.TryGetValue("service", out var svc) && string.Equals(svc, serviceName, StringComparison.OrdinalIgnoreCase) ||
                        c.Tags.TryGetValue("serviceName", out var sn) && string.Equals(sn, serviceName, StringComparison.OrdinalIgnoreCase) ||
                        c.Descriptor?.Name?.Contains(serviceName, StringComparison.OrdinalIgnoreCase) == true))
            .ToList();
    }

    #endregion

    #region 生命周期管理

    /// <summary>
    /// 执行健康检查
    /// </summary>
    public Task<IReadOnlyList<HealthCheckResult>> PerformHealthChecksAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var results = new List<HealthCheckResult>();
        var connections = GetAllConnections();

        foreach (var conn in connections)
        {
            var health = conn.State switch
            {
                ExtendedConnectionState.Connected => HealthStatus.Healthy,
                ExtendedConnectionState.Connecting => HealthStatus.Degraded,
                ExtendedConnectionState.Reconnecting => HealthStatus.Degraded,
                ExtendedConnectionState.Disconnecting => HealthStatus.Degraded,
                _ => HealthStatus.Unhealthy
            };

            results.Add(new HealthCheckResult
            {
                ConnectionId = conn.Id,
                Health = health,
                CheckedAt = DateTime.UtcNow,
                Message = $"Connection state: {conn.State}"
            });
        }

        _logger.LogDebug("健康检查完成: {Total} 个连接, {Healthy} 个健康",
            results.Count, results.Count(r => r.Health == HealthStatus.Healthy));

        return Task.FromResult<IReadOnlyList<HealthCheckResult>>(results);
    }

    /// <summary>
    /// 清理空闲连接
    /// </summary>
    public async Task<int> CleanupIdleConnectionsAsync(TimeSpan? maxAge = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var timeout = maxAge ?? TimeSpan.FromMinutes(30);
        var now = DateTime.UtcNow;
        var cleaned = 0;

        var idleConnections = GetAllConnections()
            .Where(c => c.Statistics != null && c.Statistics.LastActiveAt < now - timeout)
            .ToList();

        if (idleConnections.Count == 0)
        {
            return 0;
        }

        _logger.LogInformation("开始清理 {Count} 个空闲连接（超时: {Timeout}）", idleConnections.Count, timeout);

        foreach (var conn in idleConnections)
        {
            try
            {
                await DisconnectAsync(conn.Id, cancellationToken);
                cleaned++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "清理空闲连接失败: {ConnectionId}", conn.Id);
            }
        }

        _logger.LogInformation("清理完成: {Cleaned} 个连接已断开", cleaned);
        return cleaned;
    }

    #endregion
}
