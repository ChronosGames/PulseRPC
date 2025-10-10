using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PulseRPC.Benchmark.Metrics.Models;

namespace PulseRPC.Benchmark.Metrics.Exporters;

/// <summary>
/// HTML格式报告导出器
/// </summary>
public class HtmlReportExporter : IBenchmarkReportGenerator
{
    private readonly ILogger _logger;
    private string? _template;

    public HtmlReportExporter(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<string> GenerateReportAsync(BenchmarkReportData data, ReportConfiguration config)
    {
        try
        {
            _logger.LogDebug("开始生成HTML报告");

            // 加载模板
            var template = await LoadTemplateAsync(config);

            // 准备模板数据
            var templateData = PrepareTemplateData(data, config);

            // 渲染模板
            var htmlContent = RenderTemplate(template, templateData);

            _logger.LogDebug("HTML报告生成完成，大小: {Size} 字符", htmlContent.Length);

            return htmlContent;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "生成HTML报告失败: {Message}", ex.Message);
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

        // 验证模板文件
        if (!string.IsNullOrEmpty(config.CustomTemplatePath))
        {
            if (!File.Exists(config.CustomTemplatePath))
            {
                result.IsValid = false;
                result.Errors.Add($"自定义HTML模板文件不存在: {config.CustomTemplatePath}");
            }
        }

        await Task.CompletedTask;
        return result;
    }

    /// <inheritdoc />
    public ReportFormat[] GetSupportedFormats()
    {
        return new[] { ReportFormat.Html };
    }

    /// <summary>
    /// 加载HTML模板
    /// </summary>
    private async Task<string> LoadTemplateAsync(ReportConfiguration config)
    {
        if (_template != null)
        {
            return _template;
        }

        string templateContent;

        if (!string.IsNullOrEmpty(config.CustomTemplatePath) && File.Exists(config.CustomTemplatePath))
        {
            _logger.LogDebug("使用自定义模板: {TemplatePath}", config.CustomTemplatePath);
            templateContent = await File.ReadAllTextAsync(config.CustomTemplatePath, Encoding.UTF8);
        }
        else
        {
            _logger.LogDebug("使用内置模板");
            templateContent = await LoadEmbeddedTemplateAsync();
        }

        _template = templateContent;
        return _template;
    }

    /// <summary>
    /// 加载内置模板
    /// </summary>
    private async Task<string> LoadEmbeddedTemplateAsync()
    {
        var templatePath = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "",
            "Templates", "html-template.html");

        if (File.Exists(templatePath))
        {
            return await File.ReadAllTextAsync(templatePath, Encoding.UTF8);
        }

        // 如果文件不存在，返回基础模板
        return GetFallbackTemplate();
    }

