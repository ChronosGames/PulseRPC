using System;
using System.Collections.Generic;

namespace PulseRPC.Benchmark.Metrics.Models;

/// <summary>
/// 基准测试报告数据模型
/// </summary>
public class BenchmarkReportData
{
    /// <summary>
    /// 测试配置信息
    /// </summary>
    public TestConfiguration TestConfig { get; set; } = new();

    /// <summary>
    /// 环境信息
    /// </summary>
    public EnvironmentInfo Environment { get; set; } = new();

    /// <summary>
    /// 性能指标数据
    /// </summary>
    public PerformanceMetrics Metrics { get; set; } = new();

    /// <summary>
    /// 报告章节列表
    /// </summary>
    public List<ReportSection> Sections { get; set; } = new();

    /// <summary>
    /// 报告生成时间
    /// </summary>
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 报告版本
    /// </summary>
    public string ReportVersion { get; set; } = "1.0.0";

    /// <summary>
    /// 测试摘要
    /// </summary>
    public TestSummary Summary { get; set; } = new();
}

/// <summary>
/// 测试配置信息
/// </summary>
public class TestConfiguration
{
    /// <summary>
    /// 服务器地址
    /// </summary>
    public string ServerAddress { get; set; } = string.Empty;

    /// <summary>
    /// 测试场景名称
    /// </summary>
    public string ScenarioName { get; set; } = string.Empty;

    /// <summary>
    /// 测试持续时间（秒）
    /// </summary>
    public int DurationSeconds { get; set; }

    /// <summary>
    /// 并发连接数
    /// </summary>
    public int ConnectionCount { get; set; }

    /// <summary>
    /// 请求速率（QPS）
    /// </summary>
    public int RequestRate { get; set; }

    /// <summary>
    /// 预热时间（秒）
    /// </summary>
    public int WarmupSeconds { get; set; }

    /// <summary>
    /// 测试开始时间
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// 测试结束时间
    /// </summary>
    public DateTime EndTime { get; set; }
}

/// <summary>
/// 环境信息
/// </summary>
public class EnvironmentInfo
{
    /// <summary>
    /// 操作系统信息
    /// </summary>
    public string OperatingSystem { get; set; } = string.Empty;

    /// <summary>
    /// .NET运行时版本
    /// </summary>
    public string DotNetVersion { get; set; } = string.Empty;

    /// <summary>
    /// 处理器信息
    /// </summary>
    public string ProcessorInfo { get; set; } = string.Empty;

    /// <summary>
    /// 内存总量（MB）
    /// </summary>
    public long TotalMemoryMB { get; set; }

    /// <summary>
    /// 网络配置信息
    /// </summary>
    public string NetworkConfig { get; set; } = string.Empty;

    /// <summary>
    /// 机器名称
    /// </summary>
    public string MachineName { get; set; } = string.Empty;
}

/// <summary>
/// 性能指标数据
/// </summary>
public class PerformanceMetrics
{
    /// <summary>
    /// 延迟指标
    /// </summary>
    public LatencyMetrics Latency { get; set; } = new();

    /// <summary>
    /// 吞吐量指标
    /// </summary>
    public ThroughputMetrics Throughput { get; set; } = new();

    /// <summary>
    /// 资源使用指标
    /// </summary>
    public ResourceMetrics Resources { get; set; } = new();

    /// <summary>
    /// 错误统计
    /// </summary>
    public ErrorMetrics Errors { get; set; } = new();
}

/// <summary>
/// 延迟指标
/// </summary>
public class LatencyMetrics
{
    /// <summary>
    /// 平均延迟（毫秒）
    /// </summary>
    public double AverageMs { get; set; }

    /// <summary>
    /// 最小延迟（毫秒）
    /// </summary>
    public double MinMs { get; set; }

    /// <summary>
    /// 最大延迟（毫秒）
    /// </summary>
    public double MaxMs { get; set; }

    /// <summary>
    /// P50延迟（毫秒）
    /// </summary>
    public double P50Ms { get; set; }

    /// <summary>
    /// P95延迟（毫秒）
    /// </summary>
    public double P95Ms { get; set; }

    /// <summary>
    /// P99延迟（毫秒）
    /// </summary>
    public double P99Ms { get; set; }

    /// <summary>
    /// P99.9延迟（毫秒）
    /// </summary>
    public double P999Ms { get; set; }

