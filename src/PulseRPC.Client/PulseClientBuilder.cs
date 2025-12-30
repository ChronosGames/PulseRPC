using Microsoft.Extensions.Logging;
using PulseRPC.Authentication;
using PulseRPC.Serialization;
using PulseRPC.Transport;

namespace PulseRPC.Client;

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

    #region 预设配置方法

    /// <summary>
    /// 使用默认预设配置
    /// </summary>
    public PulseClientBuilder UseDefaults()
    {
        Configure(ClientPresets.Default);
        return this;
    }

    /// <summary>
    /// 使用游戏客户端预设 - 低延迟优化
    /// </summary>
    public PulseClientBuilder UseGameClientPreset()
    {
        Configure(ClientPresets.GameClient);
        return this;
    }

    /// <summary>
    /// 使用高吞吐预设 - 服务端到服务端通信
    /// </summary>
    public PulseClientBuilder UseHighThroughputPreset()
    {
        Configure(ClientPresets.HighThroughput);
        return this;
    }

    /// <summary>
    /// 使用开发环境预设 - 长超时便于调试
    /// </summary>
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
        var builder = new PulseClientBuilder().UseDefaults();
        if (loggerFactory != null)
            builder.WithLogging(loggerFactory);
        return builder.Build();
    }

    /// <summary>
    /// 快速创建游戏客户端
    /// </summary>
    /// <param name="loggerFactory">日志工厂（可选）</param>
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
