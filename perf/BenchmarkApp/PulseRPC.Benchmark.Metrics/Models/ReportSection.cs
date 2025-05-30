using System.Collections.Generic;

namespace PulseRPC.Benchmark.Metrics.Models;

/// <summary>
/// 报告章节
/// </summary>
public class ReportSection
{
    /// <summary>
    /// 章节类型
    /// </summary>
    public ReportSectionType Type { get; set; }

    /// <summary>
    /// 章节标题
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 章节内容
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 章节数据
    /// </summary>
    public Dictionary<string, object> Data { get; set; } = new();

    /// <summary>
    /// 章节优先级（用于排序）
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    /// 是否可见
    /// </summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>
    /// 子章节
    /// </summary>
    public List<ReportSection> SubSections { get; set; } = new();
}

/// <summary>
/// 报告章节类型
/// </summary>
public enum ReportSectionType
{
    /// <summary>
    /// 摘要
    /// </summary>
    Summary,

    /// <summary>
    /// 配置信息
    /// </summary>
    Configuration,

    /// <summary>
    /// 环境信息
    /// </summary>
    Environment,

    /// <summary>
    /// 性能指标
    /// </summary>
    Performance,

    /// <summary>
    /// 图表
    /// </summary>
    Charts,

    /// <summary>
    /// 错误信息
    /// </summary>
    Errors,

    /// <summary>
    /// 建议
    /// </summary>
    Recommendations,

    /// <summary>
    /// 详细数据
    /// </summary>
    DetailedData,

    /// <summary>
    /// 自定义章节
    /// </summary>
    Custom
} 