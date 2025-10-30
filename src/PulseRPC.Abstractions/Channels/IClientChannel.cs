using System.Buffers;
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
/// - 零拷贝优化：为源生成器提供高性能零拷贝路径
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

    // ============================================================================
    // 零拷贝优化路径 (为源生成器设计)
    // ============================================================================

    /// <summary>
    /// [Request/Response] 发送请求并等待响应 - 零拷贝路径
    /// 源生成器专用：传入已序列化的请求载荷，返回原始响应字节供反序列化
    /// </summary>
    /// <param name="serviceName">服务名称</param>
    /// <param name="methodName">方法名称</param>
    /// <param name="serializedRequest">已通过 MemoryPack 序列化的请求载荷</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>原始响应字节流（待反序列化）</returns>
    ValueTask<ReadOnlyMemory<byte>> InvokeRawAsync(
        string serviceName,
        string methodName,
        ReadOnlyMemory<byte> serializedRequest,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// [Command/OneWay] 发送命令不等待响应 - 零拷贝路径
    /// 源生成器专用：用于单向消息，无需等待服务器响应
    /// </summary>
    /// <param name="serviceName">服务名称</param>
    /// <param name="methodName">方法名称</param>
    /// <param name="serializedCommand">已通过 MemoryPack 序列化的命令载荷</param>
    /// <param name="cancellationToken">取消令牌</param>
    ValueTask SendCommandAsync(
        string serviceName,
        string methodName,
        ReadOnlyMemory<byte> serializedCommand,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// [Server Sent Event] 注册事件接收处理器 - 零拷贝路径
    /// 源生成器专用：注册原始字节流处理委托，由生成器负责反序列化
    /// </summary>
    /// <param name="eventName">事件名称（通常为 ServiceName.MethodName）</param>
    /// <param name="deserializeAndInvoke">反序列化+调用委托（由源生成器生成）</param>
    /// <returns>订阅令牌，用于取消订阅</returns>
    ISubscriptionToken RegisterEventHandler(
        string eventName,
        Action<ReadOnlyMemory<byte>> deserializeAndInvoke);

    /// <summary>
    /// 租借序列化缓冲区 - 支持零拷贝序列化
    /// 源生成器专用：租借内存池缓冲区用于序列化，避免临时分配
    /// </summary>
    /// <param name="estimatedSize">预估大小（字节）</param>
    /// <returns>可写入的缓冲区</returns>
    IBufferWriter<byte> RentSerializationBuffer(int estimatedSize = 256);

    /// <summary>
    /// 归还序列化缓冲区到对象池
    /// 源生成器专用：使用完毕后归还缓冲区以供复用
    /// </summary>
    /// <param name="buffer">租借的缓冲区</param>
    void ReturnSerializationBuffer(IBufferWriter<byte> buffer);

    // ============================================================================
    // 向后兼容的泛型方法 (保留现有功能)
    // ============================================================================

    /// <summary>
    /// 注册事件回调
    /// </summary>
    void RegisterEventCallback(Action<string, byte[]> callback);

    /// <summary>
    /// 发送请求（泛型版本，运行时序列化）
    /// 注意：源生成器应优先使用 InvokeRawAsync 以获得更好性能
    /// </summary>
    ValueTask<TResponse> InvokeAsync<TRequest, TResponse>(string serviceName, string methodName, TRequest request, CancellationToken cancellationToken = default);

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
