using System.Threading;
using System.Threading.Tasks;
using PulseRPC;
using PulseRPC.Benchmark.Shared.Models;

namespace PulseRPC.Benchmark.Shared
{
    /// <summary>
    /// 基准测试服务接口
    /// 提供各种性能测试场景的RPC服务方法
    /// </summary>
    public interface IBenchmarkService : IPulseHub
    {
        /// <summary>
        /// Echo测试 - 单次延迟测试
        /// 发送消息并等待服务端回显，用于测量往返延迟
        /// </summary>
        /// <param name="request">Echo请求</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>Echo响应</returns>
        Task<EchoResponse> EchoAsync(EchoRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Ping测试 - Ping-Pong延迟测试
        /// 连续发送Ping请求，测量网络延迟和抖动
        /// </summary>
        /// <param name="request">Ping请求</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>Ping响应</returns>
        Task<PingResponse> PingAsync(PingRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// 吞吐量测试 - 批量消息处理性能测试
        /// 发送大批量消息，测量系统的吞吐量和处理能力
        /// </summary>
        /// <param name="request">吞吐量测试请求</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>吞吐量测试响应</returns>
        Task<ThroughputTestResponse> ThroughputTestAsync(ThroughputTestRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// 流测试 - 流式数据传输性能测试
        /// 测试大文件或连续数据流的传输性能
        /// </summary>
        /// <param name="request">流测试请求</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>流测试响应</returns>
        Task<StreamTestResponse> StreamTestAsync(StreamTestRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取服务器信息
        /// 返回服务器的配置、状态和系统信息
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>服务器信息响应</returns>
        Task<ServerInfoResponse> GetServerInfoAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 健康检查
        /// 快速检查服务的可用性
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>健康状态</returns>
        Task<string> HealthCheckAsync(CancellationToken cancellationToken = default);
    }
}
