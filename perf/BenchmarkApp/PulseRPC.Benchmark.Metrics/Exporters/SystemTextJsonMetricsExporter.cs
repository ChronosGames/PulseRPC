using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PulseRPC.Benchmark.Metrics.Abstractions;
using PulseRPC.Benchmark.Metrics.Models;

namespace PulseRPC.Benchmark.Metrics.Exporters;

/// <summary>
/// 基于System.Text.Json的指标导出器
/// </summary>
public class SystemTextJsonMetricsExporter : IMetricsExporter
{
    private readonly IJsonSerializationProvider _jsonProvider;
    private readonly ILogger<SystemTextJsonMetricsExporter>? _logger;
    private readonly ExporterConfiguration _configuration;
    private PluginStatus _status = PluginStatus.NotInitialized;

    public SystemTextJsonMetricsExporter(
        IJsonSerializationProvider jsonProvider,
        ExporterConfiguration? configuration = null,
        ILogger<SystemTextJsonMetricsExporter>? logger = null)
    {
        _jsonProvider = jsonProvider;
        _configuration = configuration ?? new ExporterConfiguration();
        _logger = logger;
    }

    #region IMetricsPlugin Implementation

    public string Name => "SystemTextJsonMetricsExporter";
    public string Version => "1.0.0";
    public string Description => "High-performance JSON metrics exporter using System.Text.Json";
    public string Author => "PulseRPC";
    public bool IsInitialized => _status >= PluginStatus.Initialized;
    public bool IsRunning => _status == PluginStatus.Running;

    public ExporterConfiguration Configuration => _configuration;

    public event Action<PluginStatusChangedEventArgs>? StatusChanged;
    public event Action<PluginErrorEventArgs>? ErrorOccurred;
    public event Action<ExportProgressEventArgs>? ExportProgress;

    public Task<bool> ValidateConfigurationAsync(object? configuration)
    {
        if (configuration is not ExporterConfiguration config)
            return Task.FromResult(false);

        // 验证输出路径
        if (string.IsNullOrEmpty(config.OutputPath))
            return Task.FromResult(false);

        // 验证批处理大小
        if (config.BatchSize <= 0)
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

            _logger?.LogInformation("JSON导出器初始化完成: {OutputPath}", _configuration.OutputPath);
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
        _logger?.LogInformation("JSON导出器已启动");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        ChangeStatus(PluginStatus.Stopped);
        _logger?.LogInformation("JSON导出器已停止");
        return Task.CompletedTask;
    }

