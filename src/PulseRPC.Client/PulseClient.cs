// using PulseRPC.Transport;
// using Microsoft.Extensions.Logging;
// using System;
// using System.Net;
// using Microsoft.Extensions.Logging.Abstractions;
// using PulseRPC.Client.Channels;
// using PulseRPC.Client.Transport;
// using PulseRPC.Serialization;
// using PulseRPC.Client.Serialization;
// using PulseRPC.Messaging;
// using System.Collections.Concurrent;
// using System.Runtime.CompilerServices;
// using Microsoft.Extensions.DependencyInjection;
using PulseRPC.Client.Core;
// using ConnectionState = PulseRPC.Transport.ConnectionState;
//
namespace PulseRPC.Client;

// /// <summary>
// /// PulseRPC 统一客户端接口 - 整合所有客户端功能
// /// </summary>
// public interface IPulseClient : IDisposable
// {
//     /// <summary>
//     /// 连接管理器
//     /// </summary>
//     IConnectionManager Connections { get; }
//
//     /// <summary>
//     /// 初始化客户端（替代原来的 ConnectAsync）
//     /// </summary>
//     Task InitializeAsync(CancellationToken cancellationToken = default);
//
//     /// <summary>
//     /// 停止客户端（替代原来的 DisconnectAsync）
//     /// </summary>
//     Task StopAsync(CancellationToken cancellationToken = default);
//
//     /// <summary>
//     /// 客户端是否已初始化
//     /// </summary>
//     bool IsInitialized { get; }
// }
//

