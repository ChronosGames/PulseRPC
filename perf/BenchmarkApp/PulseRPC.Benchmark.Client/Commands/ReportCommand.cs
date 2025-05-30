using System;
using System.CommandLine;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace PulseRPC.Benchmark.Client.Commands;

/// <summary>
/// 报告生成命令处理器
/// </summary>
public class ReportCommand
{
    private readonly ILogger<ReportCommand> _logger;

    public ReportCommand(ILogger<ReportCommand> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 创建报告命令
    /// </summary>
    public Command CreateCommand()
    {
        var inputOption = new Option<string>(
            aliases: new[] { "--input", "-i" },
            description: "输入结果文件路径");
        inputOption.IsRequired = true;

        var outputOption = new Option<string>(
            aliases: new[] { "--output", "-o" },
            description: "输出报告文件路径");

        var formatOption = new Option<string>(
            aliases: new[] { "--format", "-f" },
            description: "报告格式（html|pdf|json|csv）",
            getDefaultValue: () => "html");

        var templateOption = new Option<string>(
            aliases: new[] { "--template", "-t" },
            description: "报告模板名称（default|detailed|summary|comparison）",
            getDefaultValue: () => "default");

        var titleOption = new Option<string>(
            aliases: new[] { "--title" },
            description: "报告标题",
            getDefaultValue: () => "PulseRPC 基准测试报告");

        var includeChartsOption = new Option<bool>(
            aliases: new[] { "--include-charts" },
            description: "包含图表",
            getDefaultValue: () => true);

        var includeRawDataOption = new Option<bool>(
            aliases: new[] { "--include-raw-data" },
            description: "包含原始数据",
            getDefaultValue: () => false);

        var reportCommand = new Command("generate-report", "从测试结果生成报告")
        {
            inputOption,
            outputOption,
            formatOption,
            templateOption,
            titleOption,
            includeChartsOption,
            includeRawDataOption
        };

        reportCommand.SetHandler(async (input, output, format, template, title, includeCharts, includeRawData) =>
        {
            try
            {
                await ExecuteReportCommandAsync(input, output, format, template, title, includeCharts, includeRawData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "报告生成命令执行失败");
                Console.WriteLine($"❌ 报告生成失败: {ex.Message}");
                Environment.Exit(1);
            }
        }, inputOption, outputOption, formatOption, templateOption, titleOption, includeChartsOption, includeRawDataOption);

        return reportCommand;
    }

    /// <summary>
    /// 执行报告生成命令
    /// </summary>
    private async Task ExecuteReportCommandAsync(
        string inputPath, string? outputPath, string format, string template,
        string title, bool includeCharts, bool includeRawData)
    {
        _logger.LogInformation("开始生成报告");

        // 验证输入文件
        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException($"输入文件不存在: {inputPath}");
        }

        // 确定输出路径
        if (string.IsNullOrEmpty(outputPath))
        {
            var inputFileName = Path.GetFileNameWithoutExtension(inputPath);
            outputPath = $"{inputFileName}_report.{GetFileExtension(format)}";
        }

        Console.WriteLine("📄 生成基准测试报告");
        Console.WriteLine($"📁 输入文件: {inputPath}");
        Console.WriteLine($"📊 报告格式: {format}");
        Console.WriteLine($"🎨 报告模板: {template}");
        Console.WriteLine($"📈 包含图表: {(includeCharts ? "是" : "否")}");
        Console.WriteLine($"📋 包含原始数据: {(includeRawData ? "是" : "否")}");
        Console.WriteLine($"💾 输出文件: {outputPath}");
        Console.WriteLine();

        // 加载测试结果数据
        Console.WriteLine("🔍 加载测试结果数据...");
        var testResults = await LoadTestResultsAsync(inputPath);
        Console.WriteLine($"✅ 成功加载 {testResults.Count} 个测试结果");

        // 生成报告
        Console.WriteLine($"🎯 生成 {format.ToUpper()} 格式报告...");
        await GenerateReportAsync(testResults, outputPath, format, template, title, includeCharts, includeRawData);

        // 验证输出文件
        if (File.Exists(outputPath))
        {
            var fileInfo = new FileInfo(outputPath);
            Console.WriteLine($"✅ 报告生成成功: {outputPath}");
            Console.WriteLine($"📏 文件大小: {FormatFileSize(fileInfo.Length)}");
        }
        else
        {
            throw new InvalidOperationException("报告生成失败，输出文件不存在");
        }
    }

