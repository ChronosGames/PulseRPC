using System;

namespace PulseRPC.Benchmark.Core.Models;

/// <summary>
/// 基准测试进度信息
/// 用于报告测试场景的执行进度
/// </summary>
public class BenchmarkProgress
{
    /// <summary>
    /// 总迭代次数
    /// </summary>
    public int TotalIterations { get; set; }

    /// <summary>
    /// 当前完成的迭代次数
    /// </summary>
    public int CurrentIteration { get; set; }

    /// <summary>
    /// 进度百分比 (0-100)
    /// </summary>
    public double ProgressPercentage => TotalIterations > 0 ? (double)CurrentIteration / TotalIterations * 100 : 0;

    /// <summary>
    /// 已经运行的时间
    /// </summary>
    public TimeSpan ElapsedTime { get; set; }

    /// <summary>
    /// 预计剩余时间
    /// </summary>
    public TimeSpan? EstimatedTimeRemaining { get; set; }

    /// <summary>
    /// 当前操作描述
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// 当前吞吐量 (QPS)
    /// </summary>
    public double? CurrentThroughput { get; set; }

    /// <summary>
    /// 平均延迟 (毫秒)
    /// </summary>
    public double? AverageLatencyMs { get; set; }

    /// <summary>
    /// 错误计数
    /// </summary>
    public int ErrorCount { get; set; }

    /// <summary>
    /// 成功计数
    /// </summary>
    public int SuccessCount { get; set; }

    /// <summary>
    /// 创建时间戳
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 测试阶段标识
    /// </summary>
    public string Phase { get; set; } = string.Empty;

    /// <summary>
    /// 创建进度报告
    /// </summary>
    /// <param name="current">当前进度</param>
    /// <param name="total">总数</param>
    /// <param name="message">描述信息</param>
    /// <param name="elapsed">已用时间</param>
    /// <returns>进度实例</returns>
    public static BenchmarkProgress Create(int current, int total, string message, TimeSpan elapsed)
    {
        var progress = new BenchmarkProgress
        {
            CurrentIteration = current,
            TotalIterations = total,
            Message = message,
            ElapsedTime = elapsed
        };

        // 计算预计剩余时间
        if (current > 0 && current < total)
        {
            var avgTimePerItem = elapsed.TotalMilliseconds / current;
            var remainingItems = total - current;
            progress.EstimatedTimeRemaining = TimeSpan.FromMilliseconds(avgTimePerItem * remainingItems);
        }

        return progress;
    }

    /// <summary>
    /// 创建简单进度报告
    /// </summary>
    /// <param name="current">当前进度</param>
    /// <param name="total">总数</param>
    /// <param name="message">描述信息</param>
    /// <returns>进度实例</returns>
    public static BenchmarkProgress Create(int current, int total, string message = "")
    {
        return new BenchmarkProgress
        {
            CurrentIteration = current,
            TotalIterations = total,
            Message = message
        };
    }

    /// <summary>
    /// 格式化进度信息为字符串
    /// </summary>
    /// <returns>格式化的进度字符串</returns>
    public override string ToString()
    {
        var percentage = $"{ProgressPercentage:F1}%";
        var progress = $"{CurrentIteration}/{TotalIterations}";
        var elapsed = $"耗时:{ElapsedTime:mm\\:ss}";

        var result = $"{progress} ({percentage}) {elapsed}";

        if (!string.IsNullOrEmpty(Message))
        {
            result += $" - {Message}";
        }

        if (EstimatedTimeRemaining.HasValue)
        {
            result += $" (预计剩余:{EstimatedTimeRemaining.Value:mm\\:ss})";
        }

        return result;
    }
}
