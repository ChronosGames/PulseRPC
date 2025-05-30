using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PulseRPC.Benchmark.Metrics.Models;

namespace PulseRPC.Benchmark.Metrics.Exporters;

/// <summary>
/// JSON格式报告导出器
/// </summary>
public class JsonReportExporter : IBenchmarkReportGenerator
{
    private readonly ILogger _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public JsonReportExporter(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() },
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    /// <inheritdoc />
    public async Task<string> GenerateReportAsync(BenchmarkReportData data, ReportConfiguration config)
    {
        try
        {
            _logger.LogDebug("开始生成JSON报告");

            var reportJson = JsonSerializer.Serialize(data, _jsonOptions);

            _logger.LogDebug("JSON报告生成完成，大小: {Size} 字符", reportJson.Length);

            return await Task.FromResult(reportJson);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "生成JSON报告失败: {Message}", ex.Message);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<byte[]> GenerateReportBytesAsync(BenchmarkReportData data, ReportConfiguration config)
    {
        var content = await GenerateReportAsync(data, config);
        return System.Text.Encoding.UTF8.GetBytes(content);
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

        // JSON格式没有特殊的验证要求
        await Task.CompletedTask;

        return result;
    }

    /// <inheritdoc />
    public ReportFormat[] GetSupportedFormats()
    {
        return new[] { ReportFormat.Json };
    }
} 