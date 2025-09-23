using PulseRPC.Messaging;
using PulseRPC.Transport;

namespace PulseRPC.Client;

/// <summary>
/// 连接上下文接口 - 会话层的连接抽象，集成了IConnection的功能
/// 实现思路：
/// - 会话管理：管理连接的会话状态和属性
/// - 序列化集成：集成序列化器进行数据转换
/// - 请求-响应映射：管理RPC请求和响应的对应关系
/// - 传输抽象：封装底层传输层细节
/// - 活动跟踪：跟踪连接活动状态，用于空闲检测
/// - 统一接口：集成IConnection功能，简化传输层架构
/// </summary>
public interface IClientChannel : IDisposable
{
    string Id { get; }

    /// <summary>
    /// 连接描述符
    /// </summary>
    ConnectionDescriptor Descriptor { get; }

    /// <summary>
    /// 连接状态
    /// </summary>
    ExtendedConnectionState State { get; }

    /// <summary>
    /// 连接统计信息
    /// </summary>
    ConnectionStatistics Statistics { get; }

    /// <summary>
    /// 连接标签
    /// </summary>
    Dictionary<string, string> Tags { get; }

    /// <summary>
    /// 连接状态变更事件
    /// </summary>
    event EventHandler<TransportStateEventArgs>? ConnectionStateChanged;

    /// <summary>
    /// 连接到服务器
    /// </summary>
    Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default);

    /// <summary>
    /// 断开连接
    /// </summary>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 是否已连接
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// 注册事件回调
    /// </summary>
    void RegisterEventCallback(Action<string, byte[]> callback);

    /// <summary>
    /// 发送请求
    /// </summary>
    Task<TResponse> SendRequestAsync<TRequest, TResponse>(string serviceName, string methodName, TRequest request,
        CancellationToken cancellationToken = default);

    Task<TResponse> InvokeAsync<TRequest, TResponse>(string serviceName, string methodName, in TRequest request, CancellationToken cancellationToken = default)
        => SendRequestAsync<TRequest, TResponse>(serviceName, methodName, request, cancellationToken);

    /// <summary>
    /// 发送事件
    /// </summary>
    Task SendEventAsync<T>(string hubName, string methodName, T eventData, CancellationToken cancellationToken = default);

    /// <summary>
    /// 发送消息
    /// </summary>
    Task SendAsync<T>(string hubName, string methodName, T message, CancellationToken cancellationToken = default)
        => SendEventAsync(hubName, methodName, message, cancellationToken);

    /// <summary>
    /// 订阅事件
    /// </summary>
    ISubscriptionToken SubscribeToEvent<T>(string eventName, EventHandler<T> handler);
}
