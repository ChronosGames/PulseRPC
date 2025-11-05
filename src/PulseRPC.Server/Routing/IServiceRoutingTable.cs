using System;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC.Server.Routing;

/// <summary>
/// 服务路由表接口 - 由源生成器生成的 ServiceRoutingTable 实现
/// </summary>
public interface IServiceRoutingTable
{
    #region 基于协议号的路由 (推荐 - 高性能)

    /// <summary>
    /// [推荐] 基于协议号的超快速路由 - 零字符串分配
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

    #endregion

    #region 基于方法名的路由 (向后兼容 - 保留用于调试)

    /// <summary>
    /// 高性能服务路由方法（基于服务名+方法名）
    /// </summary>
    ValueTask<object?> RouteAsync(
        IServiceProvider serviceProvider,
        string serviceName,
        string methodName,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 基于哈希的超快速路由
    /// </summary>
    ValueTask<object?> RouteByHashAsync(
        IServiceProvider serviceProvider,
        uint serviceNameHash,
        string methodName,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default);

    #endregion
}