    /// <summary>
    /// 标准差
    /// </summary>
    public double StandardDeviation { get; set; }

    /// <summary>
    /// 延迟分布数据
    /// </summary>
    public List<LatencyPoint> Distribution { get; set; } = new();
}

/// <summary>
/// 延迟分布点
/// </summary>
public class LatencyPoint
{
    /// <summary>
    /// 时间戳
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// 延迟值（毫秒）
    /// </summary>
    public double LatencyMs { get; set; }
}

/// <summary>
/// 吞吐量指标
/// </summary>
public class ThroughputMetrics
{
    /// <summary>
    /// 平均RPS
    /// </summary>
    public double AverageRps { get; set; }

    /// <summary>
    /// 峰值RPS
    /// </summary>
    public double PeakRps { get; set; }

    /// <summary>
    /// 总请求数
    /// </summary>
    public long TotalRequests { get; set; }

    /// <summary>
    /// 成功请求数
    /// </summary>
    public long SuccessfulRequests { get; set; }

    /// <summary>
    /// 失败请求数
    /// </summary>
    public long FailedRequests { get; set; }

    /// <summary>
    /// 吞吐量时间序列数据
    /// </summary>
    public List<ThroughputPoint> TimeSeries { get; set; } = new();
}

/// <summary>
/// 吞吐量时间点
/// </summary>
public class ThroughputPoint
{
    /// <summary>
    /// 时间戳
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// RPS值
    /// </summary>
    public double Rps { get; set; }
}

/// <summary>
/// 资源使用指标
/// </summary>
public class ResourceMetrics
{
    /// <summary>
    /// CPU使用率（%）
    /// </summary>
    public double CpuUsagePercent { get; set; }

    /// <summary>
    /// 内存使用量（MB）
    /// </summary>
    public long MemoryUsageMB { get; set; }

    /// <summary>
    /// 网络发送量（Bytes）
    /// </summary>
    public long NetworkSentBytes { get; set; }

    /// <summary>
    /// 网络接收量（Bytes）
    /// </summary>
    public long NetworkReceivedBytes { get; set; }

    /// <summary>
    /// 资源使用时间序列数据
    /// </summary>
    public List<ResourcePoint> TimeSeries { get; set; } = new();
}

/// <summary>
/// 资源使用时间点
/// </summary>
public class ResourcePoint
{
    /// <summary>
    /// 时间戳
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// CPU使用率
    /// </summary>
    public double CpuPercent { get; set; }

    /// <summary>
    /// 内存使用量
    /// </summary>
    public long MemoryMB { get; set; }
}

/// <summary>
/// 错误统计
/// </summary>
public class ErrorMetrics
{
    /// <summary>
    /// 总错误数
    /// </summary>
    public long TotalErrors { get; set; }

    /// <summary>
    /// 错误率（%）
    /// </summary>
    public double ErrorRate { get; set; }

    /// <summary>
    /// 错误类型分布
    /// </summary>
    public Dictionary<string, long> ErrorsByType { get; set; } = new();

    /// <summary>
    /// 超时错误数
    /// </summary>
    public long TimeoutErrors { get; set; }

    /// <summary>
    /// 连接错误数
    /// </summary>
    public long ConnectionErrors { get; set; }
}

/// <summary>
/// 测试摘要
/// </summary>
public class TestSummary
{
    /// <summary>
    /// 测试是否成功
    /// </summary>
    public bool IsSuccessful { get; set; }

    /// <summary>
    /// 性能等级
    /// </summary>
    public PerformanceGrade Grade { get; set; }

    /// <summary>
    /// 主要发现
    /// </summary>
    public List<string> KeyFindings { get; set; } = new();

    /// <summary>
    /// 建议
    /// </summary>
    public List<string> Recommendations { get; set; } = new();
}

/// <summary>
/// 性能等级
/// </summary>
public enum PerformanceGrade
{
    /// <summary>
    /// 优秀
    /// </summary>
    Excellent,

    /// <summary>
    /// 良好
    /// </summary>
    Good,

    /// <summary>
    /// 普通
    /// </summary>
    Average,

    /// <summary>
    /// 需要改进
    /// </summary>
    NeedsImprovement,

    /// <summary>
    /// 较差
    /// </summary>
    Poor
} 