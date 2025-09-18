using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using PulseRPC.Transport;

namespace PulseRPC.Client;

/// <summary>
/// 连接接口 - 面向业务的轻量级连接抽象
/// 实现思路：
/// - 业务导向：提供面向业务的高级API
/// - 代理缓存：缓存服务代理实例，避免重复创建
/// - 状态透明：向上层暴露连接状态信息
/// - 简化接口：隐藏底层传输复杂性
/// </summary>
public interface IConnection : ITransport
{
    /// <summary>
    /// 连接ID
    /// </summary>
    string Id => this.Name;

    /// <summary>
    /// 连接描述符
    /// </summary>
    ConnectionDescriptor Descriptor { get; }

    /// <summary>
    /// 连接状态
    /// </summary>
    ExtendedConnectionState State { get; }

    /// <summary>
    /// 连接标签
    /// </summary>
    Dictionary<string, string> Tags { get; }

    /// <summary>
    /// 连接统计信息
    /// </summary>
    ConnectionStatistics? Statistics { get; }

    /// <summary>
    /// 发送消息（用于 Source Generator 生成的代理）
    /// </summary>
    ValueTask SendAsync<T>(string hubName, string methodName, in T message, CancellationToken cancellationToken = default);

    /// <summary>
    /// 发送消息并接收响应（用于 Source Generator 生成的代理）
    /// </summary>
    ValueTask<TResponse> InvokeAsync<TResponse>(string hubName, string methodName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 发送消息并接收响应（用于 Source Generator 生成的代理）
    /// </summary>
    ValueTask<TResponse> InvokeAsync<TRequest, TResponse>(string hubName, string methodName, in TRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 注册事件监听器的内部实现（用于 Source Generator 生成的扩展方法）
    /// </summary>
    Task<ISubscriptionToken> RegisterReceiverAsync<T>(T listener, CancellationToken cancellationToken = default) where T : class, IPulseReceiver;

    /// <summary>
    /// 连接状态变化事件
    /// </summary>
    event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;
}

/// <summary>
/// 连接上下文接口 - 会话层的连接抽象
/// 实现思路：
/// - 会话管理：管理连接的会话状态和属性
/// - 序列化集成：集成序列化器进行数据转换
/// - 请求-响应映射：管理RPC请求和响应的对应关系
/// - 传输抽象：封装底层传输层细节
/// - 活动跟踪：跟踪连接活动状态，用于空闲检测
/// </summary>
public interface IConnectionContext : IConnection
{
    /// <summary>
    /// 端点地址
    /// </summary>
    EndpointAddress Endpoint { get; }

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
    /// 连接到服务器
    /// </summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 断开连接
    /// </summary>
    Task DisconnectAsync(CancellationToken cancellationToken = default);
}
