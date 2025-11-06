using System;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC.Server.Routing;

/// <summary>
/// 服务路由表接口 - 由源生成器生成的 ServiceRoutingTable 实现
/// 使用 Protocol ID 进行高性能路由 - 零字符串分配
/// </summary>
public interface IServiceRoutingTable
{
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
}
