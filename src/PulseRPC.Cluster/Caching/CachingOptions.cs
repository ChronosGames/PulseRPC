namespace PulseRPC.Caching;

/// <summary>
/// 缓存配置选项
/// </summary>
public class CachingOptions
{
    /// <summary>
    /// 是否启用缓存
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 默认TTL
    /// </summary>
    public TimeSpan DefaultTtl { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 最大缓存条目数
    /// </summary>
    public int MaxEntries { get; set; } = 1000;

    /// <summary>
    /// 缓存刷新间隔
    /// </summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 是否在后台刷新
    /// </summary>
    public bool BackgroundRefresh { get; set; } = true;
}
