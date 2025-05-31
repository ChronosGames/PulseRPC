using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PulseRPC.Benchmark.Metrics.Models;

namespace PulseRPC.Benchmark.Metrics.Exporters;

/// <summary>
/// 基准测试报告生成器主实现
/// </summary>
public class BenchmarkReportGenerator : IBenchmarkReportGenerator
{
    private readonly ILogger<BenchmarkReportGenerator> _logger;
    private readonly Dictionary<ReportFormat, IBenchmarkReportGenerator> _generators;

    public BenchmarkReportGenerator(ILogger<BenchmarkReportGenerator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _generators = new Dictionary<ReportFormat, IBenchmarkReportGenerator>
        {
            { ReportFormat.Html, new HtmlReportExporter(logger) },
            { ReportFormat.Json, new JsonReportExporter(logger) },
            { ReportFormat.Csv, new CsvReportExporter(logger) },
            { ReportFormat.Markdown, new MarkdownReportExporter(logger) }
        };
    }

    /// <inheritdoc />
    public async Task<string> GenerateReportAsync(BenchmarkReportData data, ReportConfiguration config)
    {
        try
        {
            _logger.LogInformation("开始生成 {Format} 格式报告", config.Format);

            // 验证配置
            var validationResult = await ValidateConfigurationAsync(config);
            if (!validationResult.IsValid)
            {
                var errors = string.Join(", ", validationResult.Errors);
                throw new ArgumentException($"报告配置验证失败: {errors}");
            }

            // 预处理数据
            var processedData = await PreprocessDataAsync(data, config);

            // 获取对应的生成器
            if (!_generators.TryGetValue(config.Format, out var generator))
            {
                throw new NotSupportedException($"不支持的报告格式: {config.Format}");
            }

            // 生成报告
            var result = await generator.GenerateReportAsync(processedData, config);

            _logger.LogInformation("成功生成 {Format} 格式报告，长度: {Length} 字符",
                config.Format, result.Length);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "生成报告失败: {Message}", ex.Message);
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
        try
        {
            // 生成报告内容
            var content = await GenerateReportAsync(data, config);

            // 确定输出路径
            var outputPath = DetermineOutputPath(config);

            // 确保目录存在
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                _logger.LogInformation("创建输出目录: {Directory}", directory);
            }

            // 写入文件
            await File.WriteAllTextAsync(outputPath, content, Encoding.UTF8);

            _logger.LogInformation("报告已保存到: {OutputPath}", outputPath);
            return outputPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存报告到文件失败: {Message}", ex.Message);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<ReportValidationResult> ValidateConfigurationAsync(ReportConfiguration config)
    {
        var result = new ReportValidationResult { IsValid = true };

        if (config == null)
        {
            result.IsValid = false;
            result.Errors.Add("报告配置不能为空");
            return result;
        }

        // 验证输出路径
        if (string.IsNullOrWhiteSpace(config.OutputPath))
        {
            result.Warnings.Add("未指定输出路径，将使用默认路径");
        }
        else
        {
            try
            {
                var directory = Path.GetDirectoryName(config.OutputPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    result.Warnings.Add($"输出目录不存在，将自动创建: {directory}");
                }
            }
            catch (Exception ex)
            {
                result.IsValid = false;
                result.Errors.Add($"输出路径格式无效: {ex.Message}");
            }
        }

        // 验证格式支持
        if (!GetSupportedFormats().Contains(config.Format))
        {
            result.IsValid = false;
            result.Errors.Add($"不支持的报告格式: {config.Format}");
        }

        // 验证自定义模板
        if (!string.IsNullOrEmpty(config.CustomTemplatePath))
        {
            if (!File.Exists(config.CustomTemplatePath))
            {
                result.IsValid = false;
                result.Errors.Add($"自定义模板文件不存在: {config.CustomTemplatePath}");
            }
        }

        // 验证图表配置
        if (config.IncludeCharts)
        {
            var chartConfig = config.Charts;
            if (chartConfig.Width <= 0 || chartConfig.Height <= 0)
            {
                result.Warnings.Add("图表尺寸配置可能不合理");
            }
        }

        // 委托给具体生成器进行深度验证
        if (result.IsValid && _generators.TryGetValue(config.Format, out var generator))
        {
            var specificResult = await generator.ValidateConfigurationAsync(config);
            result.Errors.AddRange(specificResult.Errors);
            result.Warnings.AddRange(specificResult.Warnings);
            result.IsValid = result.IsValid && specificResult.IsValid;
        }

        return result;
    }

    /// <inheritdoc />
    public ReportFormat[] GetSupportedFormats()
    {
        return _generators.Keys.ToArray();
    }

    /// <summary>
    /// 预处理报告数据
    /// </summary>
    private async Task<BenchmarkReportData> PreprocessDataAsync(BenchmarkReportData data, ReportConfiguration config)
    {
        var processedData = new BenchmarkReportData
        {
            TestConfig = data.TestConfig,
            Environment = data.Environment,
            Metrics = data.Metrics,
            GeneratedAt = DateTime.UtcNow,
            ReportVersion = data.ReportVersion,
            Summary = data.Summary
        };

        // 生成报告章节
        processedData.Sections = await GenerateReportSectionsAsync(data, config);

        // 数据清理和格式化
        await CleanupDataAsync(processedData, config);

        return processedData;
    }

    /// <summary>
    /// 生成报告章节
    /// </summary>
    private async Task<List<ReportSection>> GenerateReportSectionsAsync(BenchmarkReportData data, ReportConfiguration config)
    {
        var sections = new List<ReportSection>();

        foreach (var sectionType in config.IncludedSections.OrderBy(GetSectionPriority))
        {
            var section = await CreateSectionAsync(sectionType, data, config);
            if (section != null)
            {
                sections.Add(section);
            }
        }

        return sections;
    }

    /// <summary>
    /// 创建报告章节
    /// </summary>
    private async Task<ReportSection?> CreateSectionAsync(ReportSectionType sectionType, BenchmarkReportData data, ReportConfiguration config)
    {
        return sectionType switch
        {
            ReportSectionType.Summary => CreateSummarySection(data),
            ReportSectionType.Configuration => CreateConfigurationSection(data),
            ReportSectionType.Environment => CreateEnvironmentSection(data),
            ReportSectionType.Performance => CreatePerformanceSection(data),
            ReportSectionType.Charts => config.IncludeCharts ? CreateChartsSection(data, config) : null,
            ReportSectionType.Errors => CreateErrorsSection(data),
            ReportSectionType.Recommendations => CreateRecommendationsSection(data),
            ReportSectionType.DetailedData => config.IncludeDetailedData ? CreateDetailedDataSection(data) : null,
            _ => null
        };
    }

    /// <summary>
    /// 创建摘要章节
    /// </summary>
    private ReportSection CreateSummarySection(BenchmarkReportData data)
    {
        return new ReportSection
        {
            Type = ReportSectionType.Summary,
            Title = "测试摘要",
            Priority = GetSectionPriority(ReportSectionType.Summary),
            Data = new Dictionary<string, object>
            {
                ["testDuration"] = data.TestConfig.DurationSeconds,
                ["totalRequests"] = data.Metrics.Throughput.TotalRequests,
                ["averageLatency"] = data.Metrics.Latency.AverageMs,
                ["averageRps"] = data.Metrics.Throughput.AverageRps,
                ["errorRate"] = data.Metrics.Errors.ErrorRate,
                ["performanceGrade"] = data.Summary.Grade,
                ["isSuccessful"] = data.Summary.IsSuccessful
            }
        };
    }

    // 其他创建章节的方法...
    private ReportSection CreateConfigurationSection(BenchmarkReportData data) => new()
    {
        Type = ReportSectionType.Configuration,
        Title = "测试配置",
        Priority = GetSectionPriority(ReportSectionType.Configuration),
        Data = new Dictionary<string, object>
        {
            ["serverAddress"] = data.TestConfig.ServerAddress,
            ["scenarioName"] = data.TestConfig.ScenarioName,
            ["duration"] = data.TestConfig.DurationSeconds,
            ["connections"] = data.TestConfig.ConnectionCount,
            ["requestRate"] = data.TestConfig.RequestRate,
            ["warmupTime"] = data.TestConfig.WarmupSeconds
        }
    };

    private ReportSection CreateEnvironmentSection(BenchmarkReportData data) => new()
    {
        Type = ReportSectionType.Environment,
        Title = "环境信息",
        Priority = GetSectionPriority(ReportSectionType.Environment),
        Data = new Dictionary<string, object>
        {
            ["operatingSystem"] = data.Environment.OperatingSystem,
            ["dotnetVersion"] = data.Environment.DotNetVersion,
            ["processorInfo"] = data.Environment.ProcessorInfo,
            ["totalMemory"] = data.Environment.TotalMemoryMB,
            ["machineName"] = data.Environment.MachineName
        }
    };

    private ReportSection CreatePerformanceSection(BenchmarkReportData data) => new()
    {
        Type = ReportSectionType.Performance,
        Title = "性能指标",
        Priority = GetSectionPriority(ReportSectionType.Performance),
        Data = new Dictionary<string, object>
        {
            ["latency"] = data.Metrics.Latency,
            ["throughput"] = data.Metrics.Throughput,
            ["resources"] = data.Metrics.Resources
        }
    };

    private ReportSection? CreateChartsSection(BenchmarkReportData data, ReportConfiguration config) => new()
    {
        Type = ReportSectionType.Charts,
        Title = "图表分析",
        Priority = GetSectionPriority(ReportSectionType.Charts),
        Data = new Dictionary<string, object>
        {
            ["chartConfig"] = config.Charts,
            ["latencyData"] = data.Metrics.Latency,
            ["throughputData"] = data.Metrics.Throughput,
            ["resourceData"] = data.Metrics.Resources
        }
    };

    private ReportSection CreateErrorsSection(BenchmarkReportData data) => new()
    {
        Type = ReportSectionType.Errors,
        Title = "错误分析",
        Priority = GetSectionPriority(ReportSectionType.Errors),
        Data = new Dictionary<string, object>
        {
            ["errors"] = data.Metrics.Errors
        }
    };

    private ReportSection CreateRecommendationsSection(BenchmarkReportData data) => new()
    {
        Type = ReportSectionType.Recommendations,
        Title = "建议和总结",
        Priority = GetSectionPriority(ReportSectionType.Recommendations),
        Data = new Dictionary<string, object>
        {
            ["keyFindings"] = data.Summary.KeyFindings,
            ["recommendations"] = data.Summary.Recommendations
        }
    };

    private ReportSection? CreateDetailedDataSection(BenchmarkReportData data) => new()
    {
        Type = ReportSectionType.DetailedData,
        Title = "详细数据",
        Priority = GetSectionPriority(ReportSectionType.DetailedData),
        Data = new Dictionary<string, object>
        {
            ["latencyDistribution"] = data.Metrics.Latency.Distribution,
            ["throughputTimeSeries"] = data.Metrics.Throughput.TimeSeries,
            ["resourceTimeSeries"] = data.Metrics.Resources.TimeSeries
        }
    };

    /// <summary>
    /// 数据清理
    /// </summary>
    private async Task CleanupDataAsync(BenchmarkReportData data, ReportConfiguration config)
    {
        // 移除空的或无效的数据
        if (!config.IncludeDetailedData)
        {
            data.Metrics.Latency.Distribution.Clear();
            data.Metrics.Throughput.TimeSeries.Clear();
            data.Metrics.Resources.TimeSeries.Clear();
        }

        // 数据格式化和验证
        await Task.CompletedTask;
    }

    /// <summary>
    /// 确定输出路径
    /// </summary>
    private string DetermineOutputPath(ReportConfiguration config)
    {
        if (!string.IsNullOrWhiteSpace(config.OutputPath))
        {
            return config.OutputPath;
        }

        // 生成默认文件名
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var extension = GetFileExtension(config.Format);
        var defaultFileName = $"benchmark_report_{timestamp}.{extension}";

        return Path.Combine("reports", defaultFileName);
    }

    /// <summary>
    /// 获取文件扩展名
    /// </summary>
    private string GetFileExtension(ReportFormat format)
    {
        return format switch
        {
            ReportFormat.Html => "html",
            ReportFormat.Json => "json",
            ReportFormat.Csv => "csv",
            ReportFormat.Markdown => "md",
            ReportFormat.Pdf => "pdf",
            _ => "txt"
        };
    }

    /// <summary>
    /// 获取章节优先级
    /// </summary>
    private int GetSectionPriority(ReportSectionType sectionType)
    {
        return sectionType switch
        {
            ReportSectionType.Summary => 1,
            ReportSectionType.Configuration => 2,
            ReportSectionType.Environment => 3,
            ReportSectionType.Performance => 4,
            ReportSectionType.Charts => 5,
            ReportSectionType.Errors => 6,
            ReportSectionType.DetailedData => 7,
            ReportSectionType.Recommendations => 8,
            _ => 999
        };
    }
}
