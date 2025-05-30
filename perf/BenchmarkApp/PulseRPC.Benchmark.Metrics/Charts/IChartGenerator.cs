using PulseRPC.Benchmark.Metrics.Models;

namespace PulseRPC.Benchmark.Metrics.Charts;

/// <summary>
/// 图表生成器接口
/// </summary>
public interface IChartGenerator
{
    /// <summary>
    /// 生成延迟图表
    /// </summary>
    /// <param name="metrics">延迟指标数据</param>
    /// <param name="config">图表配置</param>
    /// <returns>图表HTML内容</returns>
    string GenerateLatencyChart(LatencyMetrics metrics, LatencyChartConfig config);

    /// <summary>
    /// 生成吞吐量图表
    /// </summary>
    /// <param name="metrics">吞吐量指标数据</param>
    /// <param name="config">图表配置</param>
    /// <returns>图表HTML内容</returns>
    string GenerateThroughputChart(ThroughputMetrics metrics, ThroughputChartConfig config);

    /// <summary>
    /// 生成资源使用图表
    /// </summary>
    /// <param name="metrics">资源使用指标数据</param>
    /// <param name="config">图表配置</param>
    /// <returns>图表HTML内容</returns>
    string GenerateResourceChart(ResourceMetrics metrics, ResourceChartConfig config);

    /// <summary>
    /// 生成延迟分布直方图
    /// </summary>
    /// <param name="metrics">延迟指标数据</param>
    /// <param name="config">图表配置</param>
    /// <returns>图表HTML内容</returns>
    string GenerateLatencyHistogram(LatencyMetrics metrics, LatencyChartConfig config);

    /// <summary>
    /// 生成组合图表脚本
    /// </summary>
    /// <param name="performanceMetrics">性能指标数据</param>
    /// <param name="chartConfig">图表配置</param>
    /// <returns>JavaScript脚本</returns>
    string GenerateChartScripts(PerformanceMetrics performanceMetrics, ChartConfiguration chartConfig);

    /// <summary>
    /// 验证图表配置
    /// </summary>
    /// <param name="config">图表配置</param>
    /// <returns>验证结果</returns>
    ChartValidationResult ValidateConfiguration(ChartConfiguration config);
}

/// <summary>
/// 图表验证结果
/// </summary>
public class ChartValidationResult
{
    /// <summary>
    /// 是否有效
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// 错误消息列表
    /// </summary>
    public List<string> Errors { get; set; } = new();

    /// <summary>
    /// 警告消息列表
    /// </summary>
    public List<string> Warnings { get; set; } = new();
} 