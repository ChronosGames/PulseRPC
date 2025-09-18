using Microsoft.Extensions.Logging;
using PulseRPC.Authentication;
using PulseRPC.Serialization;
using PulseRPC.Transport;

namespace PulseRPC.Client.Core;

/// <summary>
/// PulseRPC 客户端构建器
/// </summary>
public sealed class PulseClientBuilder : IPulseClientBuilder
{
    private readonly List<ConnectionDescriptor> _connections = new();
    private IServiceDiscovery? _serviceDiscovery;
    private ILoggerFactory? _loggerFactory;
    private ISerializerProvider? _serializerProvider;
    private IAuthenticationProvider? _authenticationProvider;
    private LoadBalancingStrategy _loadBalancingStrategy = LoadBalancingStrategy.RoundRobin;
    private readonly Dictionary<string, object> _loadBalancingOptions = new();
    private ConnectionPoolOptions? _connectionPoolOptions;
    private RetryPolicy? _retryPolicy;
    private readonly Dictionary<TransportType, TransportOptions> _transportOptions = new();
    private readonly ClientOptions _clientOptions = new();

    /// <summary>
    /// 添加连接配置
    /// </summary>
    public IPulseClientBuilder AddConnection(ConnectionDescriptor descriptor)
    {
        if (descriptor == null)
            throw new ArgumentNullException(nameof(descriptor));

        var validation = descriptor.Validate();
        if (!validation.IsValid)
            throw new ArgumentException($"连接描述符无效: {validation.GetErrorString()}", nameof(descriptor));

        _connections.Add(descriptor);
        return this;
    }

    /// <summary>
    /// 配置服务发现
    /// </summary>
    public IPulseClientBuilder WithServiceDiscovery(IServiceDiscovery serviceDiscovery)
    {
        _serviceDiscovery = serviceDiscovery ?? throw new ArgumentNullException(nameof(serviceDiscovery));
        return this;
    }

    /// <summary>
    /// 配置负载均衡策略
    /// </summary>
    public IPulseClientBuilder WithLoadBalancing(LoadBalancingStrategy strategy, IReadOnlyDictionary<string, object>? options = null)
    {
        _loadBalancingStrategy = strategy;

        if (options != null)
        {
            foreach (var kvp in options)
            {
                _loadBalancingOptions[kvp.Key] = kvp.Value;
            }
        }

        return this;
    }

    /// <summary>
    /// 配置连接池
    /// </summary>
    public IPulseClientBuilder WithConnectionPooling(ConnectionPoolOptions poolOptions)
    {
        _connectionPoolOptions = poolOptions ?? throw new ArgumentNullException(nameof(poolOptions));
        return this;
    }

    /// <summary>
    /// 配置重试策略
    /// </summary>
    public IPulseClientBuilder WithRetryPolicy(RetryPolicy retryPolicy)
    {
        _retryPolicy = retryPolicy ?? throw new ArgumentNullException(nameof(retryPolicy));
        return this;
    }

    /// <summary>
    /// 配置日志
    /// </summary>
    public IPulseClientBuilder WithLogging(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        return this;
    }

    /// <summary>
    /// 配置序列化器
    /// </summary>
    public IPulseClientBuilder WithSerializer(ISerializerProvider serializerProvider)
    {
        _serializerProvider = serializerProvider ?? throw new ArgumentNullException(nameof(serializerProvider));
        return this;
    }

    /// <summary>
    /// 配置认证提供者
    /// </summary>
    public IPulseClientBuilder WithAuthentication(IAuthenticationProvider authenticationProvider)
    {
        _authenticationProvider = authenticationProvider ?? throw new ArgumentNullException(nameof(authenticationProvider));
        return this;
    }

    /// <summary>
    /// 配置传输选项
    /// </summary>
    public IPulseClientBuilder WithTransportOptions(TransportType transportType, TransportOptions options)
    {
        _transportOptions[transportType] = options ?? throw new ArgumentNullException(nameof(options));
        return this;
    }

    /// <summary>
    /// 配置客户端选项
    /// </summary>
    public IPulseClientBuilder Configure(Action<ClientOptions> configure)
    {
        configure(_clientOptions);
        return this;
    }

    /// <summary>
    /// 构建客户端
    /// </summary>
    public IPulseClient Build()
    {
        // 验证配置
        ValidateConfiguration();

        // 应用传输选项到连接描述符
        ApplyTransportOptionsToConnections();

        return new PulseClient(
            connections: _connections,
            serviceDiscovery: _serviceDiscovery,
            loggerFactory: _loggerFactory,
            serializerProvider: _serializerProvider,
            authenticationProvider: _authenticationProvider,
            loadBalancingStrategy: _loadBalancingStrategy,
            loadBalancingOptions: _loadBalancingOptions,
            connectionPoolOptions: _connectionPoolOptions,
            retryPolicy: _retryPolicy,
            clientOptions: _clientOptions);
    }

    /// <summary>
    /// 验证构建器配置
    /// </summary>
    private void ValidateConfiguration()
    {
        if (_connections.Count == 0 && _serviceDiscovery == null)
        {
            throw new InvalidOperationException("必须至少添加一个连接或配置服务发现");
        }

        // 验证所有连接描述符
        foreach (var connection in _connections)
        {
            var validation = connection.Validate();
            if (!validation.IsValid)
            {
                throw new InvalidOperationException($"连接 {connection.Id} 配置无效: {validation.GetErrorString()}");
            }
        }

        // 验证客户端选项
        if (_clientOptions.DefaultTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("默认超时时间必须大于 0");
        }

        if (_clientOptions.MaxConcurrentConnections <= 0)
        {
            throw new InvalidOperationException("最大并发连接数必须大于 0");
        }
    }

