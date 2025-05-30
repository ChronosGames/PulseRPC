namespace PulseRPC.Benchmark.Metrics.Abstractions;

/// <summary>
/// 指标插件基础接口
/// </summary>
public interface IMetricsPlugin : IAsyncDisposable
{
    /// <summary>
    /// 插件名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 插件版本
    /// </summary>
    string Version { get; }

    /// <summary>
    /// 插件描述
    /// </summary>
    string Description { get; }

    /// <summary>
    /// 插件作者
    /// </summary>
    string Author { get; }

    /// <summary>
    /// 是否已初始化
    /// </summary>
    bool IsInitialized { get; }

    /// <summary>
    /// 是否正在运行
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// 验证配置
    /// </summary>
    /// <param name="configuration">配置对象</param>
    /// <returns>是否有效</returns>
    Task<bool> ValidateConfigurationAsync(object? configuration);

    /// <summary>
    /// 初始化插件
    /// </summary>
    /// <param name="configuration">配置对象</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>初始化任务</returns>
    Task InitializeAsync(object? configuration, CancellationToken cancellationToken = default);

    /// <summary>
    /// 启动插件
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>启动任务</returns>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止插件
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>停止任务</returns>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取插件健康状态
    /// </summary>
    /// <returns>健康状态</returns>
    Task<PluginHealthStatus> GetHealthStatusAsync();

    /// <summary>
    /// 插件状态变化事件
    /// </summary>
    event Action<PluginStatusChangedEventArgs>? StatusChanged;

    /// <summary>
    /// 错误发生事件
    /// </summary>
    event Action<PluginErrorEventArgs>? ErrorOccurred;
}

/// <summary>
/// 插件健康状态
/// </summary>
public class PluginHealthStatus
{
    /// <summary>
    /// 是否健康
    /// </summary>
    public bool IsHealthy { get; set; }

    /// <summary>
    /// 状态描述
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 最后检查时间
    /// </summary>
    public DateTime LastCheckTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 错误消息
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 性能指标
    /// </summary>
    public Dictionary<string, object> Metrics { get; set; } = new();

    /// <summary>
    /// 创建健康状态
    /// </summary>
    /// <param name="description">描述</param>
    /// <returns>健康状态</returns>
    public static PluginHealthStatus Healthy(string description = "Plugin is running normally")
    {
        return new PluginHealthStatus
        {
            IsHealthy = true,
            Description = description
        };
    }

    /// <summary>
    /// 创建不健康状态
    /// </summary>
    /// <param name="errorMessage">错误消息</param>
    /// <param name="description">描述</param>
    /// <returns>不健康状态</returns>
    public static PluginHealthStatus Unhealthy(string errorMessage, string description = "Plugin encountered an error")
    {
        return new PluginHealthStatus
        {
            IsHealthy = false,
            Description = description,
            ErrorMessage = errorMessage
        };
    }
}

/// <summary>
/// 插件状态
/// </summary>
public enum PluginStatus
{
    /// <summary>
    /// 未初始化
    /// </summary>
    NotInitialized,

    /// <summary>
    /// 已初始化
    /// </summary>
    Initialized,

    /// <summary>
    /// 正在启动
    /// </summary>
    Starting,

    /// <summary>
    /// 运行中
    /// </summary>
    Running,

    /// <summary>
    /// 正在停止
    /// </summary>
    Stopping,

    /// <summary>
    /// 已停止
    /// </summary>
    Stopped,

    /// <summary>
    /// 错误状态
    /// </summary>
    Error,

    /// <summary>
    /// 已销毁
    /// </summary>
    Disposed
}

/// <summary>
/// 插件状态变化事件参数
/// </summary>
public class PluginStatusChangedEventArgs : EventArgs
{
    /// <summary>
    /// 插件名称
    /// </summary>
    public string PluginName { get; }

    /// <summary>
    /// 旧状态
    /// </summary>
    public PluginStatus OldStatus { get; }

    /// <summary>
    /// 新状态
    /// </summary>
    public PluginStatus NewStatus { get; }

    /// <summary>
    /// 变化时间
    /// </summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// 附加信息
    /// </summary>
    public string? Message { get; }

    public PluginStatusChangedEventArgs(string pluginName, PluginStatus oldStatus, PluginStatus newStatus, string? message = null)
    {
        PluginName = pluginName;
        OldStatus = oldStatus;
        NewStatus = newStatus;
        Timestamp = DateTime.UtcNow;
        Message = message;
    }
}

/// <summary>
/// 插件错误事件参数
/// </summary>
public class PluginErrorEventArgs : EventArgs
{
    /// <summary>
    /// 插件名称
    /// </summary>
    public string PluginName { get; }

    /// <summary>
    /// 异常信息
    /// </summary>
    public Exception Exception { get; }

    /// <summary>
    /// 错误级别
    /// </summary>
    public ErrorLevel Level { get; }

    /// <summary>
    /// 发生时间
    /// </summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// 错误上下文
    /// </summary>
    public string? Context { get; }

    public PluginErrorEventArgs(string pluginName, Exception exception, ErrorLevel level = ErrorLevel.Error, string? context = null)
    {
        PluginName = pluginName;
        Exception = exception;
        Level = level;
        Timestamp = DateTime.UtcNow;
        Context = context;
    }
}

/// <summary>
/// 错误级别
/// </summary>
public enum ErrorLevel
{
    /// <summary>
    /// 警告
    /// </summary>
    Warning,

    /// <summary>
    /// 错误
    /// </summary>
    Error,

    /// <summary>
    /// 严重错误
    /// </summary>
    Critical
}
