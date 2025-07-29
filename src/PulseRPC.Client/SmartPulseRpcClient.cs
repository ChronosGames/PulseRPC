using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PulseRPC.Authentication;
using PulseRPC.Client.SmartConnection;
using PulseRPC.Routing;
using PulseRPC.SmartConnection;
using PulseRPC.Transport;
using PulseRPC.ServiceDiscovery;

namespace PulseRPC.Client;

/// <summary>
/// 智能 PulseRPC 客户端实现
/// </summary>
public class SmartPulseRpcClient : ISmartPulseRpcClient
{
    private readonly SmartConnectionManager _connectionManager;
    private readonly MultiInstanceConnectionManager _multiInstanceManager;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<SmartPulseRpcClient> _logger;
    private ServiceDiscoveryConfiguration _serviceDiscoveryConfig = new();
    private IAuthenticationProvider? _authProvider;
    private bool _disposed;

    public SmartPulseRpcClient(ILoggerFactory? loggerFactory = null)
    {
        _loggerFactory = loggerFactory ?? Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;
        _logger = _loggerFactory.CreateLogger<SmartPulseRpcClient>();
        
        // 创建简化的服务发现实现
        var serviceDiscovery = new StaticServiceDiscovery(_serviceDiscoveryConfig);
        
        // 创建连接管理器
        _connectionManager = new SmartConnectionManager(serviceDiscovery, null, _authProvider, _loggerFactory);
        _multiInstanceManager = new MultiInstanceConnectionManager(_connectionManager, _loggerFactory);
    }

    public async Task<T> GetServiceAsync<T>(string serviceName = "", SmartConnectionOptions? options = null) 
        where T : class, IPulseService
    {
        options ??= new SmartConnectionOptions();
        
        // 如果服务名为空，使用接口名作为服务名
        if (string.IsNullOrEmpty(serviceName))
        {
            serviceName = typeof(T).Name.TrimStart('I');
        }

        var connection = await _connectionManager.GetOrCreateConnectionAsync<T>(serviceName, options);
        
        // 获取原始服务代理
        var innerProxy = connection.ChannelManager.GetService<T>();
        
        // 这里应该包装为智能代理，但为了简化实现，直接返回
        return innerProxy;
    }

    public async Task<T> GetServiceAsync<T>(string serviceName, string instanceId, SmartConnectionOptions? options = null) 
        where T : class, IPulseService
    {
        return await _multiInstanceManager.GetServiceAsync<T>(serviceName, instanceId, options);
    }

    public async Task<T> GetServiceAsync<T>(string serviceName, IRoutingContext routingContext, SmartConnectionOptions? options = null) 
        where T : class, IPulseService
    {
        return await _multiInstanceManager.GetServiceAsync<T>(serviceName, routingContext, options);
    }

    public async Task<IMultiInstanceServiceManager<T>> GetMultiInstanceServiceAsync<T>(string serviceName, SmartConnectionOptions? options = null) 
        where T : class, IPulseService
    {
        options ??= new SmartConnectionOptions();
        return new MultiInstanceServiceManager<T>(serviceName, _multiInstanceManager, options, _loggerFactory);
    }

    public async Task<ISubscriptionToken> RegisterEventListenerAsync<T>(T listener, string serviceName = "", 
        SmartConnectionOptions? options = null) where T : class, IPulseEventHandler
    {
        options ??= new SmartConnectionOptions();
        
        if (string.IsNullOrEmpty(serviceName))
        {
            serviceName = typeof(T).Name.TrimStart('I').Replace("Events", "").Replace("Listener", "");
        }

        // 对于事件监听器，使用非泛型的连接方法
        var connection = await _connectionManager.GetOrCreateConnectionAsync(serviceName, options);
        
        // 注册事件监听器
        var token = connection.ChannelManager.RegisterEventListener(listener);
        
        // 增加连接引用
        connection.AddReference();
        
        // 包装订阅令牌以便在取消订阅时减少引用
        return new SmartSubscriptionToken(token, connection);
    }

    public async Task<ISubscriptionToken> RegisterEventListenerAsync<T>(T listener, string serviceName, IRoutingContext routingContext,
        SmartConnectionOptions? options = null) where T : class, IPulseEventHandler
    {
        // 根据路由上下文选择特定实例进行事件监听
        var service = await GetServiceAsync<IPulseService>(serviceName, routingContext, options);
        
        // 简化实现 - 实际应该通过路由上下文获取对应的连接
        return await RegisterEventListenerAsync(listener, serviceName, options);
    }

    public void ConfigureServiceDiscovery(Action<ServiceDiscoveryConfiguration> configure)
    {
        configure(_serviceDiscoveryConfig);
        // TODO: 实现热重载逻辑
        _logger.LogInformation("服务发现配置已更新");
    }

    public void ConfigureAuthentication(IAuthenticationProvider authProvider)
    {
        _authProvider = authProvider;
        _logger.LogInformation("认证提供者已配置: {AuthType}", authProvider.AuthenticationType);
    }

    public void ConfigureServiceRouting<T>(Action<ServiceRoutingConfiguration<T>> configure) where T : class, IPulseService
    {
        _multiInstanceManager.ConfigureServiceRouting(configure);
        _logger.LogInformation("服务路由策略已配置: {ServiceType}", typeof(T).Name);
    }

