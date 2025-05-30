using PulseRPC.Benchmark.Metrics.Models;

namespace PulseRPC.Benchmark.Metrics.Abstractions;

/// <summary>
/// 指标分析器接口
/// </summary>
public interface IMetricsAnalyzer : IMetricsPlugin
{
    /// <summary>
    /// 分析指标数据
    /// </summary>
    /// <param name="metrics">指标数据</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>分析结果</returns>
    Task<AnalysisResult> AnalyzeMetricsAsync(IEnumerable<JsonOptimizedMetricsEvent> metrics, CancellationToken cancellationToken = default);

    /// <summary>
    /// 分析聚合结果
    /// </summary>
    /// <param name="aggregationResults">聚合结果</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>分析结果</returns>
    Task<AnalysisResult> AnalyzeAggregationResultsAsync(IEnumerable<AggregationResult> aggregationResults, CancellationToken cancellationToken = default);

    /// <summary>
    /// 检测异常
    /// </summary>
    /// <param name="metrics">指标数据</param>
    /// <param name="detectionConfig">检测配置</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>异常检测结果</returns>
    Task<AnomalyDetectionResult> DetectAnomaliesAsync(IEnumerable<JsonOptimizedMetricsEvent> metrics, AnomalyDetectionConfiguration? detectionConfig = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 生成洞察
    /// </summary>
    /// <param name="analysisResults">分析结果</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>洞察结果</returns>
    Task<InsightResult> GenerateInsightsAsync(IEnumerable<AnalysisResult> analysisResults, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取趋势数据
    /// </summary>
    /// <param name="metricName">指标名称</param>
    /// <param name="timeRange">时间范围</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>趋势数据</returns>
    Task<TrendData> GetTrendDataAsync(string metricName, TimeRange timeRange, CancellationToken cancellationToken = default);

    /// <summary>
    /// 比较指标
    /// </summary>
    /// <param name="baselineMetrics">基线指标</param>
    /// <param name="currentMetrics">当前指标</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>比较结果</returns>
    Task<ComparisonResult> CompareMetricsAsync(IEnumerable<JsonOptimizedMetricsEvent> baselineMetrics, IEnumerable<JsonOptimizedMetricsEvent> currentMetrics, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取分析统计信息
    /// </summary>
    /// <returns>统计信息</returns>
    Task<AnalyzerStatistics> GetAnalyzerStatisticsAsync();

    /// <summary>
    /// 清空分析数据
    /// </summary>
    /// <returns>清空任务</returns>
    Task ClearAnalysisDataAsync();

    /// <summary>
    /// 分析器配置
    /// </summary>
    AnalyzerConfiguration Configuration { get; }

    /// <summary>
    /// 分析完成事件
    /// </summary>
    event Action<AnalysisCompletedEventArgs>? AnalysisCompleted;

    /// <summary>
    /// 异常检测事件
    /// </summary>
    event Action<AnomalyDetectedEventArgs>? AnomalyDetected;

    /// <summary>
    /// 洞察生成事件
    /// </summary>
    event Action<InsightGeneratedEventArgs>? InsightGenerated;
}

/// <summary>
/// 分析结果
/// </summary>
public class AnalysisResult
{
    /// <summary>
    /// 分析器名称
    /// </summary>
    public string AnalyzerName { get; set; } = string.Empty;

    /// <summary>
    /// 分析时间戳
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 分析的时间范围
    /// </summary>
    public TimeRange TimeRange { get; set; } = new();

    /// <summary>
    /// 分析的指标类型
    /// </summary>
    public string MetricType { get; set; } = string.Empty;

    /// <summary>
    /// 分析持续时间
    /// </summary>
    public TimeSpan AnalysisDuration { get; set; }

    /// <summary>
    /// 分析的数据点数量
    /// </summary>
    public int ProcessedDataPoints { get; set; }

    /// <summary>
    /// 趋势数据
    /// </summary>
    public List<TrendData> TrendData { get; set; } = new();

    /// <summary>
    /// 异常检测结果
    /// </summary>
    public List<AnomalyDetectionResult> Anomalies { get; set; } = new();

    /// <summary>
    /// 洞察
    /// </summary>
    public List<InsightResult> Insights { get; set; } = new();

    /// <summary>
    /// 统计摘要
    /// </summary>
    public Dictionary<string, AnalyticsSummary> AnalyticsSummaries { get; set; } = new();

    /// <summary>
    /// 元数据
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();

    /// <summary>
    /// 添加趋势数据
    /// </summary>
    public void AddTrendData(TrendData trend)
    {
        TrendData.Add(trend);
    }

    /// <summary>
    /// 添加异常检测结果
    /// </summary>
    public void AddAnomaly(AnomalyDetectionResult anomaly)
    {
        Anomalies.Add(anomaly);
    }

    /// <summary>
    /// 添加洞察
    /// </summary>
    public void AddInsight(InsightResult insight)
    {
        Insights.Add(insight);
    }
}

/// <summary>
/// 趋势数据
/// </summary>
public class TrendData
{
    /// <summary>
    /// 指标名称
    /// </summary>
    public string MetricName { get; set; } = string.Empty;

    /// <summary>
    /// 时间范围
    /// </summary>
    public TimeRange TimeRange { get; set; } = new();

    /// <summary>
    /// 趋势类型
    /// </summary>
    public TrendType TrendType { get; set; }

    /// <summary>
    /// 趋势强度（-1到1，负值表示下降趋势）
    /// </summary>
    public double TrendStrength { get; set; }

    /// <summary>
    /// 斜率
    /// </summary>
    public double Slope { get; set; }

    /// <summary>
    /// 相关系数
    /// </summary>
    public double CorrelationCoefficient { get; set; }

    /// <summary>
    /// 置信度
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// 数据点
    /// </summary>
    public List<TrendDataPoint> DataPoints { get; set; } = new();

    /// <summary>
    /// 变化点
    /// </summary>
    public List<ChangePoint> ChangePoints { get; set; } = new();
}

/// <summary>
/// 趋势类型
/// </summary>
public enum TrendType
{
    /// <summary>
    /// 无明显趋势
    /// </summary>
    None,

    /// <summary>
    /// 上升趋势
    /// </summary>
    Increasing,

    /// <summary>
    /// 下降趋势
    /// </summary>
    Decreasing,

    /// <summary>
    /// 振荡趋势
    /// </summary>
    Oscillating,

    /// <summary>
    /// 季节性趋势
    /// </summary>
    Seasonal
}

/// <summary>
/// 趋势数据点
/// </summary>
public class TrendDataPoint
{
    /// <summary>
    /// 时间戳
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// 数值
    /// </summary>
    public double Value { get; set; }

    /// <summary>
    /// 预测值
    /// </summary>
    public double PredictedValue { get; set; }

    /// <summary>
    /// 置信区间下限
    /// </summary>
    public double LowerConfidenceInterval { get; set; }

    /// <summary>
    /// 置信区间上限
    /// </summary>
    public double UpperConfidenceInterval { get; set; }
}

/// <summary>
/// 变化点
/// </summary>
public class ChangePoint
{
    /// <summary>
    /// 时间戳
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// 变化类型
    /// </summary>
    public ChangePointType Type { get; set; }

    /// <summary>
    /// 变化程度
    /// </summary>
    public double Magnitude { get; set; }

    /// <summary>
    /// 置信度
    /// </summary>
    public double Confidence { get; set; }
}

/// <summary>
/// 变化点类型
/// </summary>
public enum ChangePointType
{
    /// <summary>
    /// 均值变化
    /// </summary>
    MeanShift,

    /// <summary>
    /// 方差变化
    /// </summary>
    VarianceChange,

    /// <summary>
    /// 趋势变化
    /// </summary>
    TrendChange
}

/// <summary>
/// 异常检测结果
/// </summary>
public class AnomalyDetectionResult
{
    /// <summary>
    /// 检测时间戳
    /// </summary>
    public DateTime DetectionTimestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 指标名称
    /// </summary>
    public string MetricName { get; set; } = string.Empty;

    /// <summary>
    /// 异常点
    /// </summary>
    public List<AnomalyPoint> AnomalyPoints { get; set; } = new();

    /// <summary>
    /// 检测方法
    /// </summary>
    public string DetectionMethod { get; set; } = string.Empty;

    /// <summary>
    /// 检测参数
    /// </summary>
    public Dictionary<string, object> DetectionParameters { get; set; } = new();

    /// <summary>
    /// 总异常数量
    /// </summary>
    public int AnomalyCount => AnomalyPoints.Count;

    /// <summary>
    /// 异常比例
    /// </summary>
    public double AnomalyRatio { get; set; }
}

/// <summary>
/// 异常点
/// </summary>
public class AnomalyPoint
{
    /// <summary>
    /// 时间戳
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// 异常值
    /// </summary>
    public double Value { get; set; }

    /// <summary>
    /// 期望值
    /// </summary>
    public double ExpectedValue { get; set; }

    /// <summary>
    /// 异常分数
    /// </summary>
    public double AnomalyScore { get; set; }

    /// <summary>
    /// 异常类型
    /// </summary>
    public AnomalyType Type { get; set; }

    /// <summary>
    /// 严重程度
    /// </summary>
    public AnomalySeverity Severity { get; set; }
}

/// <summary>
/// 异常类型
/// </summary>
public enum AnomalyType
{
    /// <summary>
    /// 点异常
    /// </summary>
    Point,

    /// <summary>
    /// 上下文异常
    /// </summary>
    Contextual,

    /// <summary>
    /// 集体异常
    /// </summary>
    Collective
}

/// <summary>
/// 异常严重程度
/// </summary>
public enum AnomalySeverity
{
    /// <summary>
    /// 低
    /// </summary>
    Low,

    /// <summary>
    /// 中等
    /// </summary>
    Medium,

    /// <summary>
    /// 高
    /// </summary>
    High,

    /// <summary>
    /// 严重
    /// </summary>
    Critical
}

/// <summary>
/// 洞察结果
/// </summary>
public class InsightResult
{
    /// <summary>
    /// 洞察ID
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// 洞察类型
    /// </summary>
    public InsightType Type { get; set; }

    /// <summary>
    /// 标题
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 描述
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 重要性
    /// </summary>
    public InsightImportance Importance { get; set; }

    /// <summary>
    /// 置信度
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// 相关指标
    /// </summary>
    public List<string> RelatedMetrics { get; set; } = new();

    /// <summary>
    /// 时间范围
    /// </summary>
    public TimeRange TimeRange { get; set; } = new();

    /// <summary>
    /// 建议操作
    /// </summary>
    public List<string> RecommendedActions { get; set; } = new();

    /// <summary>
    /// 支持数据
    /// </summary>
    public Dictionary<string, object> SupportingData { get; set; } = new();
}

/// <summary>
/// 洞察类型
/// </summary>
public enum InsightType
{
    /// <summary>
    /// 性能改善
    /// </summary>
    PerformanceImprovement,

    /// <summary>
    /// 性能下降
    /// </summary>
    PerformanceDegradation,

    /// <summary>
    /// 异常模式
    /// </summary>
    AnomalousPattern,

    /// <summary>
    /// 容量预警
    /// </summary>
    CapacityWarning,

    /// <summary>
    /// 优化机会
    /// </summary>
    OptimizationOpportunity,

    /// <summary>
    /// 相关性发现
    /// </summary>
    CorrelationDiscovery
}

/// <summary>
/// 洞察重要性
/// </summary>
public enum InsightImportance
{
    /// <summary>
    /// 信息
    /// </summary>
    Info,

    /// <summary>
    /// 低
    /// </summary>
    Low,

    /// <summary>
    /// 中等
    /// </summary>
    Medium,

    /// <summary>
    /// 高
    /// </summary>
    High,

    /// <summary>
    /// 严重
    /// </summary>
    Critical
}

/// <summary>
/// 比较结果
/// </summary>
public class ComparisonResult
{
    /// <summary>
    /// 比较时间戳
    /// </summary>
    public DateTime ComparisonTimestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 指标名称
    /// </summary>
    public string MetricName { get; set; } = string.Empty;

    /// <summary>
    /// 基线统计
    /// </summary>
    public StatisticalSummary BaselineStatistics { get; set; } = new();

    /// <summary>
    /// 当前统计
    /// </summary>
    public StatisticalSummary CurrentStatistics { get; set; } = new();

    /// <summary>
    /// 变化百分比
    /// </summary>
    public double ChangePercentage { get; set; }

    /// <summary>
    /// 显著性测试结果
    /// </summary>
    public SignificanceTestResult SignificanceTest { get; set; } = new();

    /// <summary>
    /// 性能回归检测
    /// </summary>
    public bool IsRegression { get; set; }

    /// <summary>
    /// 性能改进检测
    /// </summary>
    public bool IsImprovement { get; set; }
}

/// <summary>
/// 统计摘要
/// </summary>
public class StatisticalSummary
{
    /// <summary>
    /// 样本数量
    /// </summary>
    public int Count { get; set; }

    /// <summary>
    /// 平均值
    /// </summary>
    public double Mean { get; set; }

    /// <summary>
    /// 中位数
    /// </summary>
    public double Median { get; set; }

    /// <summary>
    /// 标准差
    /// </summary>
    public double StandardDeviation { get; set; }

    /// <summary>
    /// 最小值
    /// </summary>
    public double Min { get; set; }

    /// <summary>
    /// 最大值
    /// </summary>
    public double Max { get; set; }

    /// <summary>
    /// 百分位数
    /// </summary>
    public Dictionary<int, double> Percentiles { get; set; } = new();
}

/// <summary>
/// 显著性测试结果
/// </summary>
public class SignificanceTestResult
{
    /// <summary>
    /// 测试方法
    /// </summary>
    public string TestMethod { get; set; } = string.Empty;

    /// <summary>
    /// p值
    /// </summary>
    public double PValue { get; set; }

    /// <summary>
    /// 是否显著
    /// </summary>
    public bool IsSignificant { get; set; }

    /// <summary>
    /// 显著性水平
    /// </summary>
    public double SignificanceLevel { get; set; } = 0.05;

    /// <summary>
    /// 效应大小
    /// </summary>
    public double EffectSize { get; set; }
}

/// <summary>
/// 分析摘要
/// </summary>
public class AnalyticsSummary
{
    /// <summary>
    /// 指标名称
    /// </summary>
    public string MetricName { get; set; } = string.Empty;

    /// <summary>
    /// 分析类型
    /// </summary>
    public string AnalysisType { get; set; } = string.Empty;

    /// <summary>
    /// 关键发现
    /// </summary>
    public List<string> KeyFindings { get; set; } = new();

    /// <summary>
    /// 数值摘要
    /// </summary>
    public Dictionary<string, double> NumericSummary { get; set; } = new();

    /// <summary>
    /// 置信度
    /// </summary>
    public double Confidence { get; set; }
}

/// <summary>
/// 异常检测配置
/// </summary>
public class AnomalyDetectionConfiguration
{
    /// <summary>
    /// 检测方法
    /// </summary>
    public AnomalyDetectionMethod Method { get; set; } = AnomalyDetectionMethod.StatisticalOutlier;

    /// <summary>
    /// 敏感度
    /// </summary>
    public double Sensitivity { get; set; } = 0.95;

    /// <summary>
    /// 窗口大小
    /// </summary>
    public int WindowSize { get; set; } = 100;

    /// <summary>
    /// 标准差倍数
    /// </summary>
    public double StandardDeviationMultiple { get; set; } = 3.0;

    /// <summary>
    /// 是否启用季节性检测
    /// </summary>
    public bool EnableSeasonalityDetection { get; set; } = false;

    /// <summary>
    /// 季节性周期
    /// </summary>
    public TimeSpan SeasonalPeriod { get; set; } = TimeSpan.FromDays(1);
}

/// <summary>
/// 异常检测方法
/// </summary>
public enum AnomalyDetectionMethod
{
    /// <summary>
    /// 统计离群值
    /// </summary>
    StatisticalOutlier,

    /// <summary>
    /// Z分数
    /// </summary>
    ZScore,

    /// <summary>
    /// 修正Z分数
    /// </summary>
    ModifiedZScore,

    /// <summary>
    /// 四分位距
    /// </summary>
    InterquartileRange,

    /// <summary>
    /// 移动平均
    /// </summary>
    MovingAverage,

    /// <summary>
    /// 指数平滑
    /// </summary>
    ExponentialSmoothing
}

/// <summary>
/// 分析器配置
/// </summary>
public class AnalyzerConfiguration
{
    /// <summary>
    /// 默认异常检测配置
    /// </summary>
    public AnomalyDetectionConfiguration DefaultAnomalyDetection { get; set; } = new();

    /// <summary>
    /// 是否启用趋势分析
    /// </summary>
    public bool EnableTrendAnalysis { get; set; } = true;

    /// <summary>
    /// 趋势分析窗口大小
    /// </summary>
    public int TrendAnalysisWindowSize { get; set; } = 50;

    /// <summary>
    /// 是否启用洞察生成
    /// </summary>
    public bool EnableInsightGeneration { get; set; } = true;

    /// <summary>
    /// 洞察置信度阈值
    /// </summary>
    public double InsightConfidenceThreshold { get; set; } = 0.8;

    /// <summary>
    /// 最大缓存大小
    /// </summary>
    public int MaxCacheSize { get; set; } = 10000;

    /// <summary>
    /// 并行处理度
    /// </summary>
    public int ParallelismDegree { get; set; } = Environment.ProcessorCount;
}

/// <summary>
/// 分析器统计信息
/// </summary>
public class AnalyzerStatistics
{
    /// <summary>
    /// 总分析次数
    /// </summary>
    public long TotalAnalyses { get; set; }

    /// <summary>
    /// 总处理数据点数
    /// </summary>
    public long TotalProcessedDataPoints { get; set; }

    /// <summary>
    /// 平均分析时间
    /// </summary>
    public TimeSpan AverageAnalysisTime { get; set; }

    /// <summary>
    /// 检测到的异常总数
    /// </summary>
    public long TotalAnomaliesDetected { get; set; }

    /// <summary>
    /// 生成的洞察总数
    /// </summary>
    public long TotalInsightsGenerated { get; set; }

    /// <summary>
    /// 最后分析时间
    /// </summary>
    public DateTime LastAnalysisTime { get; set; }

    /// <summary>
    /// 错误次数
    /// </summary>
    public long ErrorCount { get; set; }

    /// <summary>
    /// 缓存命中率
    /// </summary>
    public double CacheHitRatio { get; set; }
}

/// <summary>
/// 分析完成事件参数
/// </summary>
public class AnalysisCompletedEventArgs : EventArgs
{
    /// <summary>
    /// 分析结果
    /// </summary>
    public AnalysisResult Result { get; }

    /// <summary>
    /// 分析器名称
    /// </summary>
    public string AnalyzerName { get; }

    /// <summary>
    /// 完成时间
    /// </summary>
    public DateTime CompletionTime { get; }

    public AnalysisCompletedEventArgs(AnalysisResult result, string analyzerName)
    {
        Result = result;
        AnalyzerName = analyzerName;
        CompletionTime = DateTime.UtcNow;
    }
}

/// <summary>
/// 异常检测事件参数
/// </summary>
public class AnomalyDetectedEventArgs : EventArgs
{
    /// <summary>
    /// 异常检测结果
    /// </summary>
    public AnomalyDetectionResult Result { get; }

    /// <summary>
    /// 检测器名称
    /// </summary>
    public string DetectorName { get; }

    /// <summary>
    /// 检测时间
    /// </summary>
    public DateTime DetectionTime { get; }

    public AnomalyDetectedEventArgs(AnomalyDetectionResult result, string detectorName)
    {
        Result = result;
        DetectorName = detectorName;
        DetectionTime = DateTime.UtcNow;
    }
}

/// <summary>
/// 洞察生成事件参数
/// </summary>
public class InsightGeneratedEventArgs : EventArgs
{
    /// <summary>
    /// 洞察结果
    /// </summary>
    public InsightResult Insight { get; }

    /// <summary>
    /// 生成器名称
    /// </summary>
    public string GeneratorName { get; }

    /// <summary>
    /// 生成时间
    /// </summary>
    public DateTime GenerationTime { get; }

    public InsightGeneratedEventArgs(InsightResult insight, string generatorName)
    {
        Insight = insight;
        GeneratorName = generatorName;
        GenerationTime = DateTime.UtcNow;
    }
}