    public Task<PluginHealthStatus> GetHealthStatusAsync()
    {
        var status = _status == PluginStatus.Running
            ? PluginHealthStatus.Healthy("导出器运行正常")
            : PluginHealthStatus.Unhealthy("导出器未运行", $"当前状态: {_status}");

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

        // 检查快照大小是否在合理范围内
        var estimatedSize = snapshot.EstimateDataSize();
        if (estimatedSize > _configuration.MaxFileSizeBytes)
        {
            _logger?.LogWarning("快照大小超出限制: {Size}bytes > {MaxSize}bytes",
                estimatedSize, _configuration.MaxFileSizeBytes);
            return Task.FromResult(false);
        }

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
            Stream targetStream = fileStream;

            // 如果启用压缩
            if (_configuration.UseCompression)
            {
                targetStream = new GZipStream(fileStream, CompressionLevel.Optimal);
            }

            try
            {
                await _jsonProvider.SerializeAsync(targetStream, snapshot, cancellationToken);

                stopwatch.Stop();
                var fileInfo = new FileInfo(outputPath);

                _logger?.LogDebug("成功导出快照到: {OutputPath}, 大小: {Size}bytes, 耗时: {Duration}ms",
                    outputPath, fileInfo.Length, stopwatch.Elapsed.TotalMilliseconds);

                return ExportResult.Success(1, stopwatch.Elapsed, fileInfo.Length, outputPath);
            }
            finally
            {
                if (targetStream != fileStream)
                {
                    await targetStream.DisposeAsync();
                }
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger?.LogError(ex, "导出快照失败");
            OnError(ex, "导出快照失败");
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
            _logger?.LogInformation("开始批量导出 {Count} 个快照", totalCount);

            var fileName = GenerateBatchFileName();
            var outputPath = Path.Combine(_configuration.OutputPath, fileName);

            await using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
            Stream targetStream = fileStream;

            if (_configuration.UseCompression)
            {
                targetStream = new GZipStream(fileStream, CompressionLevel.Optimal);
            }

            try
            {
                // 写入JSON数组开始
                await targetStream.WriteAsync(System.Text.Encoding.UTF8.GetBytes("["), cancellationToken);

                for (int i = 0; i < snapshotList.Count; i++)
                {
                    try
                    {
                        if (i > 0)
                        {
                            await targetStream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(","), cancellationToken);
                        }

                        await _jsonProvider.SerializeAsync(targetStream, snapshotList[i], cancellationToken);
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

                // 写入JSON数组结束
                await targetStream.WriteAsync(System.Text.Encoding.UTF8.GetBytes("]"), cancellationToken);
            }
            finally
            {
                if (targetStream != fileStream)
                {
                    await targetStream.DisposeAsync();
                }
            }

            stopwatch.Stop();
            var fileInfo = new FileInfo(outputPath);
            totalSize = fileInfo.Length;

            _logger?.LogInformation("批量导出完成: {Exported}/{Total}, 大小: {Size}bytes, 耗时: {Duration}ms",
                exportedCount, totalCount, totalSize, stopwatch.Elapsed.TotalMilliseconds);

            var result = ExportResult.Success(exportedCount, stopwatch.Elapsed, totalSize, outputPath);
            result.ErrorCount = errors.Count;
            result.Errors.AddRange(errors);

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger?.LogError(ex, "批量导出失败");
            OnError(ex, "批量导出失败");
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
            Stream targetStream = outputStream;
            if (_configuration.UseCompression)
            {
                targetStream = new GZipStream(outputStream, CompressionLevel.Optimal, leaveOpen: true);
            }

            try
            {
                // 写入JSON数组开始
                await targetStream.WriteAsync(System.Text.Encoding.UTF8.GetBytes("["), cancellationToken);
                bool first = true;

                await foreach (var snapshot in snapshots.WithCancellation(cancellationToken))
                {
                    try
                    {
                        if (!first)
                        {
                            await targetStream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(","), cancellationToken);
                        }
                        first = false;

                        await _jsonProvider.SerializeAsync(targetStream, snapshot, cancellationToken);
                        exportedCount++;

                        // 定期报告进度
                        if (exportedCount % 100 == 0)
                        {
                            var progress = new ExportProgressEventArgs
                            {
                                ProcessedCount = exportedCount,
                                TotalCount = -1, // 流式处理时总数未知
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

                // 写入JSON数组结束
                await targetStream.WriteAsync(System.Text.Encoding.UTF8.GetBytes("]"), cancellationToken);
            }
            finally
            {
                if (targetStream != outputStream)
                {
                    await targetStream.DisposeAsync();
                }
            }

            stopwatch.Stop();

            _logger?.LogInformation("流式导出完成: {Count} 个快照, 耗时: {Duration}ms",
                exportedCount, stopwatch.Elapsed.TotalMilliseconds);

            var result = ExportResult.Success(exportedCount, stopwatch.Elapsed, outputStream.Length);
            result.ErrorCount = errors.Count;
            result.Errors.AddRange(errors);

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger?.LogError(ex, "流式导出失败");
            OnError(ex, "流式导出失败");
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
            Stream targetStream = fileStream;

            if (_configuration.UseCompression)
            {
                targetStream = new GZipStream(fileStream, CompressionLevel.Optimal);
            }

            try
            {
                await _jsonProvider.SerializeAsync(targetStream, snapshot, cancellationToken);
            }
            finally
            {
                if (targetStream != fileStream)
                {
                    await targetStream.DisposeAsync();
                }
            }

            stopwatch.Stop();
            var fileInfo = new FileInfo(outputPath);

            _logger?.LogDebug("成功导出快照到: {OutputPath}, 大小: {Size}bytes, 耗时: {Duration}ms",
                outputPath, fileInfo.Length, stopwatch.Elapsed.TotalMilliseconds);

            return ExportResult.Success(1, stopwatch.Elapsed, fileInfo.Length, outputPath);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger?.LogError(ex, "导出到文件失败: {OutputPath}", outputPath);
            OnError(ex, $"导出到文件失败: {outputPath}");
            return ExportResult.Failure(ex.Message, stopwatch.Elapsed);
        }
    }

    #endregion

    #region Private Methods

    private string GenerateFileName(JsonOptimizedMetricsSnapshot snapshot)
    {
        var template = _configuration.FileNameTemplate;
        var timestamp = snapshot.Timestamp.ToString("yyyyMMdd_HHmmss");
        var fileName = template
            .Replace("{timestamp}", timestamp)
            .Replace("{sequence}", snapshot.SequenceNumber.ToString())
            .Replace("{collector}", snapshot.CollectorName);

        if (_configuration.UseCompression && !fileName.EndsWith(".gz"))
        {
            fileName += ".gz";
        }

        return fileName;
    }

    private string GenerateBatchFileName()
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var fileName = $"metrics_batch_{timestamp}.json";

        if (_configuration.UseCompression)
        {
            fileName += ".gz";
        }

        return fileName;
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