    /// <summary>
    /// 准备模板数据
    /// </summary>
    private Dictionary<string, object> PrepareTemplateData(BenchmarkReportData data, ReportConfiguration config)
    {
        var templateData = new Dictionary<string, object>
        {
            // 基本信息
            ["title"] = config.Title,
            ["generatedAt"] = data.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss"),
            ["reportVersion"] = data.ReportVersion,
            ["scenarioName"] = data.TestConfig.ScenarioName,

            // 状态信息
            ["statusClass"] = data.Summary.IsSuccessful ? "success" : "danger",
            ["statusText"] = data.Summary.IsSuccessful ? "成功" : "失败",
            ["gradeClass"] = GetGradeClass(data.Summary.Grade),
            ["gradeText"] = GetGradeText(data.Summary.Grade),
            ["gradeDescription"] = GetGradeDescription(data.Summary.Grade),

            // 测试摘要
            ["totalRequests"] = FormatNumber(data.Metrics.Throughput.TotalRequests),
            ["averageLatency"] = data.Metrics.Latency.AverageMs.ToString("F2"),
            ["averageRps"] = data.Metrics.Throughput.AverageRps.ToString("F2"),
            ["errorRate"] = data.Metrics.Errors.ErrorRate.ToString("F2"),

            // 测试配置
            ["serverAddress"] = data.TestConfig.ServerAddress,
            ["durationSeconds"] = data.TestConfig.DurationSeconds,
            ["connectionCount"] = data.TestConfig.ConnectionCount,
            ["requestRate"] = data.TestConfig.RequestRate,
            ["warmupSeconds"] = data.TestConfig.WarmupSeconds,

            // 环境信息
            ["operatingSystem"] = data.Environment.OperatingSystem,
            ["dotnetVersion"] = data.Environment.DotNetVersion,
            ["processorInfo"] = data.Environment.ProcessorInfo,
            ["totalMemoryMB"] = FormatNumber(data.Environment.TotalMemoryMB),
            ["machineName"] = data.Environment.MachineName,

            // 延迟指标
            ["latencyAverage"] = data.Metrics.Latency.AverageMs.ToString("F2"),
            ["latencyMin"] = data.Metrics.Latency.MinMs.ToString("F2"),
            ["latencyMax"] = data.Metrics.Latency.MaxMs.ToString("F2"),
            ["latencyP50"] = data.Metrics.Latency.P50Ms.ToString("F2"),
            ["latencyP95"] = data.Metrics.Latency.P95Ms.ToString("F2"),
            ["latencyP99"] = data.Metrics.Latency.P99Ms.ToString("F2"),
            ["latencyP999"] = data.Metrics.Latency.P999Ms.ToString("F2"),

            // 吞吐量指标
            ["peakRps"] = data.Metrics.Throughput.PeakRps.ToString("F2"),
            ["successfulRequests"] = FormatNumber(data.Metrics.Throughput.SuccessfulRequests),
            ["failedRequests"] = FormatNumber(data.Metrics.Throughput.FailedRequests),

            // 资源使用
            ["cpuUsagePercent"] = data.Metrics.Resources.CpuUsagePercent.ToString("F2"),
            ["memoryUsageMB"] = FormatNumber(data.Metrics.Resources.MemoryUsageMB),
            ["networkSent"] = FormatBytes(data.Metrics.Resources.NetworkSentBytes),
            ["networkReceived"] = FormatBytes(data.Metrics.Resources.NetworkReceivedBytes),

            // 错误信息
            ["hasErrors"] = data.Metrics.Errors.TotalErrors > 0,
            ["totalErrors"] = FormatNumber(data.Metrics.Errors.TotalErrors),

            // 基线比较数据
            ["hasBaselineComparison"] = data.CustomData.ContainsKey("__BaselineComparison"),
            ["baselineComparisonHtml"] = GenerateBaselineComparisonHtml(data),

            // 阈值验证数据
            ["hasThresholdResults"] = data.CustomData.ContainsKey("__ThresholdResults"),
            ["thresholdResultsHtml"] = GenerateThresholdResultsHtml(data),

            // 协议比较数据
            ["hasProtocolComparison"] = HasProtocolComparisonData(data),
            ["protocolComparisonHtml"] = GenerateProtocolComparisonHtml(data),
            ["timeoutErrors"] = FormatNumber(data.Metrics.Errors.TimeoutErrors),
            ["connectionErrors"] = FormatNumber(data.Metrics.Errors.ConnectionErrors),

            // 建议和发现
            ["keyFindings"] = data.Summary.KeyFindings,
            ["recommendations"] = data.Summary.Recommendations,

            // 图表相关
            ["includeCharts"] = config.IncludeCharts
        };

        // 添加错误详情
        if (data.Metrics.Errors.ErrorsByType.Any())
        {
            var errorDetails = data.Metrics.Errors.ErrorsByType
                .Select(kv => $"{kv.Key}: {kv.Value} 次")
                .ToList();
            templateData["errorDetails"] = errorDetails;
        }

        // 添加图表数据
        if (config.IncludeCharts)
        {
            templateData["latencyChartData"] = GenerateLatencyChartData(data.Metrics.Latency);
            templateData["throughputChartData"] = GenerateThroughputChartData(data.Metrics.Throughput);
            templateData["resourceChartData"] = GenerateResourceChartData(data.Metrics.Resources);
            templateData["chartScripts"] = GenerateChartScripts(data.Metrics, config.Charts);
        }

        return templateData;
    }

