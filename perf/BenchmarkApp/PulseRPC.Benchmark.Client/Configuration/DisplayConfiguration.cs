namespace PulseRPC.Benchmark.Client.Configuration;

/// <summary>
/// 显示配置类
/// </summary>
public class DisplayConfiguration
{
    /// <summary>
    /// 是否启用实时显示
    /// </summary>
    public bool EnableRealTimeDisplay { get; set; } = true;

    /// <summary>
    /// 刷新频率（毫秒）
    /// </summary>
    public int RefreshIntervalMs { get; set; } = 500;

    /// <summary>
    /// 是否启用彩色显示
    /// </summary>
    public bool EnableColors { get; set; } = true;

    /// <summary>
    /// 是否启用Unicode字符
    /// </summary>
    public bool EnableUnicodeChars { get; set; } = true;

    /// <summary>
    /// 是否在不支持ANSI的终端使用简化显示
    /// </summary>
    public bool FallbackToSimpleDisplay { get; set; } = true;

    /// <summary>
    /// 简化显示的更新间隔（秒）
    /// </summary>
    public int SimpleDisplayUpdateIntervalSeconds { get; set; } = 5;

    /// <summary>
    /// 是否显示系统资源监控
    /// </summary>
    public bool ShowSystemResources { get; set; } = true;

    /// <summary>
    /// 是否显示详细的延迟统计
    /// </summary>
    public bool ShowDetailedLatency { get; set; } = true;

    /// <summary>
    /// 验证配置
    /// </summary>
    public void Validate()
    {
        if (RefreshIntervalMs <= 0)
            throw new ArgumentException("刷新间隔必须大于0");

        if (SimpleDisplayUpdateIntervalSeconds <= 0)
            throw new ArgumentException("简化显示更新间隔必须大于0");
    }
} 