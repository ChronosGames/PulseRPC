using PulseRPC.Benchmark.Shared.Models;

namespace PulseRPC.Benchmark.Server.Services;

/// <summary>
/// 高性能优化的基准测试服务实现
/// 专门用于测量RPC框架的真实性能，移除了所有不必要的开销
/// </summary>
public class OptimizedBenchmarkServiceImpl : IOptimizedBenchmarkService
{
    // 最小化字段，仅保留基本统计
    private long _totalRequests = 0;
    private long _successfulRequests = 0;

    /// <summary>
    /// 高性能Ping实现 - 零开销版本
    /// </summary>
    public Task<OptimizedPingResponse> OptimizedPingAsync(OptimizedPingRequest request, CancellationToken cancellationToken = default)
    {
        // 原子递增统计，最小开销
        Interlocked.Increment(ref _totalRequests);
        Interlocked.Increment(ref _successfulRequests);

        // 直接返回同步任务，避免异步状态机开销
        var response = new OptimizedPingResponse(request.RequestId, request.SequenceNumber, true);
        return Task.FromResult(response);
    }

    /// <summary>
    /// 原生性能测试 - 直接使用基本类型，最小序列化开销
    /// </summary>
    public Task<int> NativePingAsync(int sequenceNumber, CancellationToken cancellationToken = default)
    {
        // 最小化实现，直接返回
        Interlocked.Increment(ref _totalRequests);
        Interlocked.Increment(ref _successfulRequests);
        
        return Task.FromResult(sequenceNumber);
    }

    /// <summary>
    /// 获取简化的统计信息
    /// </summary>
    public (long Total, long Successful) GetSimpleStats()
    {
        return (_totalRequests, _successfulRequests);
    }

    /// <summary>
    /// 重置统计信息
    /// </summary>
    public void ResetStats()
    {
        _totalRequests = 0;
        _successfulRequests = 0;
    }
}