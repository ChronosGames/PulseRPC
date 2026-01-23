using PulseRPC.Benchmark.Contracts;
using PulseRPC.Benchmark.Models;

namespace PulseRPC.Benchmark.Scenarios;

/// <summary>
/// 测试场景接口
/// </summary>
public interface IScenario
{
    /// <summary>
    /// 场景名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 场景描述
    /// </summary>
    string Description { get; }

    /// <summary>
    /// 运行场景
    /// </summary>
    /// <param name="services">多个独立连接的服务代理列表</param>
    /// <param name="config">基准测试配置</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<BenchmarkResult> RunAsync(IReadOnlyList<IBenchmarkHub> services, BenchmarkConfig config, CancellationToken cancellationToken = default);
}
