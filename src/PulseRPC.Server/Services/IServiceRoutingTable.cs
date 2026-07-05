using System;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC.Server;

/// <summary>
/// 服务路由表接口 - 由源生成器生成的 ServiceRoutingTable 实现
/// 使用 Protocol ID 进行高性能路由 - 零字符串分配
/// </summary>
public interface IServiceRoutingTable
{
    /// <summary>
    /// 获取此路由表支持的全部协议号。
    /// </summary>
    ReadOnlySpan<ushort> EnumerateProtocolIds();

    /// <summary>
    /// 基于协议号的超快速路由 - 零字符串分配
    /// 通过编译时生成的协议号直接定位到方法处理器
    /// </summary>
    /// <param name="serviceProvider">服务提供者</param>
    /// <param name="protocolId">协议号（由源生成器自动分配）</param>
    /// <param name="data">已序列化的请求数据</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>方法执行结果</returns>
    ValueTask<object?> RouteByProtocolIdAsync(
        IServiceProvider serviceProvider,
        ushort protocolId,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 基于协议号 + 可选实例键的路由（支持 (Hub,Key) 入站路由，§P3 keyed-actor-routing）。
    /// </summary>
    /// <param name="serviceProvider">服务提供者</param>
    /// <param name="protocolId">协议号（由源生成器自动分配）</param>
    /// <param name="serviceKey">
    /// 目标服务实例键（对应 <c>MessageHeader.ServiceKey</c>）。为空字符串时保持现有 DI 单例语义，
    /// 等价于调用 <see cref="RouteByProtocolIdAsync(IServiceProvider, ushort, ReadOnlyMemory{byte}, CancellationToken)"/>；
    /// 非空时经 <c>PulseServiceManager</c> 解析/激活以 <c>(HubShortName, serviceKey)</c> 为地址的 keyed actor 实例并对其调用。
    /// </param>
    /// <param name="data">已序列化的请求数据</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>方法执行结果</returns>
    ValueTask<object?> RouteByProtocolIdAsync(
        IServiceProvider serviceProvider,
        ushort protocolId,
        string serviceKey,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default);
}
