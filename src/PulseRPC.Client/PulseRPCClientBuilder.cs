using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PulseRPC.Client.Core;
using PulseRPC.Transport;

namespace PulseRPC.Client.Redesign;

/// <summary>
/// 重新设计的客户端构建器
/// </summary>
public class PulseRPCClientBuilder
{
    private readonly List<ConnectionConfig> _initialConnections = new();
    private IServiceDiscovery? _serviceDiscovery;
    private ILoggerFactory? _loggerFactory;
    private readonly Dictionary<string, object> _settings = new();

    /// <summary>
    /// 添加初始连接配置
    /// </summary>
    public PulseRPCClientBuilder AddConnection(ConnectionConfig config)
    {
        _initialConnections.Add(config);
        return this;
    }

    /// <summary>
    /// 添加核心服务连接
    /// </summary>
    public PulseRPCClientBuilder AddCoreService(
        string serviceName,
        TransportType transport = TransportType.Tcp,
        TransportOptions? options = null)
    {
        var config = new ConnectionConfig
        {
            Name = $"core-{serviceName}",
            ServiceName = serviceName,
            Transport = transport,
            Options = options,
            Lifetime = ConnectionLifetime.Persistent,
            AutoReconnect = true,
            Tags = { ["type"] = "core", ["service"] = serviceName }
        };

        return AddConnection(config);
    }

    /// <summary>
    /// 添加直连服务器
    /// </summary>
    public PulseRPCClientBuilder AddDirectConnection(
        string name,
        string host,
        int port,
        TransportType transport = TransportType.Tcp,
        ConnectionLifetime lifetime = ConnectionLifetime.Persistent,
        TransportOptions? options = null)
    {
        var config = new ConnectionConfig
        {
            Name = name,
            Host = host,
            Port = port,
            Transport = transport,
            Options = options,
            Lifetime = lifetime,
            AutoReconnect = lifetime != ConnectionLifetime.Transient,
            Tags = { ["type"] = "direct" }
        };

        return AddConnection(config);
    }

    /// <summary>
    /// 配置服务发现
    /// </summary>
    public PulseRPCClientBuilder WithServiceDiscovery(IServiceDiscovery serviceDiscovery)
    {
        _serviceDiscovery = serviceDiscovery;
        return this;
    }

    /// <summary>
    /// 配置日志工厂
    /// </summary>
    public PulseRPCClientBuilder WithLogging(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        return this;
    }

    /// <summary>
    /// 配置设置
    /// </summary>
    public PulseRPCClientBuilder WithSetting<T>(string key, T value)
    {
        _settings[key] = value ?? throw new ArgumentNullException(nameof(value));
        return this;
    }

    /// <summary>
    /// 构建客户端
    /// </summary>
    public IPulseRPCClient Build()
    {
        return new PulseRPCClient(_initialConnections, _serviceDiscovery, _loggerFactory, _settings);
    }
}

/// <summary>
/// 游戏客户端构建器扩展
/// </summary>
public static class GameClientBuilderExtensions
{
    /// <summary>
    /// 添加典型的游戏服务器连接配置
    /// </summary>
    public static PulseRPCClientBuilder AddGameServerSet(
        this PulseRPCClientBuilder builder,
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
    public static PulseRPCClientBuilder AddDevelopmentServers(
        this PulseRPCClientBuilder builder,
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
    public static PulseRPCClientBuilder WithBattleOptimizations(this PulseRPCClientBuilder builder)
    {
        return builder
            .WithSetting("battle.preferKcp", true)
            .WithSetting("battle.autoCleanupMinutes", 1)
            .WithSetting("battle.maxConcurrentBattles", 5);
    }

    /// <summary>
    /// 配置连接池设置
    /// </summary>
    public static PulseRPCClientBuilder WithConnectionPooling(
        this PulseRPCClientBuilder builder,
        int maxConnections = 100,
        TimeSpan? idleTimeout = null)
    {
        return builder
            .WithSetting("connectionPool.maxConnections", maxConnections)
            .WithSetting("connectionPool.idleTimeout", idleTimeout ?? TimeSpan.FromMinutes(10));
    }
}
