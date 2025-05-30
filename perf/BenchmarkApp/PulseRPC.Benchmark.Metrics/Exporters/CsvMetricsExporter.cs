using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using PulseRPC.Benchmark.Metrics.Abstractions;
using PulseRPC.Benchmark.Metrics.Models;

namespace PulseRPC.Benchmark.Metrics.Exporters;

/// <summary>
/// CSV格式指标导出器
/// </summary>
public class CsvMetricsExporter : IMetricsExporter
{
    private readonly ILogger<CsvMetricsExporter>? _logger;
    private readonly CsvExporterConfiguration _configuration;
    private PluginStatus _status = PluginStatus.NotInitialized;

    public CsvMetricsExporter(
        CsvExporterConfiguration? configuration = null,
        ILogger<CsvMetricsExporter>? logger = null)
    {
        _configuration = configuration ?? new CsvExporterConfiguration();
        _logger = logger;
    }

    #region IMetricsPlugin Implementation

    public string Name => "CsvMetricsExporter";
    public string Version => "1.0.0";
    public string Description => "CSV format metrics exporter with Excel compatibility";
    public string Author => "PulseRPC";
    public bool IsInitialized => _status >= PluginStatus.Initialized;
    public bool IsRunning => _status == PluginStatus.Running;

    public ExporterConfiguration Configuration => _configuration;

    public event Action<PluginStatusChangedEventArgs>? StatusChanged;
    public event Action<PluginErrorEventArgs>? ErrorOccurred;
    public event Action<ExportProgressEventArgs>? ExportProgress;

    public Task<bool> ValidateConfigurationAsync(object? configuration)
    {
        if (configuration is not CsvExporterConfiguration config)
            return Task.FromResult(false);

        // 验证输出路径
        if (string.IsNullOrEmpty(config.OutputPath))
            return Task.FromResult(false);

        return Task.FromResult(true);
    }

    public Task InitializeAsync(object? configuration, CancellationToken cancellationToken = default)
    {
        try
        {
            ChangeStatus(PluginStatus.Initialized);

            // 确保输出目录存在
            if (!string.IsNullOrEmpty(_configuration.OutputPath))
            {
                Directory.CreateDirectory(_configuration.OutputPath);
            }

            _logger?.LogInformation("CSV导出器初始化完成: {OutputPath}", _configuration.OutputPath);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            ChangeStatus(PluginStatus.Error);
            OnError(ex, "初始化失败");
            throw;
        }
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        ChangeStatus(PluginStatus.Running);
        _logger?.LogInformation("CSV导出器已启动");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        ChangeStatus(PluginStatus.Stopped);
        _logger?.LogInformation("CSV导出器已停止");
        return Task.CompletedTask;
    }

    public Task<PluginHealthStatus> GetHealthStatusAsync()
    {
        var status = _status == PluginStatus.Running
            ? PluginHealthStatus.Healthy("CSV导出器运行正常")
            : PluginHealthStatus.Unhealthy("CSV导出器未运行", $"当前状态: {_status}");

        return Task.FromResult(status);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        ChangeStatus(PluginStatus.Disposed);
    }

    #endregion

    #region IMetricsExporter Implementation

    public Task<bool> CanExportAsync(JsonOptimizedMetricsSnapshot snapshot)
    {
        if (snapshot == null) return Task.FromResult(false);
        if (!IsRunning) return Task.FromResult(false);

        return Task.FromResult(true);
    }

