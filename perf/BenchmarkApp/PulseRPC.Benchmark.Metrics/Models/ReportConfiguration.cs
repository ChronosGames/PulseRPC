using System.Collections.Generic;

namespace PulseRPC.Benchmark.Metrics.Models;

/// <summary>
/// 报告配置
/// </summary>
public class ReportConfiguration
{
    /// <summary>
    /// 输出格式
    /// </summary>
    public ReportFormat Format { get; set; } = ReportFormat.Html;

    /// <summary>
    /// 输出文件路径
    /// </summary>
    public string OutputPath { get; set; } = string.Empty;

    /// <summary>
    /// 报告标题
    /// </summary>
    public string Title { get; set; } = "PulseRPC 基准测试报告";

    /// <summary>
    /// 是否包含图表
    /// </summary>
    public bool IncludeCharts { get; set; } = true;

    /// <summary>
    /// 报告主题
    /// </summary>
    public ReportTheme Theme { get; set; } = ReportTheme.Default;

    /// <summary>
    /// 自定义模板路径
    /// </summary>
    public string? CustomTemplatePath { get; set; }

    /// <summary>
    /// 是否包含详细数据
    /// </summary>
    public bool IncludeDetailedData { get; set; } = true;

    /// <summary>
    /// 是否包含环境信息
    /// </summary>
    public bool IncludeEnvironmentInfo { get; set; } = true;

    /// <summary>
    /// 是否包含错误详情
    /// </summary>
    public bool IncludeErrorDetails { get; set; } = true;

    /// <summary>
    /// 图表配置
    /// </summary>
    public ChartConfiguration Charts { get; set; } = new();

    /// <summary>
    /// 要包含的章节
    /// </summary>
    public List<ReportSectionType> IncludedSections { get; set; } = new()
    {
        ReportSectionType.Summary,
        ReportSectionType.Configuration,
        ReportSectionType.Environment,
        ReportSectionType.Performance,
        ReportSectionType.Charts,
        ReportSectionType.Errors,
        ReportSectionType.Recommendations
    };

    /// <summary>
    /// 自定义CSS样式
    /// </summary>
    public string? CustomCss { get; set; }

    /// <summary>
    /// 自定义JavaScript
    /// </summary>
    public string? CustomJs { get; set; }

    /// <summary>
    /// 语言设置
    /// </summary>
    public string Language { get; set; } = "zh-CN";

    /// <summary>
    /// 时区设置
    /// </summary>
    public string TimeZone { get; set; } = "Asia/Shanghai";
}

/// <summary>
/// 报告格式
/// </summary>
public enum ReportFormat
{
    /// <summary>
    /// HTML格式
    /// </summary>
    Html,

    /// <summary>
    /// JSON格式
    /// </summary>
    Json,

    /// <summary>
    /// CSV格式
    /// </summary>
    Csv,

    /// <summary>
    /// Markdown格式
    /// </summary>
    Markdown,

    /// <summary>
    /// PDF格式
    /// </summary>
    Pdf
}

/// <summary>
/// 报告主题
/// </summary>
public enum ReportTheme
{
    /// <summary>
    /// 默认主题
    /// </summary>
    Default,

    /// <summary>
    /// 深色主题
    /// </summary>
    Dark,

    /// <summary>
    /// 简约主题
    /// </summary>
    Minimal,

    /// <summary>
    /// 企业主题
    /// </summary>
    Enterprise
}

/// <summary>
/// 图表配置
/// </summary>
public class ChartConfiguration
{
    /// <summary>
    /// 图表宽度
    /// </summary>
    public int Width { get; set; } = 800;

    /// <summary>
    /// 图表高度
    /// </summary>
    public int Height { get; set; } = 400;

    /// <summary>
    /// 是否启用动画
    /// </summary>
    public bool EnableAnimation { get; set; } = true;

    /// <summary>
    /// 延迟图表配置
    /// </summary>
    public LatencyChartConfig LatencyChart { get; set; } = new();

    /// <summary>
    /// 吞吐量图表配置
    /// </summary>
    public ThroughputChartConfig ThroughputChart { get; set; } = new();

    /// <summary>
    /// 资源图表配置
    /// </summary>
    public ResourceChartConfig ResourceChart { get; set; } = new();
}

/// <summary>
/// 延迟图表配置
/// </summary>
public class LatencyChartConfig
{
    /// <summary>
    /// 是否显示百分位线
    /// </summary>
    public bool ShowPercentileLines { get; set; } = true;

    /// <summary>
    /// 是否显示分布直方图
    /// </summary>
    public bool ShowDistribution { get; set; } = true;

    /// <summary>
    /// 延迟阈值线（毫秒）
    /// </summary>
    public List<double> ThresholdLines { get; set; } = new() { 10, 50, 100, 500 };
}

/// <summary>
/// 吞吐量图表配置
/// </summary>
public class ThroughputChartConfig
{
    /// <summary>
    /// 是否显示目标线
    /// </summary>
    public bool ShowTargetLine { get; set; } = true;

    /// <summary>
    /// 目标RPS值
    /// </summary>
    public double? TargetRps { get; set; }

    /// <summary>
    /// 是否显示移动平均线
    /// </summary>
    public bool ShowMovingAverage { get; set; } = true;
}

/// <summary>
/// 资源图表配置
/// </summary>
public class ResourceChartConfig
{
    /// <summary>
    /// 是否显示CPU图表
    /// </summary>
    public bool ShowCpuChart { get; set; } = true;

    /// <summary>
    /// 是否显示内存图表
    /// </summary>
    public bool ShowMemoryChart { get; set; } = true;

    /// <summary>
    /// 是否显示网络图表
    /// </summary>
    public bool ShowNetworkChart { get; set; } = true;

    /// <summary>
    /// CPU警告阈值（%）
    /// </summary>
    public double CpuWarningThreshold { get; set; } = 80.0;

    /// <summary>
    /// 内存警告阈值（%）
    /// </summary>
    public double MemoryWarningThreshold { get; set; } = 80.0;
} 