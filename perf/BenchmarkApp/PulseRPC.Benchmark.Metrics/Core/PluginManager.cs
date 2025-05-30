using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using PulseRPC.Benchmark.Metrics.Abstractions;
using PulseRPC.Benchmark.Metrics.Models;

namespace PulseRPC.Benchmark.Metrics.Core;

/// <summary>
/// 插件管理器 - 负责插件生命周期管理和协调
/// </summary>
public class PluginManager : IAsyncDisposable
{
    private readonly ILogger<PluginManager>? _logger;
    private readonly ConcurrentDictionary<string, IMetricsPlugin> _plugins;
    private readonly ConcurrentDictionary<string, PluginMetadata> _pluginMetadata;
    private readonly object _lockObject = new();
    private bool _disposed = false;

    public PluginManager(ILogger<PluginManager>? logger = null)
    {
        _logger = logger;
        _plugins = new ConcurrentDictionary<string, IMetricsPlugin>();
        _pluginMetadata = new ConcurrentDictionary<string, PluginMetadata>();
    }

    #region Plugin Registration

    /// <summary>
    /// 注册插件
    /// </summary>
    /// <param name="plugin">插件实例</param>
    /// <param name="configuration">插件配置</param>
    /// <returns>是否注册成功</returns>
    public async Task<bool> RegisterPluginAsync(IMetricsPlugin plugin, object? configuration = null)
    {
        if (plugin == null)
            throw new ArgumentNullException(nameof(plugin));

        if (_disposed)
            throw new ObjectDisposedException(nameof(PluginManager));

        try
        {
            var pluginName = plugin.Name;

            // 检查是否已注册
            if (_plugins.ContainsKey(pluginName))
            {
                _logger?.LogWarning("插件 {PluginName} 已经注册", pluginName);
                return false;
            }

            // 验证配置
            if (!await plugin.ValidateConfigurationAsync(configuration))
            {
                _logger?.LogError("插件 {PluginName} 配置验证失败", pluginName);
                return false;
            }

            // 注册插件事件
            plugin.StatusChanged += OnPluginStatusChanged;
            plugin.ErrorOccurred += OnPluginErrorOccurred;

            // 添加到集合
            if (_plugins.TryAdd(pluginName, plugin))
            {
                var metadata = new PluginMetadata
                {
                    Name = pluginName,
                    Version = plugin.Version,
                    Description = plugin.Description,
                    Author = plugin.Author,
                    RegisteredAt = DateTime.UtcNow,
                    Configuration = configuration
                };

                _pluginMetadata.TryAdd(pluginName, metadata);

                // 初始化插件
                await plugin.InitializeAsync(configuration);

                _logger?.LogInformation("插件 {PluginName} v{Version} 注册成功", pluginName, plugin.Version);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "注册插件 {PluginName} 失败", plugin.Name);
            return false;
        }
    }