    public async Task<ExportResult> ExportAsync(JsonOptimizedMetricsSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (!await CanExportAsync(snapshot))
            {
                return ExportResult.Failure("无法导出快照", stopwatch.Elapsed);
            }

            var fileName = GenerateFileName(snapshot);
            var outputPath = Path.Combine(_configuration.OutputPath, fileName);

            await using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
            using var writer = new StreamWriter(fileStream, Encoding.UTF8);

            await WriteCsvDataAsync(writer, snapshot, cancellationToken);

            stopwatch.Stop();
            var fileInfo = new FileInfo(outputPath);

            _logger?.LogDebug("成功导出CSV到: {OutputPath}, 大小: {Size}bytes, 耗时: {Duration}ms",
                outputPath, fileInfo.Length, stopwatch.Elapsed.TotalMilliseconds);

            return ExportResult.Success(1, stopwatch.Elapsed, fileInfo.Length, outputPath);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger?.LogError(ex, "导出CSV失败");
            OnError(ex, "导出CSV失败");
            return ExportResult.Failure(ex.Message, stopwatch.Elapsed);
        }
    }

    public async Task<ExportResult> ExportBatchAsync(IEnumerable<JsonOptimizedMetricsSnapshot> snapshots, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var snapshotList = snapshots.ToList();
        var totalCount = snapshotList.Count;
        var exportedCount = 0;
        var totalSize = 0L;
        var errors = new List<string>();

        try
        {
            _logger?.LogInformation("开始批量导出 {Count} 个快照到CSV", totalCount);

            var fileName = GenerateBatchFileName();
            var outputPath = Path.Combine(_configuration.OutputPath, fileName);

            await using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
            using var writer = new StreamWriter(fileStream, Encoding.UTF8);

            // 写入CSV头部
            await WriteCsvHeaderAsync(writer);

            for (int i = 0; i < snapshotList.Count; i++)
            {
                try
                {
                    await WriteCsvDataAsync(writer, snapshotList[i], cancellationToken, writeHeader: false);
                    exportedCount++;

                    // 报告进度
                    var progress = new ExportProgressEventArgs
                    {
                        ProcessedCount = exportedCount,
                        TotalCount = totalCount,
                        Status = $"已导出 {exportedCount}/{totalCount}",
                        ElapsedTime = stopwatch.Elapsed
                    };
                    ExportProgress?.Invoke(progress);
                }
                catch (Exception ex)
                {
                    errors.Add($"快照 {i}: {ex.Message}");
                    _logger?.LogWarning(ex, "导出快照 {Index} 失败", i);
                }
            }

            stopwatch.Stop();
            var fileInfo = new FileInfo(outputPath);
            totalSize = fileInfo.Length;

            _logger?.LogInformation("批量CSV导出完成: {Exported}/{Total}, 大小: {Size}bytes, 耗时: {Duration}ms",
                exportedCount, totalCount, totalSize, stopwatch.Elapsed.TotalMilliseconds);

            var result = ExportResult.Success(exportedCount, stopwatch.Elapsed, totalSize, outputPath);
            result.ErrorCount = errors.Count;
            result.Errors.AddRange(errors);

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger?.LogError(ex, "批量CSV导出失败");
            OnError(ex, "批量CSV导出失败");
            return ExportResult.Failure(ex.Message, stopwatch.Elapsed);
        }
    }

    public async Task<ExportResult> ExportStreamAsync(IAsyncEnumerable<JsonOptimizedMetricsSnapshot> snapshots, Stream outputStream, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var exportedCount = 0;
        var errors = new List<string>();

        try
        {
            using var writer = new StreamWriter(outputStream, Encoding.UTF8, leaveOpen: true);

            // 写入CSV头部
            await WriteCsvHeaderAsync(writer);
            bool first = true;

            await foreach (var snapshot in snapshots.WithCancellation(cancellationToken))
            {
                try
                {
                    await WriteCsvDataAsync(writer, snapshot, cancellationToken, writeHeader: false);
                    exportedCount++;

                    // 定期报告进度
                    if (exportedCount % 100 == 0)
                    {
                        var progress = new ExportProgressEventArgs
                        {
                            ProcessedCount = exportedCount,
                            TotalCount = -1,
                            Status = $"已处理 {exportedCount} 个快照",
                            ElapsedTime = stopwatch.Elapsed
                        };
                        ExportProgress?.Invoke(progress);
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"快照 {exportedCount}: {ex.Message}");
                    _logger?.LogWarning(ex, "流式导出快照失败");
                }
            }

            stopwatch.Stop();

            _logger?.LogInformation("流式CSV导出完成: {Count} 个快照, 耗时: {Duration}ms",
                exportedCount, stopwatch.Elapsed.TotalMilliseconds);

            var result = ExportResult.Success(exportedCount, stopwatch.Elapsed, outputStream.Length);
            result.ErrorCount = errors.Count;
            result.Errors.AddRange(errors);

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger?.LogError(ex, "流式CSV导出失败");
            OnError(ex, "流式CSV导出失败");
            return ExportResult.Failure(ex.Message, stopwatch.Elapsed);
        }
    }

    public async Task<ExportResult> ExportToFileAsync(JsonOptimizedMetricsSnapshot snapshot, string outputPath, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (!await CanExportAsync(snapshot))
            {
                return ExportResult.Failure("无法导出快照", stopwatch.Elapsed);
            }

            // 确保目录存在
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
            using var writer = new StreamWriter(fileStream, Encoding.UTF8);

            await WriteCsvDataAsync(writer, snapshot, cancellationToken);

            stopwatch.Stop();
            var fileInfo = new FileInfo(outputPath);

            _logger?.LogDebug("成功导出CSV到: {OutputPath}, 大小: {Size}bytes, 耗时: {Duration}ms",
                outputPath, fileInfo.Length, stopwatch.Elapsed.TotalMilliseconds);

            return ExportResult.Success(1, stopwatch.Elapsed, fileInfo.Length, outputPath);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger?.LogError(ex, "导出CSV到文件失败: {OutputPath}", outputPath);
            OnError(ex, $"导出CSV到文件失败: {outputPath}");
            return ExportResult.Failure(ex.Message, stopwatch.Elapsed);
        }
    }

    #endregion

    #region CSV Writing Methods

    private async Task WriteCsvHeaderAsync(StreamWriter writer)
    {
        var headers = new List<string>
        {
            "Timestamp",
            "Collector",
            "Sequence",
            "MetricName",
            "MetricType",
            "Value",
            "Unit",
            "Source",
            "Tags"
        };

        if (_configuration.IncludeAdditionalColumns)
        {
            headers.AddRange(new[] { "CollectionDuration", "MetricsCount", "SamplingConfig" });
        }

        var headerLine = string.Join(_configuration.Delimiter, headers.Select(EscapeCsvField));
        await writer.WriteLineAsync(headerLine);
    }

    private async Task WriteCsvDataAsync(StreamWriter writer, JsonOptimizedMetricsSnapshot snapshot, CancellationToken cancellationToken, bool writeHeader = true)
    {
        if (writeHeader)
        {
            await WriteCsvHeaderAsync(writer);
        }

        foreach (var metric in snapshot.Metrics)
        {
            var fields = new List<string>
            {
                snapshot.Timestamp.ToString(_configuration.DateTimeFormat, CultureInfo.InvariantCulture),
                EscapeCsvField(snapshot.CollectorName),
                snapshot.SequenceNumber.ToString(),
                EscapeCsvField(metric.Value.MetricName),
                metric.Value.Type.ToString(),
                EscapeCsvField(GetMetricValueAsString(metric.Value)),
                EscapeCsvField(metric.Value.Unit ?? ""),
                EscapeCsvField(metric.Value.Source ?? ""),
                EscapeCsvField(FormatTags(metric.Value.Tags))
            };

            if (_configuration.IncludeAdditionalColumns)
            {
                fields.Add(snapshot.CollectionDuration.TotalMilliseconds.ToString("F2", CultureInfo.InvariantCulture));
                fields.Add(snapshot.MetricsCount.ToString());
                fields.Add(EscapeCsvField(FormatSamplingConfig(snapshot.Sampling)));
            }

            var line = string.Join(_configuration.Delimiter, fields);
            await writer.WriteLineAsync(line);

            if (cancellationToken.IsCancellationRequested)
                break;
        }
    }

    private string GetMetricValueAsString(JsonOptimizedMetricsEvent metric)
    {
        try
        {
            var valueElement = metric.Value;
            return valueElement.ValueKind switch
            {
                System.Text.Json.JsonValueKind.String => valueElement.GetString() ?? "",
                System.Text.Json.JsonValueKind.Number when valueElement.TryGetDouble(out var doubleVal) =>
                    doubleVal.ToString(_configuration.NumberFormat, CultureInfo.InvariantCulture),
                System.Text.Json.JsonValueKind.True => "TRUE",
                System.Text.Json.JsonValueKind.False => "FALSE",
                System.Text.Json.JsonValueKind.Null => "",
                _ => valueElement.ToString()
            };
        }
        catch
        {
            return metric.GetStringValue() ?? "";
        }
    }

    private string FormatTags(Dictionary<string, string> tags)
    {
        if (tags == null || tags.Count == 0)
            return "";

        var tagPairs = tags.Select(kvp => $"{kvp.Key}={kvp.Value}");
        return string.Join(_configuration.TagSeparator, tagPairs);
    }

    private string FormatSamplingConfig(SamplingConfig? config)
    {
        if (config == null)
            return "";

        var configPairs = new List<string>
        {
            $"Rate={config.Rate}",
            $"Strategy={config.Strategy}",
            $"IntervalMs={config.IntervalMs}",
            $"MaxSamples={config.MaxSamples}"
        };
        return string.Join(";", configPairs);
    }

    private string EscapeCsvField(string field)
    {
        if (string.IsNullOrEmpty(field))
            return "";

        // 如果字段包含分隔符、引号或换行符，需要转义
        if (field.Contains(_configuration.Delimiter) ||
            field.Contains('"') ||
            field.Contains('\n') ||
            field.Contains('\r'))
        {
            // 转义引号并用引号包围
            var escaped = field.Replace("\"", "\"\"");
            return $"\"{escaped}\"";
        }

        return field;
    }

    #endregion

    #region Private Methods

    private string GenerateFileName(JsonOptimizedMetricsSnapshot snapshot)
    {
        var template = _configuration.FileNameTemplate;
        var timestamp = snapshot.Timestamp.ToString("yyyyMMdd_HHmmss");

        return template
            .Replace("{timestamp}", timestamp)
            .Replace("{sequence}", snapshot.SequenceNumber.ToString())
            .Replace("{collector}", snapshot.CollectorName)
            .Replace("{extension}", ".csv");
    }

    private string GenerateBatchFileName()
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        return $"metrics_batch_{timestamp}.csv";
    }

    private void ChangeStatus(PluginStatus newStatus)
    {
        var oldStatus = _status;
        _status = newStatus;

        if (oldStatus != newStatus)
        {
            StatusChanged?.Invoke(new PluginStatusChangedEventArgs(Name, oldStatus, newStatus));
        }
    }

    private void OnError(Exception exception, string context)
    {
        ErrorOccurred?.Invoke(new PluginErrorEventArgs(Name, exception, ErrorLevel.Error, context));
    }

    #endregion
}

/// <summary>
/// CSV导出器配置
/// </summary>
public class CsvExporterConfiguration : ExporterConfiguration
{
    /// <summary>
    /// CSV分隔符
    /// </summary>
    public string Delimiter { get; set; } = ",";

    /// <summary>
    /// 日期时间格式
    /// </summary>
    public string DateTimeFormat { get; set; } = "yyyy-MM-dd HH:mm:ss.fff";

    /// <summary>
    /// 数字格式
    /// </summary>
    public string NumberFormat { get; set; } = "F6";

    /// <summary>
    /// 标签分隔符
    /// </summary>
    public string TagSeparator { get; set; } = ";";

    /// <summary>
    /// 是否包含额外列
    /// </summary>
    public bool IncludeAdditionalColumns { get; set; } = true;

    public CsvExporterConfiguration()
    {
        FileNameTemplate = "metrics_{timestamp}_{collector}.csv";
        FormatOptions["encoding"] = "UTF-8";
        FormatOptions["excel_compatible"] = true;
    }
}