    /// <summary>
    /// 渲染模板
    /// </summary>
    private string RenderTemplate(string template, Dictionary<string, object> data)
    {
        var result = template;

        // 处理简单变量替换
        foreach (var kvp in data)
        {
            var placeholder = $"{{{{{kvp.Key}}}}}";
            var value = kvp.Value?.ToString() ?? "";
            result = result.Replace(placeholder, value);
        }

        // 处理条件块
        result = ProcessConditionalBlocks(result, data);

        // 处理循环块
        result = ProcessLoopBlocks(result, data);

        return result;
    }

    /// <summary>
    /// 处理条件块
    /// </summary>
    private string ProcessConditionalBlocks(string template, Dictionary<string, object> data)
    {
        // 匹配 {{#if condition}} ... {{/if}} 和 {{#if condition}} ... {{else}} ... {{/if}}
        var ifPattern = @"\{\{#if\s+(\w+)\}\}(.*?)\{\{/if\}\}";
        var ifElsePattern = @"\{\{#if\s+(\w+)\}\}(.*?)\{\{else\}\}(.*?)\{\{/if\}\}";

        // 先处理 if-else 结构
        template = Regex.Replace(template, ifElsePattern, match =>
        {
            var condition = match.Groups[1].Value;
            var ifContent = match.Groups[2].Value;
            var elseContent = match.Groups[3].Value;

            if (data.TryGetValue(condition, out var value) && IsTrue(value))
            {
                return ifContent;
            }
            return elseContent;
        }, RegexOptions.Singleline);

        // 再处理简单的 if 结构
        template = Regex.Replace(template, ifPattern, match =>
        {
            var condition = match.Groups[1].Value;
            var content = match.Groups[2].Value;

            if (data.TryGetValue(condition, out var value) && IsTrue(value))
            {
                return content;
            }
            return "";
        }, RegexOptions.Singleline);

        return template;
    }

    /// <summary>
    /// 处理循环块
    /// </summary>
    private string ProcessLoopBlocks(string template, Dictionary<string, object> data)
    {
        // 匹配 {{#each array}} ... {{/each}}
        var eachPattern = @"\{\{#each\s+(\w+)\}\}(.*?)\{\{/each\}\}";

        template = Regex.Replace(template, eachPattern, match =>
        {
            var arrayName = match.Groups[1].Value;
            var itemTemplate = match.Groups[2].Value;

            if (data.TryGetValue(arrayName, out var value) && value is IEnumerable<object> items)
            {
                var result = new StringBuilder();
                foreach (var item in items)
                {
                    var itemContent = itemTemplate.Replace("{{this}}", item?.ToString() ?? "");
                    result.Append(itemContent);
                }
                return result.ToString();
            }
            return "";
        }, RegexOptions.Singleline);

        return template;
    }

    /// <summary>
    /// 判断值是否为真
    /// </summary>
    private bool IsTrue(object? value)
    {
        return value switch
        {
            null => false,
            bool b => b,
            string s => !string.IsNullOrEmpty(s),
            int i => i != 0,
            long l => l != 0,
            double d => d != 0,
            float f => f != 0,
            System.Collections.ICollection c => c.Count > 0,
            _ => true
        };
    }

    /// <summary>
    /// 生成延迟图表数据
    /// </summary>
    private string GenerateLatencyChartData(LatencyMetrics latency)
    {
        if (!latency.Distribution.Any())
        {
            return "<canvas id='latencyChart'></canvas>";
        }

        return "<canvas id='latencyChart'></canvas>";
    }

    /// <summary>
    /// 生成吞吐量图表数据
    /// </summary>
    private string GenerateThroughputChartData(ThroughputMetrics throughput)
    {
        if (!throughput.TimeSeries.Any())
        {
            return "<canvas id='throughputChart'></canvas>";
        }

        return "<canvas id='throughputChart'></canvas>";
    }

    /// <summary>
    /// 生成资源图表数据
    /// </summary>
    private string GenerateResourceChartData(ResourceMetrics resources)
    {
        if (!resources.TimeSeries.Any())
        {
            return "<canvas id='resourceChart'></canvas>";
        }

        return "<canvas id='resourceChart'></canvas>";
    }