/// <summary>
/// 高性能 PulseRPC 客户端实现
/// 使用零分配设计和缓存优化
/// </summary>
internal class PulseClient : IPulseRPCClient
{
//     private readonly IChannelManager _channelManager;
//     private readonly ISerializerManager _serializerManager;
//     private readonly ILogger<PulseClient> _logger;
//     private readonly ILoggerFactory _loggerFactory;
//     private readonly Dictionary<string, ClientTransportInfo> _transports = new();
//
//     // 服务代理缓存 - 线程安全
//     private readonly ConcurrentDictionary<Type, object> _serviceProxyCache = new();
//
//     private volatile bool _isConnected;
//     private volatile bool _disposed;
//
//     public PulseClient(
//         IChannelManager? channelManager = null,
//         ISerializerManager? serializerManager = null,
//         ILoggerFactory? loggerFactory = null)
//     {
//         _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
//         _logger = _loggerFactory.CreateLogger<PulseClient>();
//         _channelManager = channelManager ?? throw new ArgumentNullException(nameof(channelManager));
//         _serializerManager = serializerManager ?? new SerializerManager();
//
//         // 订阅传输事件
//         _transportManager.TransportConnected += OnTransportConnected;
//         _transportManager.TransportDisconnected += OnTransportDisconnected;
//     }
//
//     /// <summary>
//     /// 添加传输配置
//     /// </summary>
//     internal void AddTransport(ClientTransportConfiguration config)
//     {
//         if (_isConnected)
//         {
//             throw new InvalidOperationException("客户端已连接，无法添加传输");
//         }
//
//         if (_transports.ContainsKey(config.Name))
//         {
//             throw new ArgumentException($"传输配置已存在: {config.Name}");
//         }
//
//         var transportInfo = new ClientTransportInfo
//         {
//             Name = config.Name,
//             Type = config.Type,
//             Host = config.Host,
//             Port = config.Port,
//             Options = config.Options,
//             IsDefault = config.IsDefault,
//         };
//
//         _transports.Add(config.Name, transportInfo);
//
//         _logger.LogInformation("已添加 {Type} 传输配置: {Name}, 目标: {Host}:{Port}",
//             config.Type, config.Name, config.Host, config.Port);
//     }
//
//     /// <summary>
//     /// 批量添加传输配置
//     /// </summary>
//     internal void AddTransports(IEnumerable<ClientTransportConfiguration> configurations)
//     {
//         foreach (var config in configurations)
//         {
//             AddTransport(config);
//         }
//     }
//
//     /// <summary>
//     /// 连接到服务器
//     /// </summary>
//     public async Task ConnectAsync(CancellationToken cancellationToken = default)
//     {
//         if (_isConnected)
//         {
//             return;
//         }
//
//         if (_transports.Count == 0)
//         {
//             throw new InvalidOperationException("没有配置任何传输");
//         }
//
//         _logger.LogInformation("正在连接到服务器，传输配置数量：{Count}", _transports.Count);
//
//         try
//         {
//             // 连接所有配置的传输
//             var connectionTasks = _transports.Values.Select(async transportInfo =>
//             {
//                 _logger.LogDebug("正在连接 {Type} 传输: {Name} at {Host}:{Port}", transportInfo.Type, transportInfo.Name, transportInfo.Host, transportInfo.Port);
//
//                 // 创建传输连接（这里需要根据实际的传输工厂来创建）
//                 // 暂时使用模拟实现
//                 var transport = await CreateTransportAsync(transportInfo, cancellationToken);
//
//                 // 添加到传输管理器
//                 var context = _transportManager.AddTransport(transport);
//
//                 // 设置默认传输
//                 if (transportInfo.IsDefault)
//                 {
//                     _transportManager.SetDefaultTransport(transport.Name);
//                 }
//
//                 _logger.LogInformation("{Type} 传输已连接: {Name} {ServiceId}", transportInfo.Type, transportInfo.Name, context.ConnectionId);
//             });
//
//             // 等待所有传输连接完成
//             await Task.WhenAll(connectionTasks);
//
//             _isConnected = true;
//             _logger.LogInformation("所有传输已连接，客户端连接完成。活动连接数：{Count}", _transportManager.ConnectionCount);
//         }
//         catch (Exception ex)
//         {
//             _logger.LogError(ex, "连接服务器失败");
//
//             // 清理已连接的传输
//             await DisconnectAsync(CancellationToken.None);
//             throw;
//         }
//     }
//
//     /// <summary>
//     /// 断开连接
//     /// </summary>
//     public async Task DisconnectAsync(CancellationToken cancellationToken = default)
//     {
//         if (!_isConnected && _transportManager.ConnectionCount == 0)
//             return;
//
//         _logger.LogInformation("正在断开连接，当前连接数：{Count}", _transportManager.ConnectionCount);
//
//         try
//         {
//             // 断开所有传输连接
//             var connectionIds = _transportManager.ConnectionIds.ToList();
//             var disconnectionTasks = connectionIds.Select(async connectionId =>
//             {
//                 try
//                 {
//                     await _transportManager.RemoveTransportAsync(connectionId);
//                     _logger.LogDebug("传输连接已断开：{ConnectionId}", connectionId);
//                 }
//                 catch (Exception ex)
//                 {
//                     _logger.LogWarning(ex, "断开传输连接时发生异常：{ConnectionId}", connectionId);
//                 }
//             });
//
//             // 等待所有传输断开完成
//             await Task.WhenAll(disconnectionTasks);
//
//             _isConnected = false;
//             _logger.LogInformation("所有传输已断开，客户端断开连接完成");
//         }
//         catch (Exception ex)
//         {
//             _logger.LogError(ex, "断开连接时发生错误");
//             _isConnected = false; // 即使出错也标记为未连接
//         }
//     }
//
//
//     /// <summary>
//     /// 获取服务代理 - 高性能缓存实现
//     /// </summary>
//     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     public T GetService<T>() where T : class, IPulseHub
//     {
//         // 使用类型缓存避免重复创建
//         if (_serviceProxyCache.TryGetValue(typeof(T), out var cachedService))
//         {
//             return (T)cachedService;
//         }
//
//         // 使用源码生成器生成的扩展方法
//         var service = this.GetServiceInternal<T>();
//         _serviceProxyCache.TryAdd(typeof(T), service);
//         return service;
//     }
//
//     /// <summary>
//     /// 获取服务代理 - 异步版本
//     /// </summary>
//     public async Task<T> GetServiceAsync<T>(string? serviceName = null, CancellationToken cancellationToken = default)
//         where T : class, IPulseHub
//     {
//         // 对于普通客户端，直接返回同步版本
//         return GetService<T>();
//     }
//
//     /// <summary>
//     /// 配置序列化器
//     /// </summary>
//     public IPulseClient WithSerializer(ISerializerProvider serializerProvider)
//     {
//         _serializerManager.SetDefaultProvider(serializerProvider);
//         return this;
//     }
//
//     /// <summary>
//     /// 配置默认序列化器
//     /// </summary>
//     public IPulseClient WithDefaultSerializer(MemoryPack.MemoryPackSerializerOptions options)
//     {
//         var provider = PulseRPCSerializerProvider.Instance.WithOptions(options);
//         return WithSerializer(provider);
//     }
//
//     /// <summary>
//     /// 获取连接统计信息
//     /// </summary>
//     // public async Task<ConnectionStatistics> GetConnectionStatisticsAsync(CancellationToken cancellationToken = default)
//     // {
//     //     var transportStats = _transportManager.GetStatistics();
//     //
//     //     return new ConnectionStatistics
//     //     {
//     //         TotalConnections = (int)transportStats.TotalTransportsCreated,
//     //         ActiveConnections = transportStats.ActiveTransports,
//     //         IdleConnections = 0, // TODO: 实现空闲连接统计
//     //         FailedConnections = (int)(transportStats.TotalTransportsCreated - transportStats.TotalTransportsRemoved - transportStats.ActiveTransports),
//     //         Timestamp = DateTime.UtcNow
//     //     };
//     // }
//
//     /// <summary>
//     /// 连接状态变化事件
//     /// </summary>
//     // public event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;
//
//     /// <summary>
//     /// 获取默认传输上下文
//     /// </summary>
//     // public TransportContext? GetDefaultTransportContext()
//     // {
//     //     return _transportManager.GetDefaultTransportContext();
//     // }
//
//     /// <summary>
//     /// 释放资源
//     /// </summary>
//     public void Dispose()
//     {
//         if (_disposed)
//             return;
//
//         try
//         {
//             if (_isConnected)
//             {
//                 DisconnectAsync().Wait(TimeSpan.FromSeconds(5));
//             }
//         }
//         catch (Exception ex)
//         {
//             _logger.LogError(ex, "释放资源时断开连接失败");
//         }
//
//         // 取消订阅传输事件
//         _transportManager.TransportConnected -= OnTransportConnected;
//         _transportManager.TransportDisconnected -= OnTransportDisconnected;
//
//         _transportManager?.Dispose();
//         _disposed = true;
//
//         GC.SuppressFinalize(this);
//     }
//
//     /// <summary>
//     /// 创建传输连接 - 模拟实现，实际需要根据传输工厂来创建
//     /// </summary>
//     private Task<ITransport> CreateTransportAsync(ClientTransportInfo transportInfo, CancellationToken cancellationToken)
//     {
//         IClientTransport transport = transportInfo.Type switch
//         {
//             TransportType.Tcp => new TcpClientTransport(transportInfo.Options as TcpTransportOptions ?? new TcpTransportOptions(), _loggerFactory.CreateLogger<TcpClientTransport>()),
//             TransportType.Kcp => new KcpClientTransport(transportInfo.Options as KcpTransportOptions ?? new KcpTransportOptions(), _loggerFactory.CreateLogger<KcpClientTransport>()),
//             _ => throw new NotSupportedException($"不支持的传输类型: {transportInfo.Type}")
//         };
//
//         return Task.FromResult<ITransport>(transport);
//     }
//
//     /// <summary>
//     /// 处理传输连接事件
//     /// </summary>
//     private void OnTransportConnected(object? sender, TransportConnectedEventArgs e)
//     {
//         _logger.LogDebug("传输已连接：{ConnectionId}", e.TransportContext.ConnectionId);
//
//         var oldState = _isConnected ? ConnectionState.Connected : ConnectionState.Disconnected;
//         var newState = _transportManager.ConnectionCount > 0 ? ConnectionState.Connected : ConnectionState.Disconnected;
//
//         if (oldState != newState)
//         {
//             ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(oldState, newState));
//         }
//     }
//
//     /// <summary>
//     /// 处理传输断开事件
//     /// </summary>
//     private void OnTransportDisconnected(object? sender, TransportDisconnectedEventArgs e)
//     {
//         _logger.LogDebug("传输已断开：{ConnectionId}, 原因：{Reason}",
//             e.TransportContext.ConnectionId, e.DisconnectReason);
//
//         var oldState = _isConnected ? ConnectionState.Connected : ConnectionState.Disconnected;
//         var newState = _transportManager.ConnectionCount > 0 ? ConnectionState.Connected : ConnectionState.Disconnected;
//
//         if (oldState != newState)
//         {
//             _isConnected = newState == ConnectionState.Connected;
//             ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(oldState, newState));
//         }
//     }
//
//     /// <summary>
//     /// 客户端传输信息
//     /// </summary>
//     private class ClientTransportInfo
//     {
//         public string Name { get; set; } = string.Empty;
//         public TransportType Type { get; set; }
//         public string Host { get; set; } = string.Empty;
//         public int Port { get; set; }
//         public TransportOptions? Options { get; set; }
//         public bool IsDefault { get; set; }
//     }
}
