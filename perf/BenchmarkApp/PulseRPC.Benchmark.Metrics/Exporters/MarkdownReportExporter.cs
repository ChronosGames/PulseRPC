using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PulseRPC.Benchmark.Metrics.Models;

namespace PulseRPC.Benchmark.Metrics.Exporters;

/// <summary>
/// Markdown格式报告导出器
/// </summary>
public class MarkdownReportExporter : IBenchmarkReportGenerator
{
    private readonly ILogger _logger;

    public MarkdownReportExporter(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<string> GenerateReportAsync(BenchmarkReportData data, ReportConfiguration config)
    {
        try
        {
            _logger.LogDebug("开始生成Markdown报告");

            var mdBuilder = new StringBuilder();

            // 添加标题和基本信息
            await AppendHeaderAsync(mdBuilder, data, config);

            // 添加测试摘要
            await AppendSummaryAsync(mdBuilder, data);

            // 添加测试配置
            await AppendConfigurationAsync(mdBuilder, data);

            // 添加环境信息
            await AppendEnvironmentAsync(mdBuilder, data);

            // 添加性能指标
            await AppendPerformanceMetricsAsync(mdBuilder, data);

            // 添加错误分析
            await AppendErrorAnalysisAsync(mdBuilder, data);

            // 添加建议和总结
            await AppendRecommendationsAsync(mdBuilder, data);

            var result = mdBuilder.ToString();
            _logger.LogDebug("Markdown报告生成完成，大小: {Size} 字符", result.Length);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "生成Markdown报告失败: {Message}", ex.Message);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<byte[]> GenerateReportBytesAsync(BenchmarkReportData data, ReportConfiguration config)
    {
        var content = await GenerateReportAsync(data, config);
        return Encoding.UTF8.GetBytes(content);
    }

    /// <inheritdoc />
    public async Task<string> GenerateReportToFileAsync(BenchmarkReportData data, ReportConfiguration config)
    {
        throw new NotImplementedException("此方法应由主生成器调用");
    }

    /// <inheritdoc />
    public async Task<ReportValidationResult> ValidateConfigurationAsync(ReportConfiguration config)
    {
        var result = new ReportValidationResult { IsValid = true };

        // Markdown格式没有特殊的验证要求
        await Task.CompletedTask;

        return result;
    }

    /// <inheritdoc />
    public ReportFormat[] GetSupportedFormats()
    {
        return new[] { ReportFormat.Markdown };
    }

    /// <summary>
    /// 添加标题和基本信息
    /// </summary>
    private async Task AppendHeaderAsync(StringBuilder mdBuilder, BenchmarkReportData data, ReportConfiguration config)
    {
        mdBuilder.AppendLine($"# {config.Title}");
        mdBuilder.AppendLine();
        mdBuilder.AppendLine($"**生成时间**: {data.GeneratedAt:yyyy-MM-dd HH:mm:ss}");
        mdBuilder.AppendLine($"**报告版本**: {data.ReportVersion}");
        mdBuilder.AppendLine($"**测试场景**: {data.TestConfig.ScenarioName}");
        
        var statusEmoji = data.Summary.IsSuccessful ? "✅" : "❌";
        var gradeEmoji = data.Summary.Grade switch
        {
            PerformanceGrade.Excellent => "🟢",
            PerformanceGrade.Good => "🟡",
            PerformanceGrade.Average => "🟠",
            PerformanceGrade.NeedsImprovement => "🔴",
            PerformanceGrade.Poor => "🔴",
            _ => "⚪"
        };
        
        mdBuilder.AppendLine($"**测试状态**: {statusEmoji} {(data.Summary.IsSuccessful ? "成功" : "失败")}");
        mdBuilder.AppendLine($"**性能等级**: {gradeEmoji} {data.Summary.Grade}");
        mdBuilder.AppendLine();

        await Task.CompletedTask;
    }

    /// <summary>
    /// 添加测试摘要
    /// </summary>
    private async Task AppendSummaryAsync(StringBuilder mdBuilder, BenchmarkReportData data)
    {
        mdBuilder.AppendLine("## 📊 测试摘要");
        mdBuilder.AppendLine();

        mdBuilder.AppendLine("| 指标 | 值 |");
        mdBuilder.AppendLine("|------|-----|");
        mdBuilder.AppendLine($"| 测试持续时间 | {data.TestConfig.DurationSeconds} 秒 |");
        mdBuilder.AppendLine($"| 总请求数 | {data.Metrics.Throughput.TotalRequests:N0} |");
        mdBuilder.AppendLine($"| 成功请求数 | {data.Metrics.Throughput.SuccessfulRequests:N0} |");
        mdBuilder.AppendLine($"| 失败请求数 | {data.Metrics.Throughput.FailedRequests:N0} |");
        mdBuilder.AppendLine($"| 平均延迟 | {data.Metrics.Latency.AverageMs:F2} ms |");
        mdBuilder.AppendLine($"| P95延迟 | {data.Metrics.Latency.P95Ms:F2} ms |");
        mdBuilder.AppendLine($"| P99延迟 | {data.Metrics.Latency.P99Ms:F2} ms |");
        mdBuilder.AppendLine($"| 平均RPS | {data.Metrics.Throughput.AverageRps:F2} |");
        mdBuilder.AppendLine($"| 峰值RPS | {data.Metrics.Throughput.PeakRps:F2} |");
        mdBuilder.AppendLine($"| 错误率 | {data.Metrics.Errors.ErrorRate:F2}% |");
        mdBuilder.AppendLine();

        await Task.CompletedTask;
    }

    /// <summary>
    /// 添加测试配置
    /// </summary>
    private async Task AppendConfigurationAsync(StringBuilder mdBuilder, BenchmarkReportData data)
    {
        mdBuilder.AppendLine("## ⚙️ 测试配置");
        mdBuilder.AppendLine();

        mdBuilder.AppendLine("| 配置项 | 值 |");
        mdBuilder.AppendLine("|--------|-----|");
        mdBuilder.AppendLine($"| 服务器地址 | `{data.TestConfig.ServerAddress}` |");
        mdBuilder.AppendLine($"| 测试场景 | {data.TestConfig.ScenarioName} |");
        mdBuilder.AppendLine($"| 测试持续时间 | {data.TestConfig.DurationSeconds} 秒 |");
        mdBuilder.AppendLine($"| 并发连接数 | {data.TestConfig.ConnectionCount} |");
        mdBuilder.AppendLine($"| 请求速率 | {data.TestConfig.RequestRate} QPS |");
        mdBuilder.AppendLine($"| 预热时间 | {data.TestConfig.WarmupSeconds} 秒 |");
        mdBuilder.AppendLine($"| 测试开始时间 | {data.TestConfig.StartTime:yyyy-MM-dd HH:mm:ss} |");
        mdBuilder.AppendLine($"| 测试结束时间 | {data.TestConfig.EndTime:yyyy-MM-dd HH:mm:ss} |");
        mdBuilder.AppendLine();

        await Task.CompletedTask;
    }

    /// <summary>
    /// 添加环境信息
    /// </summary>
    private async Task AppendEnvironmentAsync(StringBuilder mdBuilder, BenchmarkReportData data)
    {
        mdBuilder.AppendLine("## 🖥️ 环境信息");
        mdBuilder.AppendLine();

        mdBuilder.AppendLine("| 环境项 | 值 |");
        mdBuilder.AppendLine("|--------|-----|");
        mdBuilder.AppendLine($"| 操作系统 | {data.Environment.OperatingSystem} |");
        mdBuilder.AppendLine($"| .NET版本 | {data.Environment.DotNetVersion} |");
        mdBuilder.AppendLine($"| 处理器 | {data.Environment.ProcessorInfo} |");
        mdBuilder.AppendLine($"| 总内存 | {data.Environment.TotalMemoryMB:N0} MB |");
        mdBuilder.AppendLine($"| 机器名称 | {data.Environment.MachineName} |");
        if (!string.IsNullOrEmpty(data.Environment.NetworkConfig))
        {
            mdBuilder.AppendLine($"| 网络配置 | {data.Environment.NetworkConfig} |");
        }
        mdBuilder.AppendLine();

        await Task.CompletedTask;
    }

    /// <summary>
    /// 添加性能指标
    /// </summary>
    private async Task AppendPerformanceMetricsAsync(StringBuilder mdBuilder, BenchmarkReportData data)
    {
        mdBuilder.AppendLine("## 📈 性能指标");
        mdBuilder.AppendLine();

        // 延迟指标
        mdBuilder.AppendLine("### 🕐 延迟指标");
        mdBuilder.AppendLine();
        mdBuilder.AppendLine("| 延迟百分位 | 值 (ms) |");
        mdBuilder.AppendLine("|------------|---------|");
        mdBuilder.AppendLine($"| 平均延迟 | {data.Metrics.Latency.AverageMs:F2} |");
        mdBuilder.AppendLine($"| 最小延迟 | {data.Metrics.Latency.MinMs:F2} |");
        mdBuilder.AppendLine($"| 最大延迟 | {data.Metrics.Latency.MaxMs:F2} |");
        mdBuilder.AppendLine($"| P50 (中位数) | {data.Metrics.Latency.P50Ms:F2} |");
        mdBuilder.AppendLine($"| P95 | {data.Metrics.Latency.P95Ms:F2} |");
        mdBuilder.AppendLine($"| P99 | {data.Metrics.Latency.P99Ms:F2} |");
        mdBuilder.AppendLine($"| P99.9 | {data.Metrics.Latency.P999Ms:F2} |");
        mdBuilder.AppendLine($"| 标准差 | {data.Metrics.Latency.StandardDeviation:F2} |");
        mdBuilder.AppendLine();

        // 吞吐量指标
        mdBuilder.AppendLine("### 🚀 吞吐量指标");
        mdBuilder.AppendLine();
        mdBuilder.AppendLine("| 指标 | 值 |");
        mdBuilder.AppendLine("|------|-----|");
        mdBuilder.AppendLine($"| 平均RPS | {data.Metrics.Throughput.AverageRps:F2} |");
        mdBuilder.AppendLine($"| 峰值RPS | {data.Metrics.Throughput.PeakRps:F2} |");
        mdBuilder.AppendLine($"| 总请求数 | {data.Metrics.Throughput.TotalRequests:N0} |");
        mdBuilder.AppendLine($"| 成功请求数 | {data.Metrics.Throughput.SuccessfulRequests:N0} |");
        mdBuilder.AppendLine($"| 失败请求数 | {data.Metrics.Throughput.FailedRequests:N0} |");
        var successRate = data.Metrics.Throughput.TotalRequests > 0 
            ? (double)data.Metrics.Throughput.SuccessfulRequests / data.Metrics.Throughput.TotalRequests * 100
            : 0;
        mdBuilder.AppendLine($"| 成功率 | {successRate:F2}% |");
        mdBuilder.AppendLine();

        // 资源使用指标
        mdBuilder.AppendLine("### 💻 资源使用");
        mdBuilder.AppendLine();
        mdBuilder.AppendLine("| 资源类型 | 使用量 |");
        mdBuilder.AppendLine("|----------|--------|");
        mdBuilder.AppendLine($"| CPU使用率 | {data.Metrics.Resources.CpuUsagePercent:F2}% |");
        mdBuilder.AppendLine($"| 内存使用量 | {data.Metrics.Resources.MemoryUsageMB:N0} MB |");
        mdBuilder.AppendLine($"| 网络发送 | {FormatBytes(data.Metrics.Resources.NetworkSentBytes)} |");
        mdBuilder.AppendLine($"| 网络接收 | {FormatBytes(data.Metrics.Resources.NetworkReceivedBytes)} |");
        mdBuilder.AppendLine();

        await Task.CompletedTask;
    }

    /// <summary>
    /// 添加错误分析
    /// </summary>
    private async Task AppendErrorAnalysisAsync(StringBuilder mdBuilder, BenchmarkReportData data)
    {
        mdBuilder.AppendLine("## ❌ 错误分析");
        mdBuilder.AppendLine();

        if (data.Metrics.Errors.TotalErrors == 0)
        {
            mdBuilder.AppendLine("✅ **测试过程中未发现错误**");
            mdBuilder.AppendLine();
            return;
        }

        mdBuilder.AppendLine("| 错误类型 | 数量 | 百分比 |");
        mdBuilder.AppendLine("|----------|------|--------|");
        mdBuilder.AppendLine($"| 总错误数 | {data.Metrics.Errors.TotalErrors:N0} | {data.Metrics.Errors.ErrorRate:F2}% |");
        mdBuilder.AppendLine($"| 超时错误 | {data.Metrics.Errors.TimeoutErrors:N0} | {GetErrorPercentage(data.Metrics.Errors.TimeoutErrors, data.Metrics.Errors.TotalErrors):F2}% |");
        mdBuilder.AppendLine($"| 连接错误 | {data.Metrics.Errors.ConnectionErrors:N0} | {GetErrorPercentage(data.Metrics.Errors.ConnectionErrors, data.Metrics.Errors.TotalErrors):F2}% |");

        if (data.Metrics.Errors.ErrorsByType.Any())
        {
            mdBuilder.AppendLine();
            mdBuilder.AppendLine("### 详细错误分布");
            mdBuilder.AppendLine();
            mdBuilder.AppendLine("| 错误类型 | 次数 |");
            mdBuilder.AppendLine("|----------|------|");
            foreach (var error in data.Metrics.Errors.ErrorsByType.OrderByDescending(x => x.Value))
            {
                mdBuilder.AppendLine($"| {error.Key} | {error.Value:N0} |");
            }
        }
        mdBuilder.AppendLine();

        await Task.CompletedTask;
    }

    /// <summary>
    /// 添加建议和总结
    /// </summary>
    private async Task AppendRecommendationsAsync(StringBuilder mdBuilder, BenchmarkReportData data)
    {
        mdBuilder.AppendLine("## 💡 建议和总结");
        mdBuilder.AppendLine();

        if (data.Summary.KeyFindings.Any())
        {
            mdBuilder.AppendLine("### 🔍 主要发现");
            mdBuilder.AppendLine();
            foreach (var finding in data.Summary.KeyFindings)
            {
                mdBuilder.AppendLine($"- {finding}");
            }
            mdBuilder.AppendLine();
        }

        if (data.Summary.Recommendations.Any())
        {
            mdBuilder.AppendLine("### 📋 优化建议");
            mdBuilder.AppendLine();
            foreach (var recommendation in data.Summary.Recommendations)
            {
                mdBuilder.AppendLine($"- {recommendation}");
            }
            mdBuilder.AppendLine();
        }

        // 添加总体评估
        mdBuilder.AppendLine("### 📊 总体评估");
        mdBuilder.AppendLine();
        var gradeDescription = data.Summary.Grade switch
        {
            PerformanceGrade.Excellent => "性能表现优秀，各项指标均达到预期目标。",
            PerformanceGrade.Good => "性能表现良好，大部分指标达到预期，少数需要改进。",
            PerformanceGrade.Average => "性能表现一般，存在一些可以优化的空间。",
            PerformanceGrade.NeedsImprovement => "性能需要改进，建议对系统进行优化。",
            PerformanceGrade.Poor => "性能表现较差，需要重点关注和优化。",
            _ => "性能评估暂无。"
        };
        
        mdBuilder.AppendLine($"**性能等级**: {data.Summary.Grade}");
        mdBuilder.AppendLine();
        mdBuilder.AppendLine(gradeDescription);
        mdBuilder.AppendLine();

        // 添加页脚
        mdBuilder.AppendLine("---");
        mdBuilder.AppendLine();
        mdBuilder.AppendLine($"*报告生成于 {data.GeneratedAt:yyyy-MM-dd HH:mm:ss} by PulseRPC BenchmarkApp*");

        await Task.CompletedTask;
    }

    /// <summary>
    /// 格式化字节数
    /// </summary>
    private string FormatBytes(long bytes)
    {
        if (bytes == 0) return "0 B";

        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        var order = 0;
        var len = (double)bytes;

        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }

        return $"{len:F2} {sizes[order]}";
    }

    /// <summary>
    /// 计算错误百分比
    /// </summary>
    private double GetErrorPercentage(long errorCount, long totalErrors)
    {
        return totalErrors > 0 ? (double)errorCount / totalErrors * 100 : 0;
    }
} 