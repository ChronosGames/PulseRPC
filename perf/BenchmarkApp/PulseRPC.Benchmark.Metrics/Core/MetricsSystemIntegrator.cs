using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using PulseRPC.Benchmark.Metrics.Abstractions;
using PulseRPC.Benchmark.Metrics.Models;

namespace PulseRPC.Benchmark.Metrics.Core;

/// <summary>
/// 指标系统集成器 - 协调所有指标组件的运行
/// </summary>
public class MetricsSystemIntegrator : IDisposable
{
    private readonly ILogger<MetricsSystemIntegrator>? _logger;
    private readonly MetricsConfiguration _configuration;
    private readonly ConcurrentDictionary<string, IMetricsPlugin> _plugins;
    private readonly ConcurrentDictionary<string, IMetricsCollector> _collectors;
    private readonly ConcurrentDictionary<string, IMetricsAggregator> _aggregators;
    private readonly ConcurrentDictionary<string, IMetricsAnalyzer> _analyzers;
    private readonly ConcurrentDictionary<string, IMetricsExporter> _exporters;
    private readonly Timer? _healthCheckTimer;
    private readonly Timer? _coordinationTimer;

    private bool _isRunning = false;
    private bool _isDisposed = false;
    private readonly object _stateLock = new();

    public MetricsSystemIntegrator(
        MetricsConfiguration configuration,
        ILogger<MetricsSystemIntegrator>? logger = null)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger;
        _plugins = new ConcurrentDictionary<string, IMetricsPlugin>();
        _collectors = new ConcurrentDictionary<string, IMetricsCollector>();
        _aggregators = new ConcurrentDictionary<string, IMetricsAggregator>();
        _analyzers = new ConcurrentDictionary<string, IMetricsAnalyzer>();
        _exporters = new ConcurrentDictionary<string, IMetricsExporter>();

        // 初始化定时器
        _healthCheckTimer = new Timer(PerformHealthCheck, null, Timeout.Infinite, Timeout.Infinite);
        _coordinationTimer = new Timer(CoordinateComponents, null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// 是否正在运行
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// 注册收集器
    /// </summary>
    public async Task<bool> RegisterCollectorAsync(string name, IMetricsCollector collector)
    {
        try
        {
            if (_collectors.TryAdd(name, collector))
            {
                _plugins.TryAdd($"collector_{name}", collector);

                // 配置收集器
                if (_configuration.Collectors.TryGetValue(name, out var config))
                {
                    if (await collector.ValidateConfigurationAsync(config))
                    {
                        await collector.InitializeAsync(config);
                        _logger?.LogInformation("收集器 {CollectorName} 注册成功", name);
                        return true;
                    }
                    else
                    {
                        _collectors.TryRemove(name, out _);
                        _plugins.TryRemove($"collector_{name}", out _);
                        _logger?.LogError("收集器 {CollectorName} 配置无效", name);
                        return false;
                    }
                }
            }

            _logger?.LogWarning("收集器 {CollectorName} 注册失败或已存在", name);
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "注册收集器 {CollectorName} 时发生错误", name);
            return false;
        }
    }

