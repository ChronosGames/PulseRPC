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
    Task<BenchmarkResult> RunAsync(IBenchmarkHub service, BenchmarkConfig config, CancellationToken cancellationToken = default);
}