    /// <summary>
    /// 注销插件
    /// </summary>
    /// <param name="pluginName">插件名称</param>
    /// <returns>是否注销成功</returns>
    public async Task<bool> UnregisterPluginAsync(string pluginName)
    {
        if (string.IsNullOrEmpty(pluginName))
            throw new ArgumentException("插件名称不能为空", nameof(pluginName));

        if (_disposed)
            throw new ObjectDisposedException(nameof(PluginManager));

        try
        {
            if (_plugins.TryRemove(pluginName, out var plugin))
            {
                // 取消事件订阅
                plugin.StatusChanged -= OnPluginStatusChanged;
                plugin.ErrorOccurred -= OnPluginErrorOccurred;

                // 停止并释放插件
                if (plugin.IsRunning)
                {
                    await plugin.StopAsync();
                }

                await plugin.DisposeAsync();

                // 移除元数据
                _pluginMetadata.TryRemove(pluginName, out _);

                _logger?.LogInformation("插件 {PluginName} 注销成功", pluginName);
                return true;
            }

            _logger?.LogWarning("插件 {PluginName} 未找到", pluginName);
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "注销插件 {PluginName} 失败", pluginName);
            return false;
        }
    }

    #endregion

    #region Plugin Lifecycle Management

    /// <summary>
    /// 启动指定插件
    /// </summary>
    /// <param name="pluginName">插件名称</param>
    /// <returns>是否启动成功</returns>
    public async Task<bool> StartPluginAsync(string pluginName)
    {
        if (_plugins.TryGetValue(pluginName, out var plugin))
        {
            try
            {
                if (!plugin.IsRunning)
                {
                    await plugin.StartAsync();
                    _logger?.LogInformation("插件 {PluginName} 启动成功", pluginName);
                    return true;
                }

                _logger?.LogWarning("插件 {PluginName} 已经在运行", pluginName);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "启动插件 {PluginName} 失败", pluginName);
                return false;
            }
        }

        _logger?.LogWarning("插件 {PluginName} 未找到", pluginName);
        return false;
    }

    /// <summary>
    /// 停止指定插件
    /// </summary>
    /// <param name="pluginName">插件名称</param>
    /// <returns>是否停止成功</returns>
    public async Task<bool> StopPluginAsync(string pluginName)
    {
        if (_plugins.TryGetValue(pluginName, out var plugin))
        {
            try
            {
                if (plugin.IsRunning)
                {
                    await plugin.StopAsync();
                    _logger?.LogInformation("插件 {PluginName} 停止成功", pluginName);
                    return true;
                }

                _logger?.LogWarning("插件 {PluginName} 未在运行", pluginName);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "停止插件 {PluginName} 失败", pluginName);
                return false;
            }
        }

        _logger?.LogWarning("插件 {PluginName} 未找到", pluginName);
        return false;
    }

    /// <summary>
    /// 启动所有插件
    /// </summary>
    /// <returns>启动结果</returns>
    public async Task<PluginOperationResult> StartAllPluginsAsync()
    {
        var result = new PluginOperationResult { Operation = "StartAll" };

        foreach (var kvp in _plugins)
        {
            try
            {
                if (!kvp.Value.IsRunning)
                {
                    await kvp.Value.StartAsync();
                    result.SuccessfulPlugins.Add(kvp.Key);
                }
                else
                {
                    result.SkippedPlugins.Add(kvp.Key);
                }
            }
            catch (Exception ex)
            {
                result.FailedPlugins.Add(kvp.Key, ex.Message);
                _logger?.LogError(ex, "启动插件 {PluginName} 失败", kvp.Key);
            }
        }

        _logger?.LogInformation("启动所有插件完成: 成功 {Success}, 跳过 {Skipped}, 失败 {Failed}",
            result.SuccessfulPlugins.Count, result.SkippedPlugins.Count, result.FailedPlugins.Count);

        return result;
    }

    /// <summary>
    /// 停止所有插件
    /// </summary>
    /// <returns>停止结果</returns>
    public async Task<PluginOperationResult> StopAllPluginsAsync()
    {
        var result = new PluginOperationResult { Operation = "StopAll" };

        foreach (var kvp in _plugins)
        {
            try
            {
                if (kvp.Value.IsRunning)
                {
                    await kvp.Value.StopAsync();
                    result.SuccessfulPlugins.Add(kvp.Key);
                }
                else
                {
                    result.SkippedPlugins.Add(kvp.Key);
                }
            }
            catch (Exception ex)
            {
                result.FailedPlugins.Add(kvp.Key, ex.Message);
                _logger?.LogError(ex, "停止插件 {PluginName} 失败", kvp.Key);
            }
        }

        _logger?.LogInformation("停止所有插件完成: 成功 {Success}, 跳过 {Skipped}, 失败 {Failed}",
            result.SuccessfulPlugins.Count, result.SkippedPlugins.Count, result.FailedPlugins.Count);

        return result;
    }

    #endregion

    #region Plugin Information

    /// <summary>
    /// 获取插件
    /// </summary>
    /// <typeparam name="T">插件类型</typeparam>
    /// <param name="pluginName">插件名称</param>
    /// <returns>插件实例</returns>
    public T? GetPlugin<T>(string pluginName) where T : class, IMetricsPlugin
    {
        return _plugins.TryGetValue(pluginName, out var plugin) ? plugin as T : null;
    }

    /// <summary>
    /// 获取所有指定类型的插件
    /// </summary>
    /// <typeparam name="T">插件类型</typeparam>
    /// <returns>插件列表</returns>
    public List<T> GetPlugins<T>() where T : class, IMetricsPlugin
    {
        return _plugins.Values.OfType<T>().ToList();
    }

    /// <summary>
    /// 获取所有插件信息
    /// </summary>
    /// <returns>插件信息列表</returns>
    public List<PluginInfo> GetAllPluginInfo()
    {
        var infos = new List<PluginInfo>();

        foreach (var kvp in _plugins)
        {
            var plugin = kvp.Value;
            var metadata = _pluginMetadata.TryGetValue(kvp.Key, out var meta) ? meta : null;

            infos.Add(new PluginInfo
            {
                Name = plugin.Name,
                Version = plugin.Version,
                Description = plugin.Description,
                Author = plugin.Author,
                IsInitialized = plugin.IsInitialized,
                IsRunning = plugin.IsRunning,
                RegisteredAt = metadata?.RegisteredAt ?? DateTime.MinValue,
                Type = plugin.GetType().Name
            });
        }

        return infos;
    }

    /// <summary>
    /// 获取插件健康状态
    /// </summary>
    /// <returns>健康状态报告</returns>
    public async Task<PluginHealthReport> GetHealthReportAsync()
    {
        var report = new PluginHealthReport
        {
            CheckTime = DateTime.UtcNow,
            TotalPlugins = _plugins.Count
        };

        foreach (var kvp in _plugins)
        {
            try
            {
                var healthStatus = await kvp.Value.GetHealthStatusAsync();
                var pluginHealth = new PluginHealthInfo
                {
                    PluginName = kvp.Key,
                    IsHealthy = healthStatus.IsHealthy,
                    Description = healthStatus.Description,
                    ErrorMessage = healthStatus.ErrorMessage,
                    LastCheckTime = healthStatus.LastCheckTime,
                    Metrics = new Dictionary<string, object>(healthStatus.Metrics)
                };

                report.PluginHealths.Add(pluginHealth);

                if (healthStatus.IsHealthy)
                {
                    report.HealthyPlugins++;
                }
                else
                {
                    report.UnhealthyPlugins++;
                }
            }
            catch (Exception ex)
            {
                report.PluginHealths.Add(new PluginHealthInfo
                {
                    PluginName = kvp.Key,
                    IsHealthy = false,
                    Description = "健康检查异常",
                    ErrorMessage = ex.Message,
                    LastCheckTime = DateTime.UtcNow
                });

                report.UnhealthyPlugins++;
                _logger?.LogWarning(ex, "获取插件 {PluginName} 健康状态失败", kvp.Key);
            }
        }

        return report;
    }

    #endregion

    #region Event Handling

    private void OnPluginStatusChanged(PluginStatusChangedEventArgs e)
    {
        _logger?.LogDebug("插件 {PluginName} 状态变更: {OldStatus} -> {NewStatus}",
            e.PluginName, e.OldStatus, e.NewStatus);

        PluginStatusChanged?.Invoke(e);
    }

    private void OnPluginErrorOccurred(PluginErrorEventArgs e)
    {
        _logger?.LogError(e.Exception, "插件 {PluginName} 发生错误: {Context}",
            e.PluginName, e.Context);

        PluginErrorOccurred?.Invoke(e);
    }

    /// <summary>
    /// 插件状态变更事件
    /// </summary>
    public event Action<PluginStatusChangedEventArgs>? PluginStatusChanged;

    /// <summary>
    /// 插件错误事件
    /// </summary>
    public event Action<PluginErrorEventArgs>? PluginErrorOccurred;

    #endregion

    #region IAsyncDisposable

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        try
        {
            _logger?.LogInformation("开始释放插件管理器");

            // 停止并释放所有插件
            var disposeTasks = _plugins.Values.Select(async plugin =>
            {
                try
                {
                    if (plugin.IsRunning)
                    {
                        await plugin.StopAsync();
                    }
                    await plugin.DisposeAsync();
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "释放插件 {PluginName} 失败", plugin.Name);
                }
            });

            await Task.WhenAll(disposeTasks);

            _plugins.Clear();
            _pluginMetadata.Clear();

            _disposed = true;
            _logger?.LogInformation("插件管理器释放完成");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "释放插件管理器失败");
        }
    }

    #endregion
}

