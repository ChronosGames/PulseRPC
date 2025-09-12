// using Microsoft.Extensions.DependencyInjection;
// using Microsoft.Extensions.Logging;
// using PulseRPC.Client.Channels;
// using PulseRPC.Transport;
// using PulseRPC.Serialization;
// using PulseRPC.Client.Serialization;
// using System.Collections.Generic;
// using Microsoft.Extensions.Logging.Abstractions;
// using PulseRPC.Authentication;
//
// namespace PulseRPC.Client;
//
// /// <summary>
// /// PulseClient 构建器实现
// /// </summary>
// public class PulseClientBuilder
// {
//     private readonly List<ClientTransportConfiguration> _transports = new();
//     private TimeSpan _timeout = TimeSpan.FromSeconds(30);
//     private RetryOptions _retryOptions = new();
//     private ILoggerFactory? _loggerFactory;
//     private ISerializerProvider? _serializerProvider;
//
//     public PulseClientBuilder()
//     {
//     }
//
//     public PulseClientBuilder AddTransport(string name, TransportType type, string host, int port, TransportOptions options)
//     {
//         var config = new ClientTransportConfiguration
//         {
//             Name = name ?? $"transport-{_transports.Count + 1}",
//             Type = type,
//             Host = host,
//             Port = port,
//             IsDefault = _transports.Count == 0, // 第一个添加的传输设为默认
//             Options = options
//         };
//
//         _transports.Add(config);
//         return this;
//     }
//
//     /// <summary>
//     /// 添加 TCP 传输
//     /// </summary>
//     public PulseClientBuilder AddTcp(string name, string host, int port)
//     {
//         var config = new ClientTransportConfiguration
//         {
//             Name = name,
//             Type = TransportType.Tcp,
//             Host = host,
//             Port = port,
//             IsDefault = _transports.Count == 0, // 第一个添加的传输设为默认
//             Options = new TcpTransportOptions
//             {
//                 ConnectionTimeout = (int)_timeout.TotalMilliseconds,
//                 KeepAlive = true
//             }
//         };
//
//         _transports.Add(config);
//         return this;
//     }
//
//     /// <summary>
//     /// 添加 KCP 传输
//     /// </summary>
//     public PulseClientBuilder AddKcp(string name, string host, int port)
//     {
//         var config = new ClientTransportConfiguration
//         {
//             Name = name,
//             Type = TransportType.Kcp,
//             Host = host,
//             Port = port,
//             IsDefault = _transports.Count == 0, // 第一个添加的传输设为默认
//             Options = new KcpTransportOptions
//             {
//                 ConnectionTimeout = (int)_timeout.TotalMilliseconds,
//                 KeepAlive = true,
//                 NoDelay = true,
//                 Interval = 10,
//                 Resend = 2,
//                 DisableFlowControl = false,
//             }
//         };
//
//         _transports.Add(config);
//         return this;
//     }
//
//     /// <summary>
//     /// 配置认证
//     /// </summary>
//     // public PulseRPCClientBuilder WithAuthentication(IAuthenticationProvider provider)
//     // {
//     //     _authenticationProvider = provider;
//     //     return this;
//     // }
//
//     /// <summary>
//     /// 配置超时
//     /// </summary>
//     public PulseClientBuilder WithTimeout(TimeSpan timeout)
//     {
//         _timeout = timeout;
//         return this;
//     }
//
//     /// <summary>
//     /// 配置重试策略
//     /// </summary>
//     public PulseClientBuilder WithRetry(Action<RetryOptions> configure)
//     {
//         configure(_retryOptions);
//         return this;
//     }
//
//     /// <summary>
//     /// 配置连接池
//     /// </summary>
//     // public PulseRPCClientBuilder WithConnectionPool(Action<global::PulseRPC.ConnectionPoolOptions> configure)
//     // {
//     //     configure(_connectionPoolOptions);
//     //     return this;
//     // }
//
//     /// <summary>
//     /// 配置日志工厂
//     /// </summary>
//     public PulseClientBuilder WithLogging(ILoggerFactory loggerFactory)
//     {
//         _loggerFactory = loggerFactory;
//         return this;
//     }
//
//     /// <summary>
//     /// 配置序列化器
//     /// </summary>
//     public PulseClientBuilder WithSerializer(ISerializerProvider serializerProvider)
//     {
//         _serializerProvider = serializerProvider;
//         return this;
//     }
//
//     /// <summary>
//     /// 配置 MemoryPack 序列化器
//     /// </summary>
//     public PulseClientBuilder WithMemoryPackSerializer(MemoryPack.MemoryPackSerializerOptions? options = null)
//     {
//         _serializerProvider = options != null
//             ? PulseRPCSerializerProvider.Instance.WithOptions(options)
//             : PulseRPCSerializerProvider.Instance;
//         return this;
//     }
//
//     /// <summary>
//     /// 构建高性能客户端
//     /// </summary>
//     public IPulseClient Build()
//     {
//         if (_transports.Count == 0)
//         {
//             throw new InvalidOperationException("至少需要配置一个传输方式");
//         }
//
//         // 创建日志工厂
//         var loggerFactory = _loggerFactory ?? NullLoggerFactory.Instance;
//
//         // 创建传输管理器
//         var transportManager = new ChannelManager(
//             Transport.TransportManagerType.Client,
//             loggerFactory.CreateLogger<Transport.TransportManager>());
//
//         // 创建序列化器管理器
//         var serializerManager = new SerializerManager(_serializerProvider);
//
//         // 创建通道管理器
//         var channelManager = new ChannelManager(
//             transportManager,
//             serializerManager,
//             loggerFactory.CreateLogger<ChannelManager>());
//
//         // 创建客户端
//         var client = new PulseClient(
//             transportManager,
//             channelManager,
//             serializerManager,
//             loggerFactory);
//
//         // 添加所有配置的传输
//         client.AddTransports(_transports);
//
//         return client;
//     }
// }
