using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PulseRPC.Benchmark.Metrics.Models;

namespace PulseRPC.Benchmark.Metrics.Exporters;

/// <summary>
/// CSV格式报告导出器
/// </summary>
public class CsvReportExporter : IBenchmarkReportGenerator
{
    private readonly ILogger _logger;

    public CsvReportExporter(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<string> GenerateReportAsync(BenchmarkReportData data, ReportConfiguration config)
    {
        try
        {
            _logger.LogDebug("开始生成CSV报告");

            var csvBuilder = new StringBuilder();

            // 生成基本信息部分
            await AppendBasicInfoAsync(csvBuilder, data);

            // 生成延迟数据部分
            await AppendLatencyDataAsync(csvBuilder, data);

            // 生成吞吐量数据部分
            await AppendThroughputDataAsync(csvBuilder, data);

            // 生成资源使用数据部分
            await AppendResourceDataAsync(csvBuilder, data);

            // 生成错误数据部分
            await AppendErrorDataAsync(csvBuilder, data);

            var result = csvBuilder.ToString();
            _logger.LogDebug("CSV报告生成完成，大小: {Size} 字符", result.Length);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "生成CSV报告失败: {Message}", ex.Message);
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

        // CSV格式没有特殊的验证要求
        await Task.CompletedTask;

        return result;
    }

    /// <inheritdoc />
    public ReportFormat[] GetSupportedFormats()
    {
        return new[] { ReportFormat.Csv };
    }

    /// <summary>
    /// 添加基本信息
    /// </summary>
    private async Task AppendBasicInfoAsync(StringBuilder csvBuilder, BenchmarkReportData data)
    {
        csvBuilder.AppendLine("# 基本信息");
        csvBuilder.AppendLine("键,值");
        csvBuilder.AppendLine($"报告生成时间,{data.GeneratedAt:yyyy-MM-dd HH:mm:ss}");
        csvBuilder.AppendLine($"报告版本,{data.ReportVersion}");
        csvBuilder.AppendLine($"服务器地址,{data.TestConfig.ServerAddress}");
        csvBuilder.AppendLine($"测试场景,{data.TestConfig.ScenarioName}");
        csvBuilder.AppendLine($"测试持续时间（秒）,{data.TestConfig.DurationSeconds}");
        csvBuilder.AppendLine($"并发连接数,{data.TestConfig.ConnectionCount}");
        csvBuilder.AppendLine($"请求速率,{data.TestConfig.RequestRate}");
        csvBuilder.AppendLine($"预热时间（秒）,{data.TestConfig.WarmupSeconds}");
        csvBuilder.AppendLine($"操作系统,{data.Environment.OperatingSystem}");
        csvBuilder.AppendLine($".NET版本,{data.Environment.DotNetVersion}");
        csvBuilder.AppendLine($"处理器信息,{data.Environment.ProcessorInfo}");
        csvBuilder.AppendLine($"总内存（MB）,{data.Environment.TotalMemoryMB}");
        csvBuilder.AppendLine($"机器名称,{data.Environment.MachineName}");
        csvBuilder.AppendLine($"测试是否成功,{data.Summary.IsSuccessful}");
        csvBuilder.AppendLine($"性能等级,{data.Summary.Grade}");
        csvBuilder.AppendLine();

        await Task.CompletedTask;
    }

    /// <summary>
    /// 添加延迟数据
    /// </summary>
    private async Task AppendLatencyDataAsync(StringBuilder csvBuilder, BenchmarkReportData data)
    {
        csvBuilder.AppendLine("# 延迟统计");
        csvBuilder.AppendLine("指标,值（毫秒）");
        csvBuilder.AppendLine($"平均延迟,{data.Metrics.Latency.AverageMs:F2}");
        csvBuilder.AppendLine($"最小延迟,{data.Metrics.Latency.MinMs:F2}");
        csvBuilder.AppendLine($"最大延迟,{data.Metrics.Latency.MaxMs:F2}");
        csvBuilder.AppendLine($"P50延迟,{data.Metrics.Latency.P50Ms:F2}");
        csvBuilder.AppendLine($"P95延迟,{data.Metrics.Latency.P95Ms:F2}");
        csvBuilder.AppendLine($"P99延迟,{data.Metrics.Latency.P99Ms:F2}");
        csvBuilder.AppendLine($"P99.9延迟,{data.Metrics.Latency.P999Ms:F2}");
        csvBuilder.AppendLine($"标准差,{data.Metrics.Latency.StandardDeviation:F2}");
        csvBuilder.AppendLine();

        // 延迟分布数据
        if (data.Metrics.Latency.Distribution.Any())
        {
            csvBuilder.AppendLine("# 延迟分布");
            csvBuilder.AppendLine("时间戳,延迟（毫秒）");
            foreach (var point in data.Metrics.Latency.Distribution)
            {
                csvBuilder.AppendLine($"{point.Timestamp:yyyy-MM-dd HH:mm:ss.fff},{point.LatencyMs:F2}");
            }
            csvBuilder.AppendLine();
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// 添加吞吐量数据
    /// </summary>
    private async Task AppendThroughputDataAsync(StringBuilder csvBuilder, BenchmarkReportData data)
    {
        csvBuilder.AppendLine("# 吞吐量统计");
        csvBuilder.AppendLine("指标,值");
        csvBuilder.AppendLine($"平均RPS,{data.Metrics.Throughput.AverageRps:F2}");
        csvBuilder.AppendLine($"峰值RPS,{data.Metrics.Throughput.PeakRps:F2}");
        csvBuilder.AppendLine($"总请求数,{data.Metrics.Throughput.TotalRequests}");
        csvBuilder.AppendLine($"成功请求数,{data.Metrics.Throughput.SuccessfulRequests}");
        csvBuilder.AppendLine($"失败请求数,{data.Metrics.Throughput.FailedRequests}");
        csvBuilder.AppendLine();

        // 吞吐量时间序列数据
        if (data.Metrics.Throughput.TimeSeries.Any())
        {
            csvBuilder.AppendLine("# 吞吐量时间序列");
            csvBuilder.AppendLine("时间戳,RPS");
            foreach (var point in data.Metrics.Throughput.TimeSeries)
            {
                csvBuilder.AppendLine($"{point.Timestamp:yyyy-MM-dd HH:mm:ss},{point.Rps:F2}");
            }
            csvBuilder.AppendLine();
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// 添加资源使用数据
    /// </summary>
    private async Task AppendResourceDataAsync(StringBuilder csvBuilder, BenchmarkReportData data)
    {
        csvBuilder.AppendLine("# 资源使用统计");
        csvBuilder.AppendLine("指标,值");
        csvBuilder.AppendLine($"CPU使用率（%）,{data.Metrics.Resources.CpuUsagePercent:F2}");
        csvBuilder.AppendLine($"内存使用量（MB）,{data.Metrics.Resources.MemoryUsageMB}");
        csvBuilder.AppendLine($"网络发送量（Bytes）,{data.Metrics.Resources.NetworkSentBytes}");
        csvBuilder.AppendLine($"网络接收量（Bytes）,{data.Metrics.Resources.NetworkReceivedBytes}");
        csvBuilder.AppendLine();

        // 资源使用时间序列数据
        if (data.Metrics.Resources.TimeSeries.Any())
        {
            csvBuilder.AppendLine("# 资源使用时间序列");
            csvBuilder.AppendLine("时间戳,CPU（%）,内存（MB）");
            foreach (var point in data.Metrics.Resources.TimeSeries)
            {
                csvBuilder.AppendLine($"{point.Timestamp:yyyy-MM-dd HH:mm:ss},{point.CpuPercent:F2},{point.MemoryMB}");
            }
            csvBuilder.AppendLine();
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// 添加错误数据
    /// </summary>
    private async Task AppendErrorDataAsync(StringBuilder csvBuilder, BenchmarkReportData data)
    {
        csvBuilder.AppendLine("# 错误统计");
        csvBuilder.AppendLine("指标,值");
        csvBuilder.AppendLine($"总错误数,{data.Metrics.Errors.TotalErrors}");
        csvBuilder.AppendLine($"错误率（%）,{data.Metrics.Errors.ErrorRate:F2}");
        csvBuilder.AppendLine($"超时错误数,{data.Metrics.Errors.TimeoutErrors}");
        csvBuilder.AppendLine($"连接错误数,{data.Metrics.Errors.ConnectionErrors}");
        csvBuilder.AppendLine();

        // 错误类型分布
        if (data.Metrics.Errors.ErrorsByType.Any())
        {
            csvBuilder.AppendLine("# 错误类型分布");
            csvBuilder.AppendLine("错误类型,数量");
            foreach (var error in data.Metrics.Errors.ErrorsByType)
            {
                csvBuilder.AppendLine($"{error.Key},{error.Value}");
            }
            csvBuilder.AppendLine();
        }

        await Task.CompletedTask;
    }
} 