/// <summary>
/// 插件元数据
/// </summary>
public class PluginMetadata
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public DateTime RegisteredAt { get; set; }
    public object? Configuration { get; set; }
}

/// <summary>
/// 插件信息
/// </summary>
public class PluginInfo
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool IsInitialized { get; set; }
    public bool IsRunning { get; set; }
    public DateTime RegisteredAt { get; set; }
}

/// <summary>
/// 插件操作结果
/// </summary>
public class PluginOperationResult
{
    public string Operation { get; set; } = string.Empty;
    public List<string> SuccessfulPlugins { get; set; } = new();
    public List<string> SkippedPlugins { get; set; } = new();
    public Dictionary<string, string> FailedPlugins { get; set; } = new();

    public bool IsSuccess => FailedPlugins.Count == 0;
    public int TotalProcessed => SuccessfulPlugins.Count + SkippedPlugins.Count + FailedPlugins.Count;
}

/// <summary>
/// 插件健康报告
/// </summary>
public class PluginHealthReport
{
    public DateTime CheckTime { get; set; }
    public int TotalPlugins { get; set; }
    public int HealthyPlugins { get; set; }
    public int UnhealthyPlugins { get; set; }
    public List<PluginHealthInfo> PluginHealths { get; set; } = new();

    public bool IsOverallHealthy => UnhealthyPlugins == 0;
    public double HealthPercentage => TotalPlugins > 0 ? (double)HealthyPlugins / TotalPlugins * 100 : 0;
}

/// <summary>
/// 插件健康信息
/// </summary>
public class PluginHealthInfo
{
    public string PluginName { get; set; } = string.Empty;
    public bool IsHealthy { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public DateTime LastCheckTime { get; set; }
    public Dictionary<string, object> Metrics { get; set; } = new();
}