    /// <summary>
    /// 生成图表脚本
    /// </summary>
    private string GenerateChartScripts(PerformanceMetrics metrics, ChartConfiguration chartConfig)
    {
        var scripts = new StringBuilder();

        // 延迟图表脚本
        if (metrics.Latency.Distribution.Any())
        {
            scripts.AppendLine(GenerateLatencyChartScript(metrics.Latency, chartConfig.LatencyChart));
        }

        // 吞吐量图表脚本
        if (metrics.Throughput.TimeSeries.Any())
        {
            scripts.AppendLine(GenerateThroughputChartScript(metrics.Throughput, chartConfig.ThroughputChart));
        }

        // 资源图表脚本
        if (metrics.Resources.TimeSeries.Any())
        {
            scripts.AppendLine(GenerateResourceChartScript(metrics.Resources, chartConfig.ResourceChart));
        }

        return scripts.ToString();
    }

    /// <summary>
    /// 生成延迟图表脚本
    /// </summary>
    private string GenerateLatencyChartScript(LatencyMetrics latency, LatencyChartConfig config)
    {
        var labels = latency.Distribution.Take(100).Select(p => p.Timestamp.ToString("HH:mm:ss")).ToArray();
        var data = latency.Distribution.Take(100).Select(p => p.LatencyMs).ToArray();

        return $@"
        // 延迟图表
        const latencyCtx = document.getElementById('latencyChart');
        if (latencyCtx) {{
            new Chart(latencyCtx, {{
                type: 'line',
                data: {{
                    labels: {System.Text.Json.JsonSerializer.Serialize(labels)},
                    datasets: [{{
                        label: '延迟 (ms)',
                        data: {System.Text.Json.JsonSerializer.Serialize(data)},
                        borderColor: '#007acc',
                        backgroundColor: 'rgba(0, 122, 204, 0.1)',
                        tension: 0.1
                    }}]
                }},
                options: {{
                    responsive: true,
                    plugins: {{
                        title: {{
                            display: true,
                            text: '延迟趋势图'
                        }}
                    }},
                    scales: {{
                        y: {{
                            beginAtZero: true,
                            title: {{
                                display: true,
                                text: '延迟 (ms)'
                            }}
                        }}
                    }}
                }}
            }});
        }}";
    }

    /// <summary>
    /// 生成吞吐量图表脚本
    /// </summary>
    private string GenerateThroughputChartScript(ThroughputMetrics throughput, ThroughputChartConfig config)
    {
        var labels = throughput.TimeSeries.Take(100).Select(p => p.Timestamp.ToString("HH:mm:ss")).ToArray();
        var data = throughput.TimeSeries.Take(100).Select(p => p.Rps).ToArray();

        return $@"
        // 吞吐量图表
        const throughputCtx = document.getElementById('throughputChart');
        if (throughputCtx) {{
            new Chart(throughputCtx, {{
                type: 'line',
                data: {{
                    labels: {System.Text.Json.JsonSerializer.Serialize(labels)},
                    datasets: [{{
                        label: 'RPS',
                        data: {System.Text.Json.JsonSerializer.Serialize(data)},
                        borderColor: '#28a745',
                        backgroundColor: 'rgba(40, 167, 69, 0.1)',
                        tension: 0.1
                    }}]
                }},
                options: {{
                    responsive: true,
                    plugins: {{
                        title: {{
                            display: true,
                            text: '吞吐量趋势图'
                        }}
                    }},
                    scales: {{
                        y: {{
                            beginAtZero: true,
                            title: {{
                                display: true,
                                text: 'RPS'
                            }}
                        }}
                    }}
                }}
            }});
        }}";
    }

