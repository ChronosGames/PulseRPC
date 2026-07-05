using System.Net;

namespace PulseRPC.Channels;

/// <summary>
/// 传输通道接口 - 支持双向 RPC 的统一通道抽象
/// </summary>
/// <remarks>
/// 此接口定义了传输通道的核心能力：
/// - 基础通信：发送/接收数据
/// - 双向RPC：既能作为客户端调用远程Hub，也能注册Hub响应对方的调用
/// - 生命周期：连接状态管理和资源释放
/// </remarks>
public interface ITransportChannel : IDisposable
{
    /// <summary>
    /// 连接唯一标识符
    /// </summary>
    string ConnectionId { get; }

    /// <summary>
    /// 是否已连接
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// 远程端点信息
    /// </summary>
    EndPoint? RemoteEndPoint { get; }

    /// <summary>
    /// 本地端点信息
    /// </summary>
    EndPoint? LocalEndPoint { get; }

    /// <summary>
    /// 连接建立时间
    /// </summary>
    DateTime ConnectedAt { get; }

    /// <summary>
    /// 最后活动时间
    /// </summary>
    DateTime LastActivityAt { get; }

    /// <summary>
    /// 发送数据
    /// </summary>
    /// <param name="data">要发送的数据</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否发送成功</returns>
    Task<bool> SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

    // === 双向 RPC 支持（第一阶段实现）===
    // 以下方法将在第一阶段实现 TransportChannelBase 时提供默认实现

    // 注意：GetHubAsync 和 RegisterHub 方法由 PulseRPC.Client.SourceGenerator 生成为扩展方法
    // 不在接口中定义，以便通过代码生成器支持

    /// <summary>
    /// 注册传输请求处理器（底层API，由代码生成器调用）
    /// </summary>
    /// <param name="serviceName">服务名称</param>
    /// <param name="handler">传输请求处理器</param>
    /// <returns>注册令牌</returns>
    IDisposable RegisterTransportRequestHandler(string serviceName, ITransportRequestHandler handler);
}

/// <summary>
/// 传输请求处理器接口 - 处理远程调用请求
/// </summary>
public interface ITransportRequestHandler
{
    /// <summary>
    /// 处理远程方法调用请求
    /// </summary>
    /// <param name="methodName">方法名称</param>
    /// <param name="parameters">序列化的参数</param>
    /// <param name="context">请求上下文</param>
    /// <returns>执行结果（可能为 null）</returns>
    Task<object?> HandleRequestAsync(
        string methodName,
        ReadOnlyMemory<byte> parameters,
        IRequestContext context);
}

/// <summary>
/// 请求上下文接口 - 提供请求相关的上下文信息
/// </summary>
public interface IRequestContext
{
    /// <summary>
    /// 请求ID
    /// </summary>
    Guid RequestId { get; }

    /// <summary>
    /// 连接ID
    /// </summary>
    string ConnectionId { get; }

    /// <summary>
    /// 取消令牌
    /// </summary>
    CancellationToken CancellationToken { get; }

    /// <summary>
    /// 获取上下文属性
    /// </summary>
    object? GetProperty(string key);

    /// <summary>
    /// 设置上下文属性
    /// </summary>
    void SetProperty(string key, object? value);
}

/// <summary>
/// Hub 注册令牌 - 用于管理 Hub 的注册生命周期
/// </summary>
public interface IHubRegistrationToken : IDisposable
{
    /// <summary>
    /// Hub 接口类型
    /// </summary>
    Type HubType { get; }

    /// <summary>
    /// Channel 名称（对应 ChannelAttribute）
    /// </summary>
    string ChannelName { get; }

    /// <summary>
    /// 是否已取消注册
    /// </summary>
    bool IsUnregistered { get; }

    /// <summary>
    /// 取消注册
    /// </summary>
    void Unregister();
}
