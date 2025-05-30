using PulseRPC.Benchmark.Metrics.Abstractions;

namespace PulseRPC.Benchmark.Metrics.Core;

/// <summary>
/// 指标配置构建器 - 提供流式API构建配置
/// </summary>
public class MetricsConfigurationBuilder
{
    private readonly MetricsConfiguration _configuration;

    /// <summary>
    /// 构造函数
    /// </summary>
    public MetricsConfigurationBuilder()
    {
        _configuration = new MetricsConfiguration();
    }

    /// <summary>
    /// 从现有配置开始构建
    /// </summary>
    public MetricsConfigurationBuilder(MetricsConfiguration baseConfiguration)
    {
        _configuration = baseConfiguration ?? new MetricsConfiguration();
    }

    /// <summary>
    /// 配置全局设置
    /// </summary>
    public MetricsConfigurationBuilder WithGlobalSettings(Action<GlobalConfigurationBuilder> configure)
    {
        var builder = new GlobalConfigurationBuilder(_configuration.Global);
        configure(builder);
        return this;
    }

    /// <summary>
    /// 配置序列化设置
    /// </summary>
    public MetricsConfigurationBuilder WithSerialization(Action<SerializationConfigurationBuilder> configure)
    {
        var builder = new SerializationConfigurationBuilder(_configuration.Serialization);
        configure(builder);
        return this;
    }

    /// <summary>
    /// 添加聚合器配置
    /// </summary>
    public MetricsConfigurationBuilder AddAggregator(string name, Action<AggregatorConfigurationBuilder> configure)
    {
        var config = new AggregatorConfiguration();
        var builder = new AggregatorConfigurationBuilder(config);
        configure(builder);
        _configuration.Aggregators[name] = config;
        return this;
    }

    /// <summary>
    /// 添加分析器配置
    /// </summary>
    public MetricsConfigurationBuilder AddAnalyzer(string name, Action<AnalyzerConfigurationBuilder> configure)
    {
        var config = new AnalyzerConfiguration();
        var builder = new AnalyzerConfigurationBuilder(config);
        configure(builder);
        _configuration.Analyzers[name] = config;
        return this;
    }

    /// <summary>
    /// 添加导出器配置
    /// </summary>
    public MetricsConfigurationBuilder AddExporter(string name, Action<ExporterConfigurationBuilder> configure)
    {
        var config = new ExporterConfiguration();
        var builder = new ExporterConfigurationBuilder(config);
        configure(builder);
        _configuration.Exporters[name] = config;
        return this;
    }

    /// <summary>
    /// 添加收集器配置
    /// </summary>
    public MetricsConfigurationBuilder AddCollector(string name, Action<CollectorConfigurationBuilder> configure)
    {
        var config = new CollectorConfiguration();
        var builder = new CollectorConfigurationBuilder(config);
        configure(builder);
        _configuration.Collectors[name] = config;
        return this;
    }

    /// <summary>
    /// 添加插件配置
    /// </summary>
    public MetricsConfigurationBuilder AddPlugin(string name, Action<PluginConfigurationBuilder> configure)
    {
        var config = new PluginConfiguration();
        var builder = new PluginConfigurationBuilder(config);
        configure(builder);
        _configuration.Plugins[name] = config;
        return this;
    }

    /// <summary>
    /// 合并另一个配置
    /// </summary>
    public MetricsConfigurationBuilder Merge(MetricsConfiguration other)
    {
        _configuration.MergeWith(other);
        return this;
    }

    /// <summary>
    /// 构建配置
    /// </summary>
    public MetricsConfiguration Build()
    {
        var validation = _configuration.Validate();
        if (!validation.IsValid)
        {
            throw new InvalidOperationException($"配置验证失败: {string.Join(", ", validation.Errors)}");
        }
        return _configuration;
    }

    /// <summary>
    /// 构建并保存到文件
    /// </summary>
    public async Task<MetricsConfiguration> BuildAndSaveAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var config = Build();
        await MetricsConfiguration.SaveToFileAsync(config, filePath, cancellationToken);
        return config;
    }
}

/// <summary>
/// 全局配置构建器
/// </summary>
public class GlobalConfigurationBuilder
{
    private readonly GlobalMetricsConfiguration _config;

    public GlobalConfigurationBuilder(GlobalMetricsConfiguration config)
    {
        _config = config;
    }

    public GlobalConfigurationBuilder EnableMetrics(bool enable = true)
    {
        _config.EnableMetrics = enable;
        return this;
    }

    public GlobalConfigurationBuilder WithTimeZone(TimeZoneInfo timeZone)
    {
        _config.DefaultTimeZone = timeZone;
        return this;
    }

    public GlobalConfigurationBuilder WithMaxConcurrentOperations(int maxOperations)
    {
        _config.MaxConcurrentOperations = maxOperations;
        return this;
    }