    /// <summary>
    /// 生成资源图表脚本
    /// </summary>
    private string GenerateResourceChartScript(ResourceMetrics resources, ResourceChartConfig config)
    {
        var labels = resources.TimeSeries.Take(100).Select(p => p.Timestamp.ToString("HH:mm:ss")).ToArray();
        var cpuData = resources.TimeSeries.Take(100).Select(p => p.CpuPercent).ToArray();
        var memoryData = resources.TimeSeries.Take(100).Select(p => (double)p.MemoryMB).ToArray();

        return $@"
        // 资源使用图表
        const resourceCtx = document.getElementById('resourceChart');
        if (resourceCtx) {{
            new Chart(resourceCtx, {{
                type: 'line',
                data: {{
                    labels: {System.Text.Json.JsonSerializer.Serialize(labels)},
                    datasets: [
                        {{
                            label: 'CPU使用率 (%)',
                            data: {System.Text.Json.JsonSerializer.Serialize(cpuData)},
                            borderColor: '#dc3545',
                            backgroundColor: 'rgba(220, 53, 69, 0.1)',
                            yAxisID: 'y'
                        }},
                        {{
                            label: '内存使用 (MB)',
                            data: {System.Text.Json.JsonSerializer.Serialize(memoryData)},
                            borderColor: '#ffc107',
                            backgroundColor: 'rgba(255, 193, 7, 0.1)',
                            yAxisID: 'y1'
                        }}
                    ]
                }},
                options: {{
                    responsive: true,
                    plugins: {{
                        title: {{
                            display: true,
                            text: '资源使用趋势图'
                        }}
                    }},
                    scales: {{
                        y: {{
                            type: 'linear',
                            display: true,
                            position: 'left',
                            title: {{
                                display: true,
                                text: 'CPU使用率 (%)'
                            }}
                        }},
                        y1: {{
                            type: 'linear',
                            display: true,
                            position: 'right',
                            title: {{
                                display: true,
                                text: '内存使用 (MB)'
                            }},
                            grid: {{
                                drawOnChartArea: false,
                            }},
                        }}
                    }}
                }}
            }});
        }}";
    }

    /// <summary>
    /// 获取性能等级样式类
    /// </summary>
    private string GetGradeClass(PerformanceGrade grade)
    {
        return grade switch
        {
            PerformanceGrade.Excellent => "success",
            PerformanceGrade.Good => "info",
            PerformanceGrade.Average => "warning",
            PerformanceGrade.NeedsImprovement => "warning",
            PerformanceGrade.Poor => "danger",
            _ => "secondary"
        };
    }

    /// <summary>
    /// 获取性能等级文本
    /// </summary>
    private string GetGradeText(PerformanceGrade grade)
    {
        return grade switch
        {
            PerformanceGrade.Excellent => "优秀",
            PerformanceGrade.Good => "良好",
            PerformanceGrade.Average => "一般",
            PerformanceGrade.NeedsImprovement => "需要改进",
            PerformanceGrade.Poor => "较差",
            _ => "未知"
        };
    }

    /// <summary>
    /// 获取性能等级描述
    /// </summary>
    private string GetGradeDescription(PerformanceGrade grade)
    {
        return grade switch
        {
            PerformanceGrade.Excellent => "性能表现优秀，各项指标均达到预期目标。",
            PerformanceGrade.Good => "性能表现良好，大部分指标达到预期，少数需要改进。",
            PerformanceGrade.Average => "性能表现一般，存在一些可以优化的空间。",
            PerformanceGrade.NeedsImprovement => "性能需要改进，建议对系统进行优化。",
            PerformanceGrade.Poor => "性能表现较差，需要重点关注和优化。",
            _ => "性能评估暂无。"
        };
    }

