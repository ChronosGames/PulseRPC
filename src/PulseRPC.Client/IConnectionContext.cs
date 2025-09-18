using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC.Client.Core;

/// <summary>
/// 连接上下文接口
/// </summary>
public interface IConnectionContext : IDisposable
{
    /// <summary>
    /// 连接ID
    /// </summary>
    string Id { get; }

    /// <summary>
    /// 连接配置
    /// </summary>
    ConnectionConfig Config { get; }

    /// <summary>
    /// 连接描述符
    /// </summary>
    ConnectionDescriptor Descriptor { get; }

    /// <summary>
    /// 端点地址
    /// </summary>
    EndpointAddress Endpoint { get; }

    /// <summary>
    /// 连接状态
    /// </summary>
    ExtendedConnectionState State { get; }

    /// <summary>
    /// 连接统计信息
    /// </summary>
    ConnectionStatistics Statistics { get; }

    /// <summary>
    /// 远程端点地址
    /// </summary>
    EndPoint? RemoteEndPoint { get; }

    /// <summary>
    /// 本地端点地址
    /// </summary>
    EndPoint? LocalEndPoint { get; }

    /// <summary>
    /// 连接创建时间
    /// </summary>
    DateTime CreatedAt { get; }

    /// <summary>
    /// 最后活动时间
    /// </summary>
    DateTime LastActivityAt { get; }

    /// <summary>
    /// 连接状态变化事件
    /// </summary>
    event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// 连接到服务器
    /// </summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 断开连接
    /// </summary>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取服务代理
    /// </summary>
    Task<T> GetServiceAsync<T>() where T : class, IPulseHub;

    /// <summary>
    /// 注册事件监听器
    /// </summary>
    Task<ISubscriptionToken> RegisterEventListenerAsync<T>(T listener) where T : class, IPulseReceiver;
}