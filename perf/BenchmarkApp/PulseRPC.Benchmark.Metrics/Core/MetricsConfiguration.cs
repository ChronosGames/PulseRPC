using System.Text.Json;
using PulseRPC.Benchmark.Metrics.Abstractions;

namespace PulseRPC.Benchmark.Metrics.Core;

/// <summary>
/// 指标系统配置管理器
/// </summary>
public class MetricsConfiguration
{
    /// <summary>
    /// 序列化配置
    /// </summary>
    public SerializationConfiguration Serialization { get; set; } = new();

    /// <summary>
    /// 聚合器配置
    /// </summary>
    public Dictionary<string, AggregatorConfiguration> Aggregators { get; set; } = new();

    /// <summary>
    /// 分析器配置
    /// </summary>
    public Dictionary<string, AnalyzerConfiguration> Analyzers { get; set; } = new();

    /// <summary>
    /// 导出器配置
    /// </summary>
    public Dictionary<string, ExporterConfiguration> Exporters { get; set; } = new();

    /// <summary>
    /// 收集器配置
    /// </summary>
    public Dictionary<string, CollectorConfiguration> Collectors { get; set; } = new();

    /// <summary>
    /// 全局配置
    /// </summary>
    public GlobalMetricsConfiguration Global { get; set; } = new();

    /// <summary>
    /// 插件配置
    /// </summary>
    public Dictionary<string, PluginConfiguration> Plugins { get; set; } = new();