    public GlobalConfigurationBuilder WithHealthCheckInterval(TimeSpan interval)
    {
        _config.HealthCheckInterval = interval;
        return this;
    }

    public GlobalConfigurationBuilder WithLogLevel(LogLevel level)
    {
        _config.LogLevel = level;
        return this;
    }

    public GlobalConfigurationBuilder EnablePerformanceCounters(bool enable = true)
    {
        _config.EnablePerformanceCounters = enable;
        return this;
    }

    public GlobalConfigurationBuilder AddDefaultTag(string key, string value)
    {
        _config.DefaultTags[key] = value;
        return this;
    }
}

/// <summary>
/// 序列化配置构建器
/// </summary>
public class SerializationConfigurationBuilder
{
    private readonly SerializationConfiguration _config;

    public SerializationConfigurationBuilder(SerializationConfiguration config)
    {
        _config = config;
    }

    public SerializationConfigurationBuilder WithProvider(string provider)
    {
        _config.DefaultProvider = provider;
        return this;
    }

    public SerializationConfigurationBuilder EnableCompression(bool enable = true, int level = 6)
    {
        _config.EnableCompression = enable;
        _config.CompressionLevel = level;
        return this;
    }

    public SerializationConfigurationBuilder PrettyPrint(bool enable = true)
    {
        _config.PrettyPrint = enable;
        return this;
    }

    public SerializationConfigurationBuilder WithMaxDepth(int depth)
    {
        _config.MaxDepth = depth;
        return this;
    }
}

/// <summary>
/// 聚合器配置构建器
/// </summary>
public class AggregatorConfigurationBuilder
{
    private readonly AggregatorConfiguration _config;

    public AggregatorConfigurationBuilder(AggregatorConfiguration config)
    {
        _config = config;
    }

    public AggregatorConfigurationBuilder EnableStatistics(bool enable = true)
    {
        _config.EnableStatistics = enable;
        return this;
    }

    public AggregatorConfigurationBuilder EnableParallelProcessing(bool enable = true, int degree = -1)
    {
        _config.EnableParallelProcessing = enable;
        if (degree > 0)
            _config.ParallelismDegree = degree;
        return this;
    }

    public AggregatorConfigurationBuilder WithPercentiles(params int[] percentiles)
    {
        _config.Percentiles = percentiles.ToList();
        return this;
    }

    public AggregatorConfigurationBuilder WithMaxMemoryUsage(long bytes)
    {
        _config.MaxMemoryUsage = bytes;
        return this;
    }

    public AggregatorConfigurationBuilder WithBufferSize(int size)
    {
        _config.BufferSize = size;
        return this;
    }

    public AggregatorConfigurationBuilder WithTimeWindow(Action<TimeWindowConfigurationBuilder> configure)
    {
        var builder = new TimeWindowConfigurationBuilder(_config.DefaultWindowConfig);
        configure(builder);
        return this;
    }
}

/// <summary>
/// 时间窗口配置构建器
/// </summary>
public class TimeWindowConfigurationBuilder
{
    private readonly TimeWindowConfiguration _config;

    public TimeWindowConfigurationBuilder(TimeWindowConfiguration config)
    {
        _config = config;
    }

    public TimeWindowConfigurationBuilder WithWindowSize(TimeSpan size)
    {
        _config.WindowSize = size;
        return this;
    }

    public TimeWindowConfigurationBuilder WithSlideInterval(TimeSpan interval)
    {
        _config.SlideInterval = interval;
        return this;
    }

    public TimeWindowConfigurationBuilder WithWindowType(WindowType type)
    {
        _config.WindowType = type;
        return this;
    }

    public TimeWindowConfigurationBuilder WithMaxWindows(int maxWindows)
    {
        _config.MaxWindows = maxWindows;
        return this;
    }

    public TimeWindowConfigurationBuilder EnableAutoCleanup(bool enable = true, TimeSpan? expirationTime = null)
    {
        _config.AutoCleanup = enable;
        if (expirationTime.HasValue)
            _config.ExpirationTime = expirationTime.Value;
        return this;
    }
}

/// <summary>
/// 分析器配置构建器
/// </summary>
public class AnalyzerConfigurationBuilder
{
    private readonly AnalyzerConfiguration _config;

    public AnalyzerConfigurationBuilder(AnalyzerConfiguration config)
    {
        _config = config;
    }

    public AnalyzerConfigurationBuilder EnableTrendAnalysis(bool enable = true, int windowSize = 50)
    {
        _config.EnableTrendAnalysis = enable;
        _config.TrendAnalysisWindowSize = windowSize;
        return this;
    }

    public AnalyzerConfigurationBuilder EnableInsightGeneration(bool enable = true, double threshold = 0.8)
    {
        _config.EnableInsightGeneration = enable;
        _config.InsightConfidenceThreshold = threshold;
        return this;
    }

