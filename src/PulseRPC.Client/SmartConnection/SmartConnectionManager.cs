using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PulseRPC.Authentication;
using PulseRPC.Messaging;
using PulseRPC.SmartConnection;
using PulseRPC.ServiceDiscovery;
using PulseRPC.Transport;

namespace PulseRPC.Client.SmartConnection;

/// <summary>
/// 智能连接管理器 - 核心实现
/// </summary>
public class SmartConnectionManager : IDisposable
{
    private readonly ConcurrentDictionary<string, ServiceConnectionInfo> _connections = new();
    private readonly IServiceDiscovery _serviceDiscovery;
    private readonly IAuthenticationProvider? _authProvider;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<SmartConnectionManager> _logger;
    private readonly Timer _cleanupTimer;
    private readonly SemaphoreSlim _connectionSemaphore = new(1, 1);
    private readonly SmartConnectionOptions _defaultOptions;
    private bool _disposed;

    public SmartConnectionManager(
        IServiceDiscovery serviceDiscovery,
        SmartConnectionOptions? defaultOptions = null,
        IAuthenticationProvider? authProvider = null,
        ILoggerFactory? loggerFactory = null)
    {
        _serviceDiscovery = serviceDiscovery ?? throw new ArgumentNullException(nameof(serviceDiscovery));
        _authProvider = authProvider;
        _loggerFactory = loggerFactory ?? Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;
        _logger = _loggerFactory.CreateLogger<SmartConnectionManager>();
        _defaultOptions = defaultOptions ?? new SmartConnectionOptions();

        // 每分钟清理一次空闲连接
        _cleanupTimer = new Timer(CleanupIdleConnections, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// 获取或创建服务连接
    /// </summary>
    public async Task<ServiceConnectionInfo> GetOrCreateConnectionAsync<T>(
        string serviceName,
        SmartConnectionOptions? options = null,
        CancellationToken cancellationToken = default) where T : class, IPulseService
    {
        options ??= _defaultOptions;
        var connectionKey = $"{serviceName}:{typeof(T).Name}";

        // 检查现有连接
        if (_connections.TryGetValue(connectionKey, out var existingConnection) &&
            existingConnection.IsConnected)
        {
            existingConnection.UpdateLastUsed();
            return existingConnection;
        }

        await _connectionSemaphore.WaitAsync(cancellationToken);
        try
        {
            _logger.LogInformation("创建新的服务连接: {ServiceName} for {ServiceType}", serviceName, typeof(T).Name);

            // 服务发现
            var endpoint = await DiscoverServiceEndpointAsync(serviceName, options, cancellationToken);

            // 创建连接
            var connection = await CreateConnectionAsync(connectionKey, endpoint, options, cancellationToken);

            // 缓存连接
            _connections.AddOrUpdate(connectionKey, connection, (_, old) =>
            {
                old?.Dispose();
                return connection;
            });

            return connection;
        }
        finally
        {
            _connectionSemaphore.Release();
        }
    }

    /// <summary>
    /// 服务发现
    /// </summary>
    private async Task<ServiceEndpoint> DiscoverServiceEndpointAsync(
        string serviceName,
        SmartConnectionOptions options,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(serviceName))
        {
            return new ServiceEndpoint
            {
                Host = "localhost",
                Port = 8000,
                Transport = options.PreferredTransport
            };
        }

        // 调用服务发现获取端点
        var discoveryEndpoints = await _serviceDiscovery.GetServicesAsync(serviceName, cancellationToken);
        var discoveredEndpoint = discoveryEndpoints.FirstOrDefault();

        if (discoveredEndpoint != null)
        {
            return new ServiceEndpoint
            {
                Host = discoveredEndpoint.Host,
                Port = discoveredEndpoint.Port,
                Transport = Enum.TryParse<TransportType>(discoveredEndpoint.Protocol, true, out var t)
                    ? t : options.PreferredTransport,
                Metadata = discoveredEndpoint.Metadata
            };
        }

        // 回退到默认端点
        return new ServiceEndpoint
        {
            Host = "localhost",
            Port = 8000,
            Transport = options.PreferredTransport
        };
    }

    /// <summary>
    /// 创建物理连接
    /// </summary>
    private async Task<ServiceConnectionInfo> CreateConnectionAsync(
        string connectionKey,
        ServiceEndpoint endpoint,
        SmartConnectionOptions options,
        CancellationToken cancellationToken)
    {
        var channelManager = new ChannelManager(_loggerFactory);
        var channelName = $"smart-{connectionKey}-{Guid.NewGuid():N}";

        // 注册通道
        channelManager.RegisterChannel(channelName, endpoint.Transport, new TransportOptions(), isDefault: true);

        // 连接
        var channel = channelManager.GetChannel(channelName);
        await channel.ConnectAsync(endpoint.Host, endpoint.Port, cancellationToken);

        return new ServiceConnectionInfo
        {
            ConnectionKey = connectionKey,
            ChannelManager = channelManager,
            Channel = channel,
            Endpoint = endpoint,
            Options = options,
            CreatedAt = DateTime.UtcNow,
            LastUsedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// 清理空闲连接
    /// </summary>
    private void CleanupIdleConnections(object? state)
    {
        if (_disposed) return;

        var now = DateTime.UtcNow;
        var connectionsToRemove = new List<string>();

        foreach (var kvp in _connections)
        {
            var connection = kvp.Value;
            var idleTime = now - connection.LastUsedAt;

            if (idleTime > connection.Options.IdleRecycleTime &&
                connection.ActiveReferences == 0)
            {
                connectionsToRemove.Add(kvp.Key);
                _logger.LogInformation("回收空闲连接: {ConnectionKey}, 空闲时间: {IdleTime}",
                    kvp.Key, idleTime);
            }
        }

        foreach (var key in connectionsToRemove)
        {
            if (_connections.TryRemove(key, out var connection))
            {
                connection.Dispose();
            }
        }
    }

    /// <summary>
    /// 获取连接统计信息
    /// </summary>
    public ConnectionStatistics GetConnectionStatistics()
    {
        return new ConnectionStatistics
        {
            TotalConnections = _connections.Count,
            ActiveConnections = _connections.Values.Count(c => c.ActiveReferences > 0),
            IdleConnections = _connections.Values.Count(c => c.ActiveReferences == 0),
            FailedConnections = 0, // 简化实现 - 可以后续追踪失败连接
            ServiceStatistics = new Dictionary<string, ServiceConnectionStatistics>(),
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// 清理空闲连接
    /// </summary>
    public async Task<int> CleanupIdleConnectionsAsync(TimeSpan? maxAge = null)
    {
        var cutoffTime = DateTime.UtcNow.Subtract(maxAge ?? TimeSpan.FromMinutes(30));
        var cleanedCount = 0;

        var connectionsToRemove = new List<string>();

        foreach (var kvp in _connections)
        {
            var connection = kvp.Value;
            if (connection.ActiveReferences == 0 && connection.LastUsedAt < cutoffTime)
            {
                connectionsToRemove.Add(kvp.Key);
            }
        }

        foreach (var key in connectionsToRemove)
        {
            if (_connections.TryRemove(key, out var connection))
            {
                connection.Dispose();
                cleanedCount++;
                _logger.LogInformation("清理空闲连接: {ConnectionKey}", key);
            }
        }

        return cleanedCount;
    }

    /// <summary>
    /// 获取或创建通用连接（用于事件监听器等非特定服务类型的场景）
    /// </summary>
    public async Task<ServiceConnectionInfo> GetOrCreateConnectionAsync(
        string serviceName,
        SmartConnectionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // 重用泛型版本，使用object作为占位符类型
        return await GetOrCreateConnectionAsync<IPulseService>(serviceName, options, cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cleanupTimer?.Dispose();
        _connectionSemaphore?.Dispose();

        foreach (var connection in _connections.Values)
        {
            connection.Dispose();
        }
        _connections.Clear();
    }
}

/// <summary>
/// 服务连接信息
/// </summary>
public class ServiceConnectionInfo : IDisposable
{
    public string ConnectionKey { get; set; } = "";
    public IChannelManager ChannelManager { get; set; } = null!;
    public IClientChannel Channel { get; set; } = null!;
    public ServiceEndpoint Endpoint { get; set; } = null!;
    public SmartConnectionOptions Options { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime LastUsedAt { get; set; }
    public AuthenticationToken? AuthToken { get; set; }

    private int _activeReferences = 0;
    public int ActiveReferences => _activeReferences;

    public bool IsConnected => Channel.IsConnected;

    public void UpdateLastUsed()
    {
        LastUsedAt = DateTime.UtcNow;
    }

    public void AddReference()
    {
        Interlocked.Increment(ref _activeReferences);
    }

    public void RemoveReference()
    {
        Interlocked.Decrement(ref _activeReferences);
    }

    public void Dispose()
    {
        Channel?.Dispose();
        ChannelManager?.Dispose();
    }
}