    /// <summary>
    /// 从JSON配置文件加载
    /// </summary>
    public static async Task<MetricsConfiguration> LoadFromFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            var defaultConfig = CreateDefault();
            await SaveToFileAsync(defaultConfig, filePath, cancellationToken);
            return defaultConfig;
        }

        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        return JsonSerializer.Deserialize<MetricsConfiguration>(json, JsonOptions.Default) ?? CreateDefault();
    }

    /// <summary>
    /// 保存到JSON配置文件
    /// </summary>
    public static async Task SaveToFileAsync(MetricsConfiguration configuration, string filePath, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(configuration, JsonOptions.Pretty);
        await File.WriteAllTextAsync(filePath, json, cancellationToken);
    }

    /// <summary>
    /// 创建默认配置
    /// </summary>
    public static MetricsConfiguration CreateDefault()
    {
        return new MetricsConfiguration
        {
            Aggregators = new Dictionary<string, AggregatorConfiguration>
            {
                ["TimeWindow"] = new AggregatorConfiguration
                {
                    EnableStatistics = true,
                    EnableParallelProcessing = true,
                    DefaultWindowConfig = new TimeWindowConfiguration
                    {
                        WindowSize = TimeSpan.FromMinutes(5),
                        SlideInterval = TimeSpan.FromMinutes(1),
                        WindowType = WindowType.Sliding,
                        MaxWindows = 100,
                        AutoCleanup = true,
                        ExpirationTime = TimeSpan.FromHours(1)
                    }
                },
                ["Statistical"] = new AggregatorConfiguration
                {
                    EnableStatistics = true,
                    EnableParallelProcessing = true,
                    Percentiles = new List<int> { 50, 90, 95, 99 },
                    MaxMemoryUsage = 100 * 1024 * 1024,
                    BufferSize = 10000
                }
            },
            Analyzers = new Dictionary<string, AnalyzerConfiguration>
            {
                ["Trend"] = new AnalyzerConfiguration
                {
                    EnableTrendAnalysis = true,
                    TrendAnalysisWindowSize = 50,
                    EnableInsightGeneration = true,
                    InsightConfidenceThreshold = 0.8,
                    MaxCacheSize = 10000,
                    DefaultAnomalyDetection = new AnomalyDetectionConfiguration
                    {
                        Method = AnomalyDetectionMethod.StatisticalOutlier,
                        Sensitivity = 0.95,
                        WindowSize = 100,
                        StandardDeviationMultiple = 3.0
                    }
                }
            },
            Exporters = new Dictionary<string, ExporterConfiguration>
            {
                ["Json"] = new ExporterConfiguration
                {
                    OutputPath = "metrics_export.json",
                    MaxFileSize = 10 * 1024 * 1024,
                    EnableCompression = true,
                    ExportFormat = "JSON"
                },
                ["Csv"] = new ExporterConfiguration
                {
                    OutputPath = "metrics_export.csv",
                    MaxFileSize = 10 * 1024 * 1024,
                    ExportFormat = "CSV"
                }
            },
            Collectors = new Dictionary<string, CollectorConfiguration>
            {
                ["RealTime"] = new CollectorConfiguration
                {
                    EnableBuffering = true,
                    BufferSize = 1000,
                    FlushInterval = TimeSpan.FromSeconds(5),
                    MaxMemoryUsage = 50 * 1024 * 1024
                },
                ["Batch"] = new CollectorConfiguration
                {
                    EnableBuffering = true,
                    BufferSize = 10000,
                    FlushInterval = TimeSpan.FromMinutes(1),
                    MaxMemoryUsage = 100 * 1024 * 1024
                },
                ["Resource"] = new CollectorConfiguration
                {
                    EnableBuffering = false,
                    FlushInterval = TimeSpan.FromSeconds(1),
                    MaxMemoryUsage = 10 * 1024 * 1024
                }
            },
            Global = new GlobalMetricsConfiguration
            {
                EnableMetrics = true,
                DefaultTimeZone = TimeZoneInfo.Utc,
                MaxConcurrentOperations = Environment.ProcessorCount * 2,
                HealthCheckInterval = TimeSpan.FromSeconds(30),
                LogLevel = LogLevel.Information
            }
        };
    }

    /// <summary>
    /// 验证配置有效性
    /// </summary>
    public ValidationResult Validate()
    {
        var result = new ValidationResult();

        // 验证聚合器配置
        foreach (var (name, config) in Aggregators)
        {
            if (config.DefaultWindowConfig.WindowSize <= TimeSpan.Zero)
                result.AddError($"聚合器 {name} 的窗口大小必须大于零");

            if (config.MaxMemoryUsage <= 0)
                result.AddError($"聚合器 {name} 的最大内存使用量必须大于零");
        }

        // 验证分析器配置
        foreach (var (name, config) in Analyzers)
        {
            if (config.TrendAnalysisWindowSize <= 0)
                result.AddError($"分析器 {name} 的趋势分析窗口大小必须大于零");

            if (config.InsightConfidenceThreshold is < 0 or > 1)
                result.AddError($"分析器 {name} 的洞察置信度阈值必须在0-1之间");
        }

        // 验证导出器配置
        foreach (var (name, config) in Exporters)
        {
            if (string.IsNullOrEmpty(config.OutputPath))
                result.AddError($"导出器 {name} 的输出路径不能为空");

            if (config.MaxFileSize <= 0)
                result.AddError($"导出器 {name} 的最大文件大小必须大于零");
        }

        // 验证收集器配置
        foreach (var (name, config) in Collectors)
        {
            if (config.EnableBuffering && config.BufferSize <= 0)
                result.AddError($"收集器 {name} 启用缓冲时缓冲区大小必须大于零");

            if (config.FlushInterval <= TimeSpan.Zero)
                result.AddError($"收集器 {name} 的刷新间隔必须大于零");
        }

        return result;
    }

    /// <summary>
    /// 合并配置
    /// </summary>
    public void MergeWith(MetricsConfiguration other)
    {
        // 合并聚合器配置
        foreach (var (key, value) in other.Aggregators)
        {
            Aggregators[key] = value;
        }

        // 合并分析器配置
        foreach (var (key, value) in other.Analyzers)
        {
            Analyzers[key] = value;
        }

        // 合并导出器配置
        foreach (var (key, value) in other.Exporters)
        {
            Exporters[key] = value;
        }

        // 合并收集器配置
        foreach (var (key, value) in other.Collectors)
        {
            Collectors[key] = value;
        }

        // 合并插件配置
        foreach (var (key, value) in other.Plugins)
        {
            Plugins[key] = value;
        }
    }
}

/// <summary>
/// 序列化配置
/// </summary>
public class SerializationConfiguration
{
    /// <summary>
    /// 默认序列化提供程序
    /// </summary>
    public string DefaultProvider { get; set; } = "SystemTextJson";

    /// <summary>
    /// 是否启用压缩
    /// </summary>
    public bool EnableCompression { get; set; } = false;