    public AnalyzerConfigurationBuilder WithMaxCacheSize(int size)
    {
        _config.MaxCacheSize = size;
        return this;
    }

    public AnalyzerConfigurationBuilder WithParallelismDegree(int degree)
    {
        _config.ParallelismDegree = degree;
        return this;
    }

    public AnalyzerConfigurationBuilder WithAnomalyDetection(Action<AnomalyDetectionConfigurationBuilder> configure)
    {
        var builder = new AnomalyDetectionConfigurationBuilder(_config.DefaultAnomalyDetection);
        configure(builder);
        return this;
    }
}

/// <summary>
/// 异常检测配置构建器
/// </summary>
public class AnomalyDetectionConfigurationBuilder
{
    private readonly AnomalyDetectionConfiguration _config;

    public AnomalyDetectionConfigurationBuilder(AnomalyDetectionConfiguration config)
    {
        _config = config;
    }

    public AnomalyDetectionConfigurationBuilder WithMethod(AnomalyDetectionMethod method)
    {
        _config.Method = method;
        return this;
    }

    public AnomalyDetectionConfigurationBuilder WithSensitivity(double sensitivity)
    {
        _config.Sensitivity = sensitivity;
        return this;
    }

    public AnomalyDetectionConfigurationBuilder WithWindowSize(int size)
    {
        _config.WindowSize = size;
        return this;
    }

    public AnomalyDetectionConfigurationBuilder WithStandardDeviationMultiple(double multiple)
    {
        _config.StandardDeviationMultiple = multiple;
        return this;
    }

    public AnomalyDetectionConfigurationBuilder EnableSeasonalityDetection(bool enable = true, TimeSpan? period = null)
    {
        _config.EnableSeasonalityDetection = enable;
        if (period.HasValue)
            _config.SeasonalPeriod = period.Value;
        return this;
    }
}

/// <summary>
/// 导出器配置构建器
/// </summary>
public class ExporterConfigurationBuilder
{
    private readonly ExporterConfiguration _config;

    public ExporterConfigurationBuilder(ExporterConfiguration config)
    {
        _config = config;
    }

    public ExporterConfigurationBuilder WithOutputPath(string path)
    {
        _config.OutputPath = path;
        return this;
    }

    public ExporterConfigurationBuilder WithFormat(string format)
    {
        _config.ExportFormat = format;
        return this;
    }

    public ExporterConfigurationBuilder WithMaxFileSize(long bytes)
    {
        _config.MaxFileSize = bytes;
        return this;
    }

    public ExporterConfigurationBuilder EnableCompression(bool enable = true)
    {
        _config.EnableCompression = enable;
        return this;
    }

    public ExporterConfigurationBuilder EnableRotation(bool enable = true, int maxFiles = 10)
    {
        _config.EnableRotation = enable;
        _config.MaxFiles = maxFiles;
        return this;
    }

    public ExporterConfigurationBuilder WithExportInterval(TimeSpan interval)
    {
        _config.ExportInterval = interval;
        return this;
    }
}

/// <summary>
/// 收集器配置构建器
/// </summary>
public class CollectorConfigurationBuilder
{
    private readonly CollectorConfiguration _config;

    public CollectorConfigurationBuilder(CollectorConfiguration config)
    {
        _config = config;
    }

    public CollectorConfigurationBuilder EnableBuffering(bool enable = true, int bufferSize = 1000)
    {
        _config.EnableBuffering = enable;
        _config.BufferSize = bufferSize;
        return this;
    }

    public CollectorConfigurationBuilder WithFlushInterval(TimeSpan interval)
    {
        _config.FlushInterval = interval;
        return this;
    }

    public CollectorConfigurationBuilder WithMaxMemoryUsage(long bytes)
    {
        _config.MaxMemoryUsage = bytes;
        return this;
    }

    public CollectorConfigurationBuilder EnableFiltering(bool enable = true, params string[] rules)
    {
        _config.EnableFiltering = enable;
        if (rules.Length > 0)
            _config.FilterRules = rules.ToList();
        return this;
    }
}

/// <summary>
/// 插件配置构建器
/// </summary>
public class PluginConfigurationBuilder
{
    private readonly PluginConfiguration _config;

    public PluginConfigurationBuilder(PluginConfiguration config)
    {
        _config = config;
    }

    public PluginConfigurationBuilder WithType(string type)
    {
        _config.PluginType = type;
        return this;
    }

    public PluginConfigurationBuilder Enable(bool enable = true)
    {
        _config.Enabled = enable;
        return this;
    }

    public PluginConfigurationBuilder AddParameter(string key, object value)
    {
        _config.Parameters[key] = value;
        return this;
    }

    public PluginConfigurationBuilder AddDependency(string dependency)
    {
        _config.Dependencies.Add(dependency);
        return this;
    }
}