    /// <summary>
    /// 格式化数字
    /// </summary>
    private string FormatNumber(long number)
    {
        return number.ToString("N0");
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
    /// 生成基线比较HTML
    /// </summary>
    private string GenerateBaselineComparisonHtml(BenchmarkReportData data)
    {
        if (!data.CustomData.TryGetValue("__BaselineComparison", out var baselineObj))
            return string.Empty;

        var html = new StringBuilder();
        html.AppendLine("<div class='baseline-comparison-section'>");
        html.AppendLine("  <h3>📊 基线比较</h3>");
        html.AppendLine("  <div class='alert alert-info'>与历史基线进行性能对比分析</div>");
        html.AppendLine("  <table class='table table-bordered'>");
        html.AppendLine("    <thead><tr><th>指标</th><th>当前值</th><th>基线值</th><th>变化</th></tr></thead>");
        html.AppendLine("    <tbody>");
        html.AppendLine($"      <tr><td colspan='4' class='text-muted'>基线比较数据: {baselineObj?.GetType().Name ?? "N/A"}</td></tr>");
        html.AppendLine("    </tbody>");
        html.AppendLine("  </table>");
        html.AppendLine("</div>");
        return html.ToString();
    }

    /// <summary>
    /// 生成阈值验证结果HTML
    /// </summary>
    private string GenerateThresholdResultsHtml(BenchmarkReportData data)
    {
        if (!data.CustomData.TryGetValue("__ThresholdResults", out var thresholdObj))
            return string.Empty;

        var html = new StringBuilder();
        html.AppendLine("<div class='threshold-results-section'>");
        html.AppendLine("  <h3>✓ 阈值验证</h3>");
        html.AppendLine("  <div class='alert alert-success'>性能指标阈值验证结果</div>");
        html.AppendLine("  <table class='table table-bordered'>");
        html.AppendLine("    <thead><tr><th>指标</th><th>阈值</th><th>实际值</th><th>状态</th></tr></thead>");
        html.AppendLine("    <tbody>");
        html.AppendLine($"      <tr><td colspan='4' class='text-muted'>阈值验证数据: {thresholdObj?.GetType().Name ?? "N/A"}</td></tr>");
        html.AppendLine("    </tbody>");
        html.AppendLine("  </table>");
        html.AppendLine("</div>");
        return html.ToString();
    }

    /// <summary>
    /// 检查是否有协议比较数据
    /// </summary>
    private bool HasProtocolComparisonData(BenchmarkReportData data)
    {
        // 检查是否有 TCP 和 KCP 的指标数据
        return data.CustomData.Keys.Any(k => k.Contains("TCP_") || k.Contains("KCP_"));
    }

    /// <summary>
    /// 生成协议比较HTML
    /// </summary>
    private string GenerateProtocolComparisonHtml(BenchmarkReportData data)
    {
        if (!HasProtocolComparisonData(data))
            return string.Empty;

        var html = new StringBuilder();
        html.AppendLine("<div class='protocol-comparison-section'>");
        html.AppendLine("  <h3>⚡ 协议性能比较</h3>");
        html.AppendLine("  <div class='alert alert-info'>TCP vs KCP 性能对比分析</div>");
        html.AppendLine("  <table class='table table-bordered'>");
        html.AppendLine("    <thead><tr><th>指标</th><th>TCP</th><th>KCP</th><th>差异</th></tr></thead>");
        html.AppendLine("    <tbody>");
        
        // 提取并比较指标
        var metrics = new[] { "AverageLatencyMs", "P95Ms", "P99Ms", "SuccessCount", "ErrorCount" };
        foreach (var metric in metrics)
        {
            var tcpKey = $"TCP_{metric}";
            var kcpKey = $"KCP_{metric}";
            
            if (data.CustomData.TryGetValue(tcpKey, out var tcpValue) && 
                data.CustomData.TryGetValue(kcpKey, out var kcpValue))
            {
                html.AppendLine($"      <tr>");
                html.AppendLine($"        <td>{metric}</td>");
                html.AppendLine($"        <td>{tcpValue}</td>");
                html.AppendLine($"        <td>{kcpValue}</td>");
                html.AppendLine($"        <td>-</td>");
                html.AppendLine($"      </tr>");
            }
        }
        
        // 添加推荐
        if (data.CustomData.TryGetValue("Recommendation", out var recommendation))
        {
            html.AppendLine($"      <tr><td colspan='4'><strong>推荐:</strong> <pre>{recommendation}</pre></td></tr>");
        }
        
        html.AppendLine("    </tbody>");
        html.AppendLine("  </table>");
        html.AppendLine("</div>");
        return html.ToString();
    }

    /// <summary>
    /// 获取后备模板
    /// </summary>
    private string GetFallbackTemplate()
    {
        return @"<!DOCTYPE html>
<html>
<head>
    <title>{{title}}</title>
    <style>
        body { font-family: Arial, sans-serif; margin: 40px; }
        .header { background: #007acc; color: white; padding: 20px; text-align: center; }
        .content { padding: 20px; }
        .metric { margin: 10px 0; }
    </style>
</head>
<body>
    <div class='header'>
        <h1>{{title}}</h1>
        <p>生成时间: {{generatedAt}}</p>
    </div>
    <div class='content'>
        <h2>测试摘要</h2>
        <div class='metric'>总请求数: {{totalRequests}}</div>
        <div class='metric'>平均延迟: {{averageLatency}} ms</div>
        <div class='metric'>平均RPS: {{averageRps}}</div>
        <div class='metric'>错误率: {{errorRate}}%</div>
    </div>
</body>
</html>";
    }
} 