    /// <summary>
    /// 压缩级别
    /// </summary>
    public int CompressionLevel { get; set; } = 6;

    /// <summary>
    /// 是否美化JSON输出
    /// </summary>
    public bool PrettyPrint { get; set; } = false;

    /// <summary>
    /// 最大序列化深度
    /// </summary>
    public int MaxDepth { get; set; } = 64;
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
    /// 导出格式
    /// </summary>
    public string ExportFormat { get; set; } = "JSON";

    /// <summary>
    /// 最大文件大小
    /// </summary>
    public long MaxFileSize { get; set; } = 10 * 1024 * 1024;

    /// <summary>
    /// 是否启用压缩
    /// </summary>
    public bool EnableCompression { get; set; } = false;

    /// <summary>
    /// 是否启用文件轮转
    /// </summary>
    public bool EnableRotation { get; set; } = true;

    /// <summary>
    /// 最大文件数量
    /// </summary>
    public int MaxFiles { get; set; } = 10;

    /// <summary>
    /// 导出间隔
    /// </summary>
    public TimeSpan ExportInterval { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// 收集器配置
/// </summary>
public class CollectorConfiguration
{
    /// <summary>
    /// 是否启用缓冲
    /// </summary>
    public bool EnableBuffering { get; set; } = true;

    /// <summary>
    /// 缓冲区大小
    /// </summary>
    public int BufferSize { get; set; } = 1000;

    /// <summary>
    /// 刷新间隔
    /// </summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 最大内存使用量
    /// </summary>
    public long MaxMemoryUsage { get; set; } = 50 * 1024 * 1024;

    /// <summary>
    /// 是否启用指标过滤
    /// </summary>
    public bool EnableFiltering { get; set; } = false;

    /// <summary>
    /// 指标过滤规则
    /// </summary>
    public List<string> FilterRules { get; set; } = new();
}

/// <summary>
/// 全局指标配置
/// </summary>
public class GlobalMetricsConfiguration
{
    /// <summary>
    /// 是否启用指标收集
    /// </summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>
    /// 默认时区
    /// </summary>
    public TimeZoneInfo DefaultTimeZone { get; set; } = TimeZoneInfo.Utc;

    /// <summary>
    /// 最大并发操作数
    /// </summary>
    public int MaxConcurrentOperations { get; set; } = Environment.ProcessorCount * 2;

    /// <summary>
    /// 健康检查间隔
    /// </summary>
    public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 日志级别
    /// </summary>
    public LogLevel LogLevel { get; set; } = LogLevel.Information;

    /// <summary>
    /// 是否启用性能计数器
    /// </summary>
    public bool EnablePerformanceCounters { get; set; } = true;

    /// <summary>
    /// 默认标签
    /// </summary>
    public Dictionary<string, string> DefaultTags { get; set; } = new();
}

/// <summary>
/// 插件配置
/// </summary>
public class PluginConfiguration
{
    /// <summary>
    /// 插件类型
    /// </summary>
    public string PluginType { get; set; } = string.Empty;

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 插件参数
    /// </summary>
    public Dictionary<string, object> Parameters { get; set; } = new();

    /// <summary>
    /// 依赖项
    /// </summary>
    public List<string> Dependencies { get; set; } = new();
}

/// <summary>
/// 日志级别
/// </summary>
public enum LogLevel
{
    Trace,
    Debug,
    Information,
    Warning,
    Error,
    Critical,
    None
}

/// <summary>
/// 验证结果
/// </summary>
public class ValidationResult
{
    /// <summary>
    /// 错误列表
    /// </summary>
    public List<string> Errors { get; } = new();

    /// <summary>
    /// 警告列表
    /// </summary>
    public List<string> Warnings { get; } = new();

    /// <summary>
    /// 是否有效
    /// </summary>
    public bool IsValid => Errors.Count == 0;

    /// <summary>
    /// 添加错误
    /// </summary>
    public void AddError(string error)
    {
        Errors.Add(error);
    }

    /// <summary>
    /// 添加警告
    /// </summary>
    public void AddWarning(string warning)
    {
        Warnings.Add(warning);
    }
}

/// <summary>
/// JSON选项
/// </summary>
internal static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static readonly JsonSerializerOptions Pretty = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
}
