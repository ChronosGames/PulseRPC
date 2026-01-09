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
    [Channel("TcpChannel")]
    public interface IBenchmarkHub : IPulseHub
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
        Task<PingResponse> PingAsync(PingRequest request);

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

        #region Quick Benchmark Methods

        /// <summary>
        /// Notify测试 - 无返回值的单向消息测试（Fire-and-Forget）
        /// 用于测试服务端的消息处理吞吐量，不需要等待响应
        /// </summary>
        /// <param name="request">Notify请求</param>
        /// <returns>无返回值</returns>
        ValueTask NotifyAsync(NotifyRequest request);

        /// <summary>
        /// 上行测试 - 客户端发送大数据到服务端
        /// 用于测试上行带宽和处理能力
        /// </summary>
        /// <param name="request">上行请求（包含大数据负载）</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>上行响应（确认接收字节数）</returns>
        Task<UploadResponse> UploadAsync(UploadRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// 下行测试 - 服务端返回大数据到客户端
        /// 用于测试下行带宽
        /// </summary>
        /// <param name="request">下行请求（指定需要的数据大小）</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>下行响应（包含大数据负载）</returns>
        Task<DownloadResponse> DownloadAsync(DownloadRequest request, CancellationToken cancellationToken = default);

        #endregion
    }
}
