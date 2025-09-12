using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PulseRPC.Client.Channels;
using PulseRPC.Transport;
using System.Collections.Generic;

namespace PulseRPC.Client;

/// <summary>
/// PulseClient 构建器实现
/// </summary>
public class PulseRPCClientBuilder
{
    private readonly List<ClientTransportConfiguration> _transports = new();
    private global::PulseRPC.ServiceDiscoveryOptions? _serviceDiscoveryOptions;
    private IAuthenticationProvider? _authenticationProvider;
    private TimeSpan _timeout = TimeSpan.FromSeconds(30);
    private RetryOptions _retryOptions = new();
    private global::PulseRPC.ConnectionPoolOptions _connectionPoolOptions = new();

    public PulseRPCClientBuilder()
    {
    }

    public PulseRPCClientBuilder AddTransport(string name, TransportType type, string host, int port, TransportOptions options)
    {
        var config = new ClientTransportConfiguration
        {
            Name = name ?? $"transport-{_transports.Count + 1}",
            Type = type,
            Host = host,
            Port = port,
            IsDefault = _transports.Count == 0, // 第一个添加的传输设为默认
            Options = options
        };

        _transports.Add(config);
        return this;
    }

    /// <summary>
    /// 添加 TCP 传输
    /// </summary>
    public PulseRPCClientBuilder AddTcp(string name, string host, int port)
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
    public PulseRPCClientBuilder AddKcp(string name, string host, int port)
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
    public PulseRPCClientBuilder WithServiceDiscovery(Action<global::PulseRPC.ServiceDiscoveryOptions> configure)
    {
        _serviceDiscoveryOptions ??= new global::PulseRPC.ServiceDiscoveryOptions();
        configure(_serviceDiscoveryOptions);
        return this;
    }

    /// <summary>
    /// 配置认证
    /// </summary>
    public PulseRPCClientBuilder WithAuthentication(IAuthenticationProvider provider)
    {
        _authenticationProvider = provider;
        return this;
    }

    /// <summary>
    /// 配置超时
    /// </summary>
    public PulseRPCClientBuilder WithTimeout(TimeSpan timeout)
    {
        _timeout = timeout;
        return this;
    }

    /// <summary>
    /// 配置重试策略
    /// </summary>
    public PulseRPCClientBuilder WithRetry(Action<RetryOptions> configure)
    {
        configure(_retryOptions);
        return this;
    }

    /// <summary>
    /// 配置连接池
    /// </summary>
    public PulseRPCClientBuilder WithConnectionPool(Action<global::PulseRPC.ConnectionPoolOptions> configure)
    {
        configure(_connectionPoolOptions);
        return this;
    }

    /// <summary>
    /// 构建客户端
    /// </summary>
    public IPulseRPCClient Build()
    {
        if (_transports.Count == 0)
        {
            throw new InvalidOperationException("至少需要配置一个传输方式");
        }

        var client = new PulseRPCClient();

        // 添加所有配置的传输
        client.AddTransports(_transports);

        return client;
    }
}
