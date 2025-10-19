using System;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC.Server.Routing;

/// <summary>
/// 服务路由表接口 - 由源生成器生成的 ServiceRoutingTable 实现
/// </summary>
public interface IServiceRoutingTable
{
    /// <summary>
    /// 高性能服务路由方法
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
}
