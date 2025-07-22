using MemoryPack;

namespace PulseRPC.Benchmark.Shared.Models
{
    /// <summary>
    /// 高性能优化的Ping请求模型 - 最小化字段以减少序列化开销
    /// </summary>
    [MemoryPackable]
    public partial class OptimizedPingRequest
    {
        /// <summary>
        /// 请求ID - 仅保留必要字段
        /// </summary>
        public long RequestId { get; set; }

        /// <summary>
        /// 序列号
        /// </summary>
        public int SequenceNumber { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        public OptimizedPingRequest()
        {
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="requestId">请求ID</param>
        /// <param name="sequenceNumber">序列号</param>
        [MemoryPackConstructor]
        public OptimizedPingRequest(long requestId, int sequenceNumber)
        {
            RequestId = requestId;
            SequenceNumber = sequenceNumber;
        }
    }

    /// <summary>
    /// 高性能优化的Ping响应模型 - 最小化字段以减少序列化开销
    /// </summary>
    [MemoryPackable]
    public partial class OptimizedPingResponse
    {
        /// <summary>
        /// 对应的请求ID
        /// </summary>
        public long RequestId { get; set; }

        /// <summary>
        /// 对应的序列号
        /// </summary>
        public int SequenceNumber { get; set; }

        /// <summary>
        /// 处理是否成功
        /// </summary>
        public bool Success { get; set; } = true;

        /// <summary>
        /// 构造函数
        /// </summary>
        public OptimizedPingResponse()
        {
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="requestId">请求ID</param>
        /// <param name="sequenceNumber">序列号</param>
        /// <param name="success">是否成功</param>
        [MemoryPackConstructor]
        public OptimizedPingResponse(long requestId, int sequenceNumber, bool success = true)
        {
            RequestId = requestId;
            SequenceNumber = sequenceNumber;
            Success = success;
        }
    }

    /// <summary>
    /// 高性能基准测试服务接口 - 简化版本
    /// </summary>
    public interface IOptimizedBenchmarkService
    {
        /// <summary>
        /// 高性能Ping测试
        /// </summary>
        /// <param name="request">优化的Ping请求</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>优化的Ping响应</returns>
        Task<OptimizedPingResponse> OptimizedPingAsync(OptimizedPingRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// 原生性能测试 - 直接传递基本类型
        /// </summary>
        /// <param name="sequenceNumber">序列号</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>序列号</returns>
        Task<int> NativePingAsync(int sequenceNumber, CancellationToken cancellationToken = default);
    }
}