/// <summary>
/// 预配置的配置构建器
/// </summary>
public static class MetricsConfigurationBuilderExtensions
{
    /// <summary>
    /// 添加默认聚合器
    /// </summary>
    public static MetricsConfigurationBuilder AddDefaultAggregators(this MetricsConfigurationBuilder builder)
    {
        return builder
            .AddAggregator("TimeWindow", cfg => cfg
                .EnableStatistics()
                .EnableParallelProcessing()
                .WithPercentiles(50, 90, 95, 99)
                .WithTimeWindow(tw => tw
                    .WithWindowSize(TimeSpan.FromMinutes(5))
                    .WithSlideInterval(TimeSpan.FromMinutes(1))
                    .WithWindowType(WindowType.Sliding)
                    .EnableAutoCleanup()))
            .AddAggregator("Statistical", cfg => cfg
                .EnableStatistics()
                .EnableParallelProcessing()
                .WithPercentiles(50, 90, 95, 99)
                .WithMaxMemoryUsage(100 * 1024 * 1024)
                .WithBufferSize(10000));
    }

    /// <summary>
    /// 添加默认分析器
    /// </summary>
    public static MetricsConfigurationBuilder AddDefaultAnalyzers(this MetricsConfigurationBuilder builder)
    {
        return builder
            .AddAnalyzer("Trend", cfg => cfg
                .EnableTrendAnalysis(true, 50)
                .EnableInsightGeneration(true, 0.8)
                .WithMaxCacheSize(10000)
                .WithAnomalyDetection(ad => ad
                    .WithMethod(AnomalyDetectionMethod.StatisticalOutlier)
                    .WithSensitivity(0.95)
                    .WithStandardDeviationMultiple(3.0)));
    }

    /// <summary>
    /// 添加默认导出器
    /// </summary>
    public static MetricsConfigurationBuilder AddDefaultExporters(this MetricsConfigurationBuilder builder)
    {
        return builder
            .AddExporter("Json", cfg => cfg
                .WithOutputPath("metrics_export.json")
                .WithFormat("JSON")
                .WithMaxFileSize(10 * 1024 * 1024)
                .EnableCompression()
                .EnableRotation())
            .AddExporter("Csv", cfg => cfg
                .WithOutputPath("metrics_export.csv")
                .WithFormat("CSV")
                .WithMaxFileSize(10 * 1024 * 1024)
                .EnableRotation());
    }

    /// <summary>
    /// 添加默认收集器
    /// </summary>
    public static MetricsConfigurationBuilder AddDefaultCollectors(this MetricsConfigurationBuilder builder)
    {
        return builder
            .AddCollector("RealTime", cfg => cfg
                .EnableBuffering(true, 1000)
                .WithFlushInterval(TimeSpan.FromSeconds(5))
                .WithMaxMemoryUsage(50 * 1024 * 1024))
            .AddCollector("Batch", cfg => cfg
                .EnableBuffering(true, 10000)
                .WithFlushInterval(TimeSpan.FromMinutes(1))
                .WithMaxMemoryUsage(100 * 1024 * 1024))
            .AddCollector("Resource", cfg => cfg
                .EnableBuffering(false)
                .WithFlushInterval(TimeSpan.FromSeconds(1))
                .WithMaxMemoryUsage(10 * 1024 * 1024));
    }

    /// <summary>
    /// 创建默认开发配置
    /// </summary>
    public static MetricsConfigurationBuilder CreateDevelopmentConfiguration()
    {
        return new MetricsConfigurationBuilder()
            .WithGlobalSettings(g => g
                .EnableMetrics()
                .WithLogLevel(LogLevel.Debug)
                .WithHealthCheckInterval(TimeSpan.FromSeconds(10))
                .EnablePerformanceCounters())
            .WithSerialization(s => s
                .WithProvider("SystemTextJson")
                .PrettyPrint()
                .EnableCompression(false))
            .AddDefaultAggregators()
            .AddDefaultAnalyzers()
            .AddDefaultExporters()
            .AddDefaultCollectors();
    }

    /// <summary>
    /// 创建默认生产配置
    /// </summary>
    public static MetricsConfigurationBuilder CreateProductionConfiguration()
    {
        return new MetricsConfigurationBuilder()
            .WithGlobalSettings(g => g
                .EnableMetrics()
                .WithLogLevel(LogLevel.Information)
                .WithHealthCheckInterval(TimeSpan.FromSeconds(30))
                .EnablePerformanceCounters())
            .WithSerialization(s => s
                .WithProvider("SystemTextJson")
                .PrettyPrint(false)
                .EnableCompression())
            .AddDefaultAggregators()
            .AddDefaultAnalyzers()
            .AddDefaultExporters()
            .AddDefaultCollectors();
    }
}