    /// <summary>
    /// 将传输选项应用到连接描述符
    /// </summary>
    private void ApplyTransportOptionsToConnections()
    {
        foreach (var connection in _connections)
        {
            if (connection.TransportOptions == null && _transportOptions.TryGetValue(connection.Transport, out var options))
            {
                connection.TransportOptions = options;
            }
        }
    }

    #region 便捷方法

    /// <summary>
    /// 添加 TCP 连接
    /// </summary>
    public PulseClientBuilder AddTcpConnection(
        string id,
        string name,
        string host,
        int port,
        ConnectionStrategy strategy = ConnectionStrategy.Session,
        Dictionary<string, string>? tags = null)
    {
        var descriptor = ConnectionDescriptor.CreateTcp(id, name, host, port, strategy, tags);
        AddConnection(descriptor);
        return this;
    }

    /// <summary>
    /// 添加 KCP 连接
    /// </summary>
    public PulseClientBuilder AddKcpConnection(
        string id,
        string name,
        string host,
        int port,
        ConnectionStrategy strategy = ConnectionStrategy.Session,
        Dictionary<string, string>? tags = null)
    {
        var descriptor = ConnectionDescriptor.CreateKcp(id, name, host, port, strategy, tags);
        AddConnection(descriptor);
        return this;
    }

    /// <summary>
    /// 添加服务发现连接
    /// </summary>
    public PulseClientBuilder AddServiceConnection(
        string id,
        string name,
        string serviceName,
        TransportType transport = TransportType.Tcp,
        ConnectionStrategy strategy = ConnectionStrategy.Session,
        Dictionary<string, string>? tags = null)
    {
        var descriptor = ConnectionDescriptor.CreateService(id, name, serviceName, transport, strategy, tags);
        AddConnection(descriptor);
        return this;
    }

    #endregion
}

/// <summary>
/// 游戏客户端构建器扩展
/// </summary>
public static class GameClientBuilderExtensions
{
    /// <summary>
    /// 添加核心服务连接
    /// </summary>
    public static PulseClientBuilder AddCoreService(this PulseClientBuilder builder, string serviceName)
    {
        return builder.AddServiceConnection(
            id: Guid.NewGuid().ToString("N"),
            name: $"core-{serviceName}",
            serviceName: serviceName,
            transport: TransportType.Tcp,
            strategy: ConnectionStrategy.Persistent,
            tags: new Dictionary<string, string>
            {
                ["type"] = "core",
                ["service"] = serviceName
            });
    }

    /// <summary>
    /// 添加直连配置
    /// </summary>
    public static PulseClientBuilder AddDirectConnection(this PulseClientBuilder builder,
        string name, string host, int port, TransportType transport = TransportType.Tcp)
    {
        return builder.AddTcpConnection(
            id: Guid.NewGuid().ToString("N"),
            name: name,
            host: host,
            port: port,
            strategy: ConnectionStrategy.Session);
    }

    /// <summary>
    /// 添加配置设置
    /// </summary>
    public static PulseClientBuilder WithSetting<T>(this PulseClientBuilder builder, string key, T value)
    {
        // 暂时通过 Configure 方法实现
        return (PulseClientBuilder)builder.Configure(options =>
        {
            options.Settings[key] = value?.ToString() ?? string.Empty;
        });
    }
    /// <summary>
    /// 添加典型的游戏服务器连接配置
    /// </summary>
    public static PulseClientBuilder AddGameServerSet(
        this PulseClientBuilder builder,
        string environment = "production")
    {
        return builder
            .AddCoreService("login-service")
            .AddCoreService("game-world-service")
            .AddCoreService("chat-service")
            .WithSetting("environment", environment);
    }

    /// <summary>
    /// 添加开发环境的直连配置
    /// </summary>
    public static PulseClientBuilder AddDevelopmentServers(
        this PulseClientBuilder builder,
        string baseHost = "localhost",
        int basePort = 8000)
    {
        return builder
            .AddDirectConnection("login", baseHost, basePort)
            .AddDirectConnection("game-world", baseHost, basePort + 1)
            .AddDirectConnection("chat", baseHost, basePort + 2);
    }

    /// <summary>
    /// 配置战斗服优化设置
    /// </summary>
    public static PulseClientBuilder WithBattleOptimizations(this PulseClientBuilder builder)
    {
        return builder
            .WithSetting("battle.preferKcp", true)
            .WithSetting("battle.autoCleanupMinutes", 1)
            .WithSetting("battle.maxConcurrentBattles", 5);
    }

    /// <summary>
    /// 配置连接池设置
    /// </summary>
    public static PulseClientBuilder WithConnectionPooling(
        this PulseClientBuilder builder,
        int maxConnections = 100,
        TimeSpan? idleTimeout = null)
    {
        return builder
            .WithSetting("connectionPool.maxConnections", maxConnections)
            .WithSetting("connectionPool.idleTimeout", idleTimeout ?? TimeSpan.FromMinutes(10));
    }
}
