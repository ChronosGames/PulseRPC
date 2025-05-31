using PulseRPC.Benchmark.Core.Models;

namespace PulseRPC.Benchmark.Core.Interfaces;

/// <summary>
/// 结果聚合器接口，负责收集和聚合测试结果
/// </summary>
public interface IResultAggregator
{
    /// <summary>
    /// 添加单个测试结果
    /// </summary>
    /// <param name="result">测试结果</param>
    void AddResult(BenchmarkResult result);

    /// <summary>
    /// 批量添加测试结果
    /// </summary>
    /// <param name="results">测试结果集合</param>
    void AddResults(IEnumerable<BenchmarkResult> results);

    /// <summary>
    /// 获取聚合后的结果
    /// </summary>
    /// <returns>聚合结果</returns>
    AggregatedBenchmarkResult GetAggregatedResult();

    /// <summary>
    /// 清空所有结果
    /// </summary>
    void Clear();

    /// <summary>
    /// 获取结果数量
    /// </summary>
    int ResultCount { get; }

    /// <summary>
    /// 按场景名称分组的结果
    /// </summary>
    /// <param name="scenarioName">场景名称</param>
    /// <returns>该场景的聚合结果</returns>
    AggregatedBenchmarkResult GetResultsByScenario(string scenarioName);

    /// <summary>
    /// 获取所有场景名称
    /// </summary>
    /// <returns>场景名称列表</returns>
    IEnumerable<string> GetScenarioNames();
}

/// <summary>
/// 聚合的基准测试结果
/// </summary>
public class AggregatedBenchmarkResult
{
    /// <summary>
    /// 场景名称
    /// </summary>
    public string ScenarioName { get; set; } = string.Empty;

    /// <summary>
    /// 总测试次数
    /// </summary>
    public int TotalTests { get; set; }

    /// <summary>
    /// 成功次数
    /// </summary>
    public int SuccessfulTests { get; set; }

    /// <summary>
    /// 失败次数
    /// </summary>
    public int FailedTests { get; set; }

    /// <summary>
    /// 成功率百分比
    /// </summary>
    public double SuccessRate => TotalTests > 0 ? (double)SuccessfulTests / TotalTests * 100 : 0;

    /// <summary>
    /// 平均延迟（毫秒）
    /// </summary>
    public double AverageLatencyMs { get; set; }

    /// <summary>
    /// 最小延迟（毫秒）
    /// </summary>
    public double MinLatencyMs { get; set; }

    /// <summary>
    /// 最大延迟（毫秒）
    /// </summary>
    public double MaxLatencyMs { get; set; }

    /// <summary>
    /// P50延迟（毫秒）
    /// </summary>
    public double P50LatencyMs { get; set; }

    /// <summary>
    /// P95延迟（毫秒）
    /// </summary>
    public double P95LatencyMs { get; set; }

    /// <summary>
    /// P99延迟（毫秒）
    /// </summary>
    public double P99LatencyMs { get; set; }

    /// <summary>
    /// 总吞吐量（每秒操作数）
    /// </summary>
    public double TotalThroughputOps { get; set; }

    /// <summary>
    /// 平均吞吐量（每秒操作数）
    /// </summary>
    public double AverageThroughputOps { get; set; }

    /// <summary>
    /// 总数据传输量（字节）
    /// </summary>
    public long TotalBytesTransferred { get; set; }

    /// <summary>
    /// 平均带宽（字节/秒）
    /// </summary>
    public double AverageBandwidthBps { get; set; }
}
