using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace PulseRPC.Server.Engine;

/// <summary>
/// 编译时消息分发器的抽象基类
/// </summary>
public abstract class AbstractCompiledMessageDispatcher
{
    /// <summary>
    /// 初始化服务实例 - 由生成的实现调用
    /// </summary>
    public abstract void InitializeServices(IServiceProvider serviceProvider);

    /// <summary>
    /// 注册消息处理器到静态分发器 - 由生成的实现调用
    /// </summary>
    public abstract void RegisterHandlers(IStaticMessageDispatcher dispatcher);

    /// <summary>
    /// 直接从字节流分发消息 - 零拷贝高性能路径
    /// </summary>
    public abstract ValueTask<object?> DispatchFromBytesAsync(
        string serviceName,
        string methodName,
        ReadOnlyMemory<byte> payloadData,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 分发已反序列化的消息对象
    /// </summary>
    public abstract ValueTask<object?> DispatchAsync(
        object message,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取分发器统计信息
    /// </summary>
    public abstract object GetStatistics();

    /// <summary>
    /// 检查是否已正确初始化
    /// </summary>
    public abstract bool IsInitialized { get; }

    /// <summary>
    /// 尝试获取指定方法的处理器元数据
    /// 由生成的实现提供，用于运行时反序列化和调度
    /// </summary>
    public abstract bool TryGetHandlerMetadata(string serviceName, string methodName, [NotNullWhen(true)] out HandlerMetadata? metadata);

    /// <summary>
    /// 高性能路由分发方法 - 直接调用编译时生成的路由表
    /// 此方法由生成的 CompiledMessageDispatcher 实现，委托给 ServiceRoutingTable.RouteAsync
    /// </summary>
    /// <param name="serviceName">服务名称</param>
    /// <param name="methodName">方法名称</param>
    /// <param name="payloadData">消息负载数据</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>方法调用结果</returns>
    public abstract ValueTask<object?> RouteAsync(
        string serviceName,
        string methodName,
        ReadOnlyMemory<byte> payloadData,
        CancellationToken cancellationToken = default);
}