    /// <summary>
    /// 注册聚合器
    /// </summary>
    public async Task<bool> RegisterAggregatorAsync(string name, IMetricsAggregator aggregator)
    {
        try
        {
            if (_aggregators.TryAdd(name, aggregator))
            {
                _plugins.TryAdd($"aggregator_{name}", aggregator);

                // 配置聚合器
                if (_configuration.Aggregators.TryGetValue(name, out var config))
                {
                    if (await aggregator.ValidateConfigurationAsync(config))
                    {
                        await aggregator.InitializeAsync(config);
                        _logger?.LogInformation("聚合器 {AggregatorName} 注册成功", name);
                        return true;
                    }
                    else
                    {
                        _aggregators.TryRemove(name, out _);
                        _plugins.TryRemove($"aggregator_{name}", out _);
                        _logger?.LogError("聚合器 {AggregatorName} 配置无效", name);
                        return false;
                    }
                }
            }

            _logger?.LogWarning("聚合器 {AggregatorName} 注册失败或已存在", name);
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "注册聚合器 {AggregatorName} 时发生错误", name);
            return false;
        }
    }

    /// <summary>
    /// 注册分析器
    /// </summary>
    public async Task<bool> RegisterAnalyzerAsync(string name, IMetricsAnalyzer analyzer)
    {
        try
        {
            if (_analyzers.TryAdd(name, analyzer))
            {
                _plugins.TryAdd($"analyzer_{name}", analyzer);

                // 配置分析器
                if (_configuration.Analyzers.TryGetValue(name, out var config))
                {
                    if (await analyzer.ValidateConfigurationAsync(config))
                    {
                        await analyzer.InitializeAsync(config);
                        _logger?.LogInformation("分析器 {AnalyzerName} 注册成功", name);
                        return true;
                    }
                    else
                    {
                        _analyzers.TryRemove(name, out _);
                        _plugins.TryRemove($"analyzer_{name}", out _);
                        _logger?.LogError("分析器 {AnalyzerName} 配置无效", name);
                        return false;
                    }
                }
            }

            _logger?.LogWarning("分析器 {AnalyzerName} 注册失败或已存在", name);
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "注册分析器 {AnalyzerName} 时发生错误", name);
            return false;
        }
    }

    /// <summary>
    /// 注册导出器
    /// </summary>
    public async Task<bool> RegisterExporterAsync(string name, IMetricsExporter exporter)
    {
        try
        {
            if (_exporters.TryAdd(name, exporter))
            {
                _plugins.TryAdd($"exporter_{name}", exporter);

                // 配置导出器
                if (_configuration.Exporters.TryGetValue(name, out var config))
                {
                    if (await exporter.ValidateConfigurationAsync(config))
                    {
                        await exporter.InitializeAsync(config);
                        _logger?.LogInformation("导出器 {ExporterName} 注册成功", name);
                        return true;
                    }
                    else
                    {
                        _exporters.TryRemove(name, out _);
                        _plugins.TryRemove($"exporter_{name}", out _);
                        _logger?.LogError("导出器 {ExporterName} 配置无效", name);
                        return false;
                    }
                }
            }

            _logger?.LogWarning("导出器 {ExporterName} 注册失败或已存在", name);
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "注册导出器 {ExporterName} 时发生错误", name);
            return false;
        }
    }

    /// <summary>
    /// 启动系统
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            if (_isRunning || _isDisposed)
                return;
        }

        try
        {
            _logger?.LogInformation("启动指标系统集成器");

            // 启动所有组件
            await StartAllPluginsAsync(cancellationToken);

            // 设置组件间的数据流连接
            SetupDataFlowConnections();

            // 启动定时器
            _healthCheckTimer?.Change(_configuration.Global.HealthCheckInterval, _configuration.Global.HealthCheckInterval);
            _coordinationTimer?.Change(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));

            lock (_stateLock)
            {
                _isRunning = true;
            }

            _logger?.LogInformation("指标系统集成器启动完成");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "启动指标系统集成器失败");
            throw;
        }
    }

    /// <summary>
    /// 停止系统
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            if (!_isRunning || _isDisposed)
                return;

            _isRunning = false;
        }

        try
        {
            _logger?.LogInformation("停止指标系统集成器");

            // 停止定时器
            _healthCheckTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _coordinationTimer?.Change(Timeout.Infinite, Timeout.Infinite);

            // 停止所有组件
            await StopAllPluginsAsync(cancellationToken);

            _logger?.LogInformation("指标系统集成器停止完成");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "停止指标系统集成器失败");
            throw;
        }
    }

    /// <summary>
    /// 获取系统健康状态
    /// </summary>
    public async Task<SystemHealthStatus> GetSystemHealthAsync()
    {
        var systemHealth = new SystemHealthStatus
        {
            CheckTime = DateTime.UtcNow,
            IsRunning = _isRunning
        };

        foreach (var (name, plugin) in _plugins)
        {
            try
            {
                var pluginHealth = await plugin.GetHealthStatusAsync();
                systemHealth.ComponentHealths[name] = pluginHealth;

                if (!pluginHealth.IsHealthy)
                {
                    systemHealth.IsHealthy = false;
                    systemHealth.Issues.Add($"{name}: {pluginHealth.ErrorMessage ?? pluginHealth.Description}");
                }
            }
            catch (Exception ex)
            {
                systemHealth.IsHealthy = false;
                systemHealth.Issues.Add($"{name}: 健康检查异常 - {ex.Message}");
            }
        }

        return systemHealth;
    }

    /// <summary>
    /// 触发指标收集
    /// </summary>
    public async Task TriggerMetricsCollectionAsync(CancellationToken cancellationToken = default)
    {
        if (!_isRunning) return;

        var collectionTasks = _collectors.Values.Select(async collector =>
        {
            try
            {
                if (collector.IsRunning)
                {
                    await Task.Delay(100, cancellationToken); // 给收集器一些时间
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "触发收集器时发生错误");
            }
        });

        await Task.WhenAll(collectionTasks);
    }

    /// <summary>
    /// 执行数据聚合
    /// </summary>
    public async Task<List<AggregationResult>> PerformAggregationAsync(IEnumerable<JsonOptimizedMetricsEvent> metrics, CancellationToken cancellationToken = default)
    {
        if (!_isRunning) return new List<AggregationResult>();

        var results = new List<AggregationResult>();
        var metricsList = metrics.ToList();

        var aggregationTasks = _aggregators.Values.Select(async aggregator =>
        {
            try
            {
                if (aggregator.IsRunning)
                {
                    var result = await aggregator.AggregateMetricsAsync(metricsList, cancellationToken);
                    lock (results)
                    {
                        results.Add(result);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "聚合器处理时发生错误");
            }
        });

        await Task.WhenAll(aggregationTasks);
        return results;
    }

    /// <summary>
    /// 执行数据分析
    /// </summary>
    public async Task<List<AnalysisResult>> PerformAnalysisAsync(IEnumerable<JsonOptimizedMetricsEvent> metrics, CancellationToken cancellationToken = default)
    {
        if (!_isRunning) return new List<AnalysisResult>();

        var results = new List<AnalysisResult>();
        var metricsList = metrics.ToList();

        var analysisTasks = _analyzers.Values.Select(async analyzer =>
        {
            try
            {
                if (analyzer.IsRunning)
                {
                    var result = await analyzer.AnalyzeMetricsAsync(metricsList, cancellationToken);
                    lock (results)
                    {
                        results.Add(result);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "分析器处理时发生错误");
            }
        });

        await Task.WhenAll(analysisTasks);
        return results;
    }

    /// <summary>
    /// 执行数据导出
    /// </summary>
    public async Task PerformExportAsync(IEnumerable<JsonOptimizedMetricsEvent> metrics, CancellationToken cancellationToken = default)
    {
        if (!_isRunning) return;

        var metricsList = metrics.ToList();

        // 创建快照
        var snapshot = new JsonOptimizedMetricsSnapshot
        {
            Timestamp = DateTime.UtcNow,
            CollectorName = "SystemIntegrator",
            SequenceNumber = (uint)DateTime.UtcNow.Ticks,
            CollectionDuration = TimeSpan.FromMilliseconds(1),
            Sampling = new SamplingConfig(),
            Metrics = metricsList.ToDictionary(m => m.MetricName, m => m)
        };

        var exportTasks = _exporters.Values.Select(async exporter =>
        {
            try
            {
                if (exporter.IsRunning)
                {
                    await exporter.ExportAsync(snapshot, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "导出器处理时发生错误");
            }
        });

        await Task.WhenAll(exportTasks);
    }

    #region Private Methods

    private async Task StartAllPluginsAsync(CancellationToken cancellationToken)
    {
        var startTasks = _plugins.Values.Select(async plugin =>
        {
            try
            {
                if (!plugin.IsRunning)
                {
                    await plugin.StartAsync(cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "启动插件 {PluginName} 失败", plugin.Name);
            }
        });

        await Task.WhenAll(startTasks);
    }

    private async Task StopAllPluginsAsync(CancellationToken cancellationToken)
    {
        var stopTasks = _plugins.Values.Select(async plugin =>
        {
            try
            {
                if (plugin.IsRunning)
                {
                    await plugin.StopAsync(cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "停止插件 {PluginName} 失败", plugin.Name);
            }
        });

        await Task.WhenAll(stopTasks);
    }

    private void SetupDataFlowConnections()
    {
        // 设置收集器到聚合器的连接
        foreach (var (collectorName, collector) in _collectors)
        {
            if (collector is IMetricsCollector realTimeCollector)
            {
                // 连接收集器和聚合器
                // 这里可以添加事件订阅逻辑
            }
        }

        // 设置聚合器到分析器的连接
        foreach (var (aggregatorName, aggregator) in _aggregators)
        {
            aggregator.AggregationCompleted += OnAggregationCompleted;
        }

        // 设置分析器到导出器的连接
        foreach (var (analyzerName, analyzer) in _analyzers)
        {
            analyzer.AnalysisCompleted += OnAnalysisCompleted;
        }
    }

    private async void OnAggregationCompleted(AggregationCompletedEventArgs e)
    {
        try
        {
            // 触发分析
            var analysisResults = await PerformAnalysisAsync(new List<JsonOptimizedMetricsEvent>());

            // 这里可以添加更多的后续处理逻辑
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "处理聚合完成事件时发生错误");
        }
    }

    private async void OnAnalysisCompleted(AnalysisCompletedEventArgs e)
    {
        try
        {
            // 触发导出
            await PerformExportAsync(new List<JsonOptimizedMetricsEvent>());

            // 这里可以添加更多的后续处理逻辑
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "处理分析完成事件时发生错误");
        }
    }

    private async void PerformHealthCheck(object? state)
    {
        if (!_isRunning) return;

        try
        {
            var systemHealth = await GetSystemHealthAsync();

            if (!systemHealth.IsHealthy)
            {
                _logger?.LogWarning("系统健康检查发现问题: {Issues}", string.Join(", ", systemHealth.Issues));
            }
            else
            {
                _logger?.LogDebug("系统健康检查正常");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "执行健康检查时发生错误");
        }
    }

    private async void CoordinateComponents(object? state)
    {
        if (!_isRunning) return;

        try
        {
            // 协调组件运行
            // 检查是否有组件停止运行并尝试重启
            foreach (var (name, plugin) in _plugins)
            {
                if (!plugin.IsRunning)
                {
                    _logger?.LogWarning("检测到插件 {PluginName} 已停止，尝试重启", name);
                    try
                    {
                        await plugin.StartAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "重启插件 {PluginName} 失败", name);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "协调组件时发生错误");
        }
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (_isDisposed) return;

        try
        {
            StopAsync().Wait(TimeSpan.FromSeconds(10));
        }
        catch
        {
            // 忽略停止时的异常
        }

        _healthCheckTimer?.Dispose();
        _coordinationTimer?.Dispose();

        // 释放所有插件
        foreach (var plugin in _plugins.Values)
        {
            if (plugin is IDisposable disposable)
            {
                disposable.Dispose();
            }
            else if (plugin is IAsyncDisposable asyncDisposable)
            {
                try
                {
                    asyncDisposable.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));
                }
                catch
                {
                    // 忽略释放时的异常
                }
            }
        }

        _isDisposed = true;
    }

    #endregion
}

/// <summary>
/// 系统健康状态
/// </summary>
public class SystemHealthStatus
{
    /// <summary>
    /// 检查时间
    /// </summary>
    public DateTime CheckTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 是否正在运行
    /// </summary>
    public bool IsRunning { get; set; }

    /// <summary>
    /// 是否健康
    /// </summary>
    public bool IsHealthy { get; set; } = true;

    /// <summary>
    /// 组件健康状态
    /// </summary>
    public Dictionary<string, PluginHealthStatus> ComponentHealths { get; set; } = new();

    /// <summary>
    /// 问题列表
    /// </summary>
    public List<string> Issues { get; set; } = new();

    /// <summary>
    /// 系统指标
    /// </summary>
    public Dictionary<string, object> SystemMetrics { get; set; } = new();
}