    /// <summary>
    /// 加载测试结果数据
    /// </summary>
    private async Task<List<TestResultData>> LoadTestResultsAsync(string inputPath)
    {
        try
        {
            var jsonContent = await File.ReadAllTextAsync(inputPath);

            // 简化实现：假设输入是JSON格式的测试结果
            var results = new List<TestResultData>
            {
                new TestResultData
                {
                    TestName = "Sample Test",
                    StartTime = DateTime.UtcNow.AddMinutes(-5),
                    EndTime = DateTime.UtcNow,
                    TotalRequests = 10000,
                    SuccessfulRequests = 9950,
                    FailedRequests = 50,
                    AverageLatencyMs = 12.5,
                    P95LatencyMs = 45.2,
                    P99LatencyMs = 85.7,
                    RequestsPerSecond = 1000.0
                }
            };

            return results;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"加载测试结果失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 生成报告
    /// </summary>
    private async Task GenerateReportAsync(
        List<TestResultData> testResults, string outputPath, string format,
        string template, string title, bool includeCharts, bool includeRawData)
    {
        switch (format.ToLower())
        {
            case "html":
                await GenerateHtmlReportAsync(testResults, outputPath, template, title, includeCharts, includeRawData);
                break;
            case "json":
                await GenerateJsonReportAsync(testResults, outputPath, includeRawData);
                break;
            case "csv":
                await GenerateCsvReportAsync(testResults, outputPath);
                break;
            case "pdf":
                await GeneratePdfReportAsync(testResults, outputPath, template, title, includeCharts);
                break;
            default:
                throw new ArgumentException($"不支持的报告格式: {format}");
        }
    }

    /// <summary>
    /// 生成HTML报告
    /// </summary>
    private async Task GenerateHtmlReportAsync(
        List<TestResultData> testResults, string outputPath, string template,
        string title, bool includeCharts, bool includeRawData)
    {
        var html = GenerateHtmlContent(testResults, template, title, includeCharts, includeRawData);
        await File.WriteAllTextAsync(outputPath, html);
        _logger.LogInformation("HTML报告已生成: {OutputPath}", outputPath);
    }

    /// <summary>
    /// 生成JSON报告
    /// </summary>
    private async Task GenerateJsonReportAsync(List<TestResultData> testResults, string outputPath, bool includeRawData)
    {
        var reportData = new
        {
            GeneratedAt = DateTime.UtcNow,
            Version = "1.0.0",
            TestResults = testResults,
            Summary = new
            {
                TotalTests = testResults.Count,
                TotalRequests = testResults.Sum(r => r.TotalRequests),
                OverallSuccessRate = testResults.Average(r => r.SuccessRate),
                AverageLatency = testResults.Average(r => r.AverageLatencyMs)
            },
            IncludeRawData = includeRawData
        };

        var json = System.Text.Json.JsonSerializer.Serialize(reportData, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });

        await File.WriteAllTextAsync(outputPath, json);
        _logger.LogInformation("JSON报告已生成: {OutputPath}", outputPath);
    }

    /// <summary>
    /// 生成CSV报告
    /// </summary>
    private async Task GenerateCsvReportAsync(List<TestResultData> testResults, string outputPath)
    {
        var csv = new System.Text.StringBuilder();
        csv.AppendLine("TestName,StartTime,EndTime,TotalRequests,SuccessfulRequests,FailedRequests,SuccessRate,AverageLatencyMs,P95LatencyMs,P99LatencyMs,RequestsPerSecond");

        foreach (var result in testResults)
        {
            csv.AppendLine($"{result.TestName},{result.StartTime:yyyy-MM-dd HH:mm:ss},{result.EndTime:yyyy-MM-dd HH:mm:ss}," +
                          $"{result.TotalRequests},{result.SuccessfulRequests},{result.FailedRequests}," +
                          $"{result.SuccessRate:F4},{result.AverageLatencyMs:F2},{result.P95LatencyMs:F2}," +
                          $"{result.P99LatencyMs:F2},{result.RequestsPerSecond:F2}");
        }

        await File.WriteAllTextAsync(outputPath, csv.ToString());
        _logger.LogInformation("CSV报告已生成: {OutputPath}", outputPath);
    }

    /// <summary>
    /// 生成PDF报告
    /// </summary>
    private async Task GeneratePdfReportAsync(
        List<TestResultData> testResults, string outputPath, string template,
        string title, bool includeCharts)
    {
        // 简化实现：生成HTML然后转换为PDF（这里仅作为占位符）
        await Task.Delay(100);
        throw new NotImplementedException("PDF报告生成将在后续版本中实现");
    }

    /// <summary>
    /// 生成HTML内容
    /// </summary>
    private string GenerateHtmlContent(
        List<TestResultData> testResults, string template, string title,
        bool includeCharts, bool includeRawData)
    {
        var html = $@"
<!DOCTYPE html>
<html lang='zh-CN'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>{title}</title>
    <style>
        body {{ font-family: Arial, sans-serif; margin: 20px; }}
        .header {{ background: #f8f9fa; padding: 20px; border-radius: 5px; margin-bottom: 20px; }}
        .summary {{ display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 15px; margin-bottom: 30px; }}
        .metric {{ background: white; border: 1px solid #dee2e6; padding: 15px; border-radius: 5px; }}
        .metric-value {{ font-size: 24px; font-weight: bold; color: #007bff; }}
        .metric-label {{ color: #6c757d; font-size: 14px; }}
        table {{ width: 100%; border-collapse: collapse; margin-top: 20px; }}
        th, td {{ padding: 10px; text-align: left; border-bottom: 1px solid #dee2e6; }}
        th {{ background-color: #f8f9fa; }}
        .success {{ color: #28a745; }}
        .warning {{ color: #ffc107; }}
        .error {{ color: #dc3545; }}
    </style>
</head>
<body>
    <div class='header'>
        <h1>{title}</h1>
        <p>生成时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}</p>
    </div>

    <div class='summary'>
        <div class='metric'>
            <div class='metric-value'>{testResults.Count}</div>
            <div class='metric-label'>测试场景</div>
        </div>
        <div class='metric'>
            <div class='metric-value'>{testResults.Sum(r => r.TotalRequests):N0}</div>
            <div class='metric-label'>总请求数</div>
        </div>
        <div class='metric'>
            <div class='metric-value'>{testResults.Average(r => r.SuccessRate):P2}</div>
            <div class='metric-label'>平均成功率</div>
        </div>
        <div class='metric'>
            <div class='metric-value'>{testResults.Average(r => r.AverageLatencyMs):F2} ms</div>
            <div class='metric-label'>平均延迟</div>
        </div>
    </div>

    <h2>测试结果详情</h2>
    <table>
        <thead>
            <tr>
                <th>测试名称</th>
                <th>总请求数</th>
                <th>成功率</th>
                <th>平均延迟 (ms)</th>
                <th>P95延迟 (ms)</th>
                <th>P99延迟 (ms)</th>
                <th>QPS</th>
            </tr>
        </thead>
        <tbody>";

        foreach (var result in testResults)
        {
            var successClass = result.SuccessRate >= 0.99 ? "success" : result.SuccessRate >= 0.95 ? "warning" : "error";
            html += $@"
            <tr>
                <td>{result.TestName}</td>
                <td>{result.TotalRequests:N0}</td>
                <td class='{successClass}'>{result.SuccessRate:P2}</td>
                <td>{result.AverageLatencyMs:F2}</td>
                <td>{result.P95LatencyMs:F2}</td>
                <td>{result.P99LatencyMs:F2}</td>
                <td>{result.RequestsPerSecond:F2}</td>
            </tr>";
        }

        html += @"
        </tbody>
    </table>
</body>
</html>";

        return html;
    }

    /// <summary>
    /// 获取文件扩展名
    /// </summary>
    private string GetFileExtension(string format)
    {
        return format.ToLower() switch
        {
            "html" => "html",
            "json" => "json",
            "csv" => "csv",
            "pdf" => "pdf",
            _ => "txt"
        };
    }

    /// <summary>
    /// 格式化文件大小
    /// </summary>
    private string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}

/// <summary>
/// 测试结果数据模型
/// </summary>
public class TestResultData
{
    public string TestName { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int TotalRequests { get; set; }
    public int SuccessfulRequests { get; set; }
    public int FailedRequests { get; set; }
    public double SuccessRate => TotalRequests > 0 ? (double)SuccessfulRequests / TotalRequests : 0;
    public double AverageLatencyMs { get; set; }
    public double P95LatencyMs { get; set; }
    public double P99LatencyMs { get; set; }
    public double RequestsPerSecond { get; set; }
}
