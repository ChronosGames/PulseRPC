using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PulseRPC.Client.Channels;
using PulseRPC.Transport;
using System.Collections.Generic;

namespace PulseRPC.Client;

/// <summary>
/// PulseClient 构建器实现
/// </summary>
internal class PulseClientBuilder : IPulseClientBuilder
{
    private readonly List<ClientTransportConfiguration> _transports = new();
    private readonly IServiceProvider _serviceProvider;
    private global::PulseRPC.ServiceDiscoveryOptions? _serviceDiscoveryOptions;
    private IAuthenticationProvider? _authenticationProvider;
    private TimeSpan _timeout = TimeSpan.FromSeconds(30);
    private RetryOptions _retryOptions = new();
    private global::PulseRPC.ConnectionPoolOptions _connectionPoolOptions = new();

    public PulseClientBuilder(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// 添加 TCP 传输
    /// </summary>
    public IPulseClientBuilder AddTcp(string name, string host, int port)
    {
        var config = new ClientTransportConfiguration
        {
            Name = name,
            Type = TransportType.Tcp,
            Host = host,
            Port = port,
            IsDefault = _transports.Count == 0, // 第一个添加的传输设为默认
            Options = new TransportOptions
            {
                ConnectionTimeout = (int)_timeout.TotalMilliseconds,
                KeepAlive = true
            }
        };

        _transports.Add(config);
        return this;
    }

    /// <summary>
    /// 添加 KCP 传输
    /// </summary>
    public IPulseClientBuilder AddKcp(string name, string host, int port)
    {
        var config = new ClientTransportConfiguration
        {
            Name = name,
            Type = TransportType.Kcp,
            Host = host,
            Port = port,
            IsDefault = _transports.Count == 0, // 第一个添加的传输设为默认
            Options = new TransportOptions
            {
                ConnectionTimeout = (int)_timeout.TotalMilliseconds,
                KeepAlive = true,
                Kcp = new KcpOptions
                {
                    NoDelay = 1,
                    Interval = 10,
                    Resend = 2,
                    DisableFlowControl = false
                },
            }
        };

        _transports.Add(config);
        return this;
    }

    /// <summary>
    /// 配置服务发现
    /// </summary>
    public IPulseClientBuilder WithServiceDiscovery(Action<global::PulseRPC.ServiceDiscoveryOptions> configure)
    {
        _serviceDiscoveryOptions ??= new global::PulseRPC.ServiceDiscoveryOptions();
        configure(_serviceDiscoveryOptions);
        return this;
    }

    /// <summary>
    /// 配置认证
    /// </summary>
    public IPulseClientBuilder WithAuthentication(IAuthenticationProvider provider)
    {
        _authenticationProvider = provider;
        return this;
    }

    /// <summary>
    /// 配置超时
    /// </summary>
    public IPulseClientBuilder WithTimeout(TimeSpan timeout)
    {
        _timeout = timeout;
        return this;
    }

    /// <summary>
    /// 配置重试策略
    /// </summary>
    public IPulseClientBuilder WithRetry(Action<RetryOptions> configure)
    {
        configure(_retryOptions);
        return this;
    }

    /// <summary>
    /// 配置连接池
    /// </summary>
    public IPulseClientBuilder WithConnectionPool(Action<global::PulseRPC.ConnectionPoolOptions> configure)
    {
        configure(_connectionPoolOptions);
        return this;
    }

    /// <summary>
    /// 构建客户端
    /// </summary>
    public IPulseClient Build()
    {
        if (_transports.Count == 0)
        {
            throw new InvalidOperationException("至少需要配置一个传输方式");
        }

        var loggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();
        var channelManager = _serviceProvider.GetRequiredService<IChannelManager>();

        var client = new PulseRpcClientManager(channelManager, loggerFactory);

        // 添加所有配置的传输
        client.AddTransports(_transports);

        return client;
    }
}
