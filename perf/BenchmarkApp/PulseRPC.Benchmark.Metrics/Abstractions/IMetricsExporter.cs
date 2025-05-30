using PulseRPC.Benchmark.Metrics.Models;

namespace PulseRPC.Benchmark.Metrics.Abstractions;

/// <summary>
/// 指标导出器接口
/// </summary>
public interface IMetricsExporter : IMetricsPlugin
{
    /// <summary>
    /// 检查是否可以导出指定快照
    /// </summary>
    /// <param name="snapshot">指标快照</param>
    /// <returns>是否可以导出</returns>
    Task<bool> CanExportAsync(JsonOptimizedMetricsSnapshot snapshot);

    /// <summary>
    /// 导出单个指标快照
    /// </summary>
    /// <param name="snapshot">指标快照</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>导出结果</returns>
    Task<ExportResult> ExportAsync(JsonOptimizedMetricsSnapshot snapshot, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量导出指标快照
    /// </summary>
    /// <param name="snapshots">指标快照集合</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>导出结果</returns>
    Task<ExportResult> ExportBatchAsync(IEnumerable<JsonOptimizedMetricsSnapshot> snapshots, CancellationToken cancellationToken = default);

    /// <summary>
    /// 流式导出指标快照（用于大数据量）
    /// </summary>
    /// <param name="snapshots">异步指标快照流</param>
    /// <param name="outputStream">输出流</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>导出结果</returns>
    Task<ExportResult> ExportStreamAsync(IAsyncEnumerable<JsonOptimizedMetricsSnapshot> snapshots, Stream outputStream, CancellationToken cancellationToken = default);

    /// <summary>
    /// 导出到指定路径
    /// </summary>
    /// <param name="snapshot">指标快照</param>
    /// <param name="outputPath">输出路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>导出结果</returns>
    Task<ExportResult> ExportToFileAsync(JsonOptimizedMetricsSnapshot snapshot, string outputPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取导出器配置
    /// </summary>
    ExporterConfiguration Configuration { get; }

    /// <summary>
    /// 导出进度事件
    /// </summary>
    event Action<ExportProgressEventArgs>? ExportProgress;
}

/// <summary>
/// 导出结果
/// </summary>
public class ExportResult
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// 导出的记录数
    /// </summary>
    public int ExportedCount { get; set; }

    /// <summary>
    /// 错误记录数
    /// </summary>
    public int ErrorCount { get; set; }

    /// <summary>
    /// 导出耗时
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// 输出大小（字节）
    /// </summary>
    public long OutputSizeBytes { get; set; }

    /// <summary>
    /// 错误消息
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 详细错误列表
    /// </summary>
    public List<string> Errors { get; set; } = new();

    /// <summary>
    /// 输出路径或标识
    /// </summary>
    public string? OutputLocation { get; set; }

    /// <summary>
    /// 导出格式
    /// </summary>
    public string? ExportFormat { get; set; }

    /// <summary>
    /// 添加错误
    /// </summary>
    /// <param name="error">错误消息</param>
    public void AddError(string error)
    {
        Errors.Add(error);
        ErrorCount++;
    }

    /// <summary>
    /// 创建成功结果
    /// </summary>
    /// <param name="exportedCount">导出数量</param>
    /// <param name="duration">耗时</param>
    /// <param name="outputSize">输出大小</param>
    /// <param name="outputLocation">输出位置</param>
    /// <returns>成功结果</returns>
    public static ExportResult Success(int exportedCount, TimeSpan duration, long outputSize = 0, string? outputLocation = null)
    {
        return new ExportResult
        {
            IsSuccess = true,
            ExportedCount = exportedCount,
            Duration = duration,
            OutputSizeBytes = outputSize,
            OutputLocation = outputLocation
        };
    }

    /// <summary>
    /// 创建失败结果
    /// </summary>
    /// <param name="errorMessage">错误消息</param>
    /// <param name="duration">耗时</param>
    /// <returns>失败结果</returns>
    public static ExportResult Failure(string errorMessage, TimeSpan duration = default)
    {
        return new ExportResult
        {
            IsSuccess = false,
            ErrorMessage = errorMessage,
            Duration = duration,
            ErrorCount = 1,
            Errors = new List<string> { errorMessage }
        };
    }
}

/// <summary>
/// 导出器配置
/// </summary>
public class ExporterConfiguration
{
    /// <summary>
    /// 输出路径
    /// </summary>
    public string OutputPath { get; set; } = string.Empty;

    /// <summary>
    /// 是否启用压缩
    /// </summary>
    public bool UseCompression { get; set; } = false;

    /// <summary>
    /// 文件名模板
    /// </summary>
    public string FileNameTemplate { get; set; } = "metrics_{timestamp}.json";

    /// <summary>
    /// 最大文件大小（字节）
    /// </summary>
    public long MaxFileSizeBytes { get; set; } = 100 * 1024 * 1024; // 100MB

    /// <summary>
    /// 批处理大小
    /// </summary>
    public int BatchSize { get; set; } = 1000;

    /// <summary>
    /// 流式处理阈值
    /// </summary>
    public int StreamingThreshold { get; set; } = 10000;

    /// <summary>
    /// 是否包含元数据
    /// </summary>
    public bool IncludeMetadata { get; set; } = true;

    /// <summary>
    /// 格式化选项
    /// </summary>
    public Dictionary<string, object> FormatOptions { get; set; } = new();
}

/// <summary>
/// 导出进度事件参数
/// </summary>
public class ExportProgressEventArgs : EventArgs
{
    /// <summary>
    /// 已处理数量
    /// </summary>
    public int ProcessedCount { get; set; }

    /// <summary>
    /// 总数量
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// 进度百分比（0-100）
    /// </summary>
    public double ProgressPercentage => TotalCount > 0 ? (double)ProcessedCount / TotalCount * 100 : 0;

    /// <summary>
    /// 当前状态
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// 已用时间
    /// </summary>
    public TimeSpan ElapsedTime { get; set; }

    /// <summary>
    /// 估计剩余时间
    /// </summary>
    public TimeSpan? EstimatedRemainingTime { get; set; }
}