    public Task<ConnectionStatistics> GetConnectionStatisticsAsync()
    {
        return Task.FromResult(_connectionManager.GetConnectionStatistics());
    }

    public async Task<int> CleanupIdleConnectionsAsync(TimeSpan? maxAge = null)
    {
        return await _connectionManager.CleanupIdleConnectionsAsync(maxAge);
    }

    // 实现其他 IPulseRpcClient 方法
    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        // 智能客户端不需要预连接
        _logger.LogInformation("智能客户端采用按需连接模式，无需预连接");
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        // 断开所有管理的连接
        _connectionManager?.Dispose();
        _multiInstanceManager?.Dispose();
        _logger.LogInformation("智能客户端已断开所有连接");
        return Task.CompletedTask;
    }

    public bool IsConnected => true; // 按需连接，总是"可用"

    public IChannelManager GetChannelManager()
    {
        throw new NotSupportedException("智能客户端不支持直接访问通道管理器，请使用 GetServiceAsync 方法");
    }

    public IReadOnlyDictionary<string, (TransportType Type, string Host, int Port, bool IsDefault)> GetTransports()
    {
        // 返回当前活动的连接信息
        var stats = _connectionManager.GetConnectionStatistics();
        var transports = new Dictionary<string, (TransportType, string, int, bool)>();
        
        foreach (var serviceStat in stats.ServiceStatistics)
        {
            transports[serviceStat.Key] = (TransportType.Tcp, "dynamic", 0, false);
        }
        
        return transports;
    }

    public event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;
    public event EventHandler<ServiceDiscoveryEventArgs>? ServiceDiscoveryChanged;

    protected virtual void OnConnectionStateChanged(ConnectionStateChangedEventArgs e)
    {
        ConnectionStateChanged?.Invoke(this, e);
    }

    protected virtual void OnServiceDiscoveryChanged(ServiceDiscoveryEventArgs e)
    {
        ServiceDiscoveryChanged?.Invoke(this, e);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _connectionManager?.Dispose();
        _multiInstanceManager?.Dispose();
        
        ConnectionStateChanged = null;
        ServiceDiscoveryChanged = null;
        
        _logger.LogInformation("智能PulseRPC客户端已释放");
    }
}

/// <summary>
/// 智能订阅令牌 - 包装原始令牌并管理连接引用
/// </summary>
public class SmartSubscriptionToken : ISubscriptionToken
{
    private readonly ISubscriptionToken _innerToken;
    private readonly ServiceConnectionInfo _connection;
    private bool _disposed;

    public SmartSubscriptionToken(ISubscriptionToken innerToken, ServiceConnectionInfo connection)
    {
        _innerToken = innerToken;
        _connection = connection;
    }

    /// <summary>
    /// 订阅令牌ID
    /// </summary>
    public Guid Id => _innerToken?.Id ?? Guid.NewGuid();

    /// <summary>
    /// 是否处于活跃状态
    /// </summary>
    public bool IsActive => !_disposed && (_innerToken?.IsActive ?? false);

    /// <summary>
    /// 是否已取消订阅
    /// </summary>
    public bool IsUnsubscribed => _disposed || (_innerToken?.IsUnsubscribed ?? true);

    /// <summary>
    /// 取消订阅
    /// </summary>
    public void Unsubscribe()
    {
        _innerToken?.Unsubscribe();
        Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _innerToken?.Dispose();
        _connection?.RemoveReference();
    }
}

/// <summary>
/// 简化的静态服务发现实现
/// </summary>
internal class StaticServiceDiscovery : IServiceDiscovery
{
    private readonly ServiceDiscoveryConfiguration _config;

    public StaticServiceDiscovery(ServiceDiscoveryConfiguration config)
    {
        _config = config;
    }

    public async Task<IReadOnlyList<ServiceDiscovery.ServiceEndpoint>> GetServicesAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        // 简化实现 - 返回静态配置或默认端点
        if (_config.StaticEndpoints.TryGetValue(serviceName, out var endpoint))
        {
            return new List<ServiceDiscovery.ServiceEndpoint>
            {
                new ServiceDiscovery.ServiceEndpoint
                {
                    ServiceId = $"{serviceName}-1",
                    ServiceType = serviceName,
                    Host = endpoint.Host,
                    Port = endpoint.Port,
                    Protocol = endpoint.Transport.ToString(),
                    Metadata = new Dictionary<string, string>()
                }
            };
        }

        // 返回默认端点
        return new List<ServiceDiscovery.ServiceEndpoint>
        {
            new ServiceDiscovery.ServiceEndpoint
            {
                ServiceId = $"{serviceName}-default",
                ServiceType = serviceName,
                Host = "localhost",
                Port = 8000,
                Protocol = "Tcp",
                Metadata = new Dictionary<string, string>()
            }
        };
    }

    public Task<ServiceDiscovery.ServiceEndpoint?> GetServiceAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        // 简化实现
        return Task.FromResult<ServiceDiscovery.ServiceEndpoint?>(null);
    }

    public Task<bool> ExistsAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        // 检查静态配置中是否存在该服务
        var exists = _config.StaticEndpoints.ContainsKey(serviceName);
        return Task.FromResult(exists);
    }
}

// IServiceDiscovery 接口现在从 PulseRPC.Infrastructure 项目引用 