using Microsoft.Extensions.Logging;
using PulseRPC.Authentication;
using PulseRPC.Serialization;
using PulseRPC.Shared;

namespace PulseRPC.Client.Configuration;

/// <summary>
/// PulseRPC 客户端构建器
/// </summary>
public sealed class PulseClientBuilder : IPulseClientBuilder
{
    private readonly List<ConnectionDescriptor> _connections = new();
    private ILoggerFactory? _loggerFactory;
    private ISerializerProvider? _serializerProvider;
    private IAuthenticationProvider? _authenticationProvider;
    private LoadBalancingStrategy _loadBalancingStrategy = LoadBalancingStrategy.RoundRobin;
    private readonly Dictionary<string, object> _loadBalancingOptions = new();
#pragma warning disable CS0618 // Stored only so compatibility builder calls can fail explicitly at Build().
    private ConnectionPoolOptions? _connectionPoolOptions;
    private RetryPolicy? _retryPolicy;
#pragma warning restore CS0618
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
    /// 兼容入口；连接池尚未接入客户端运行时。
    /// </summary>
    [Obsolete("Connection pooling is not connected to the client runtime. Configure explicit connections instead.", false)]
    public IPulseClientBuilder WithConnectionPooling(ConnectionPoolOptions poolOptions)
    {
        _connectionPoolOptions = poolOptions ?? throw new ArgumentNullException(nameof(poolOptions));
        return this;
    }

    /// <summary>
    /// 兼容入口；重试策略尚未接入请求或连接路径。
    /// </summary>
    [Obsolete("RetryPolicy is not connected to client requests or connection attempts.", false)]
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
    /// 兼容入口；认证提供者尚未接入传输握手。
    /// </summary>
    [Obsolete("Client authentication is not connected to the transport handshake.", false)]
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
            loggerFactory: _loggerFactory,
            serializerProvider: _serializerProvider,
            authenticationProvider: _authenticationProvider,
            loadBalancingStrategy: _loadBalancingStrategy,
            connectionPoolOptions: _connectionPoolOptions,
            retryPolicy: _retryPolicy,
            clientOptions: _clientOptions);
    }

    /// <summary>
    /// 验证构建器配置
    /// </summary>
    private void ValidateConfiguration()
    {
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
        if (_authenticationProvider != null)
        {
            throw new NotSupportedException(
                "WithAuthentication 当前尚未接入客户端传输握手，不能保证认证令牌会发送到服务端。");
        }

        if (_connectionPoolOptions != null)
        {
            throw new NotSupportedException(
                "WithConnectionPooling 当前尚未接入 ConnectionManager，不能保证连接池配置生效。");
        }

        if (_retryPolicy != null)
        {
            throw new NotSupportedException(
                "WithRetryPolicy 当前尚未接入客户端请求/连接主路径，不能保证重试策略生效。");
        }

        if (_loadBalancingOptions.Count > 0)
        {
            throw new NotSupportedException(
                "WithLoadBalancing 的 options 参数当前尚未被负载均衡器消费；仅 strategy 参数会生效。");
        }

        if (_clientOptions.LoadBalancing == null)
        {
            throw new InvalidOperationException("负载均衡配置不能为空");
        }

        if (_clientOptions.LoadBalancing.ConsistentHashVirtualNodes is < 1 or > 4096)
        {
            throw new InvalidOperationException("一致性哈希虚拟节点数必须在 1 到 4096 之间");
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

    #region 预设配置方法

    /// <summary>
    /// Applies the legacy default preset.
    /// </summary>
    [Obsolete("Client presets contain settings that are not consumed. Configure effective options explicitly.", false)]
    public PulseClientBuilder UseDefaults()
    {
        Configure(ClientPresets.Default);
        return this;
    }

    /// <summary>
    /// Applies the legacy game-client preset.
    /// </summary>
    [Obsolete("This preset does not change channel timeout, concurrency, or statistics behavior. Configure each connection explicitly.", false)]
    public PulseClientBuilder UseGameClientPreset()
    {
        Configure(ClientPresets.GameClient);
        return this;
    }

    /// <summary>
    /// Applies the legacy high-throughput preset.
    /// </summary>
    [Obsolete("This preset does not change channel timeout, concurrency, or statistics behavior. Configure each connection explicitly.", false)]
    public PulseClientBuilder UseHighThroughputPreset()
    {
        Configure(ClientPresets.HighThroughput);
        return this;
    }

    /// <summary>
    /// Applies the legacy development preset.
    /// </summary>
    [Obsolete("This preset does not change channel timeout or logging behavior. Configure transport options and Microsoft.Extensions.Logging explicitly.", false)]
    public PulseClientBuilder UseDevelopmentPreset()
    {
        Configure(ClientPresets.Development);
        return this;
    }

    #endregion

    #region 静态工厂方法

    /// <summary>
    /// 快速创建默认配置的客户端
    /// </summary>
    /// <param name="loggerFactory">日志工厂（可选）</param>
    public static IPulseClient CreateDefault(ILoggerFactory? loggerFactory = null)
    {
        var builder = new PulseClientBuilder();
        if (loggerFactory != null)
            builder.WithLogging(loggerFactory);
        return builder.Build();
    }

    /// <summary>
    /// 快速创建游戏客户端
    /// </summary>
    /// <param name="loggerFactory">日志工厂（可选）</param>
    [Obsolete("The game preset does not change channel runtime behavior. Use PulseClientBuilder and configure each connection explicitly.", false)]
    public static IPulseClient CreateGameClient(ILoggerFactory? loggerFactory = null)
    {
        var builder = new PulseClientBuilder().UseGameClientPreset();
        if (loggerFactory != null)
            builder.WithLogging(loggerFactory);
        return builder.Build();
    }

    /// <summary>
    /// 快速创建开发环境客户端
    /// </summary>
    /// <param name="loggerFactory">日志工厂（可选）</param>
    [Obsolete("The development preset does not change channel timeout or logging behavior. Use PulseClientBuilder with explicit transport and logging configuration.", false)]
    public static IPulseClient CreateDevelopment(ILoggerFactory? loggerFactory = null)
    {
        var builder = new PulseClientBuilder().UseDevelopmentPreset();
        if (loggerFactory != null)
            builder.WithLogging(loggerFactory);
        return builder.Build();
    }

    #endregion

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
        TransportType transport = TransportType.TCP,
        ConnectionStrategy strategy = ConnectionStrategy.Session,
        Dictionary<string, string>? tags = null)
    {
        var descriptor = ConnectionDescriptor.CreateService(id, name, serviceName, transport, strategy, tags);
        AddConnection(descriptor);
        return this;
    }

    #endregion
}
