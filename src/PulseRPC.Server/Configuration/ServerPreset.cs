namespace PulseRPC.Server.Configuration;

/// <summary>
/// 服务器预设模式
/// </summary>
public enum ServerPreset
{
    /// <summary>
    /// 默认配置 - 适用于大多数场景
    /// </summary>
    Default,

    /// <summary>
    /// 高吞吐量模式 - 优化批量处理和队列容量
    /// 适用于：批量数据处理、日志收集、消息队列场景
    /// </summary>
    HighThroughput,

    /// <summary>
    /// 低延迟模式 - 优化响应速度
    /// 适用于：实时游戏、金融交易、即时通讯
    /// </summary>
    LowLatency,

    /// <summary>
    /// 平衡模式 - 在吞吐量和延迟之间取得平衡
    /// 适用于：通用 Web 服务、API 网关
    /// </summary>
    Balanced,

    /// <summary>
    /// 最小资源模式 - 最小化内存和 CPU 使用
    /// 适用于：边缘设备、嵌入式系统、资源受限环境
    /// </summary>
    Minimal,

    /// <summary>
    /// 开发模式 - 启用详细日志和调试功能
    /// 适用于：开发调试、问题排查
    /// </summary>
    Development
}

/// <summary>
/// 服务器预设配置工厂
/// </summary>
public static class ServerPresets
{
    /// <summary>
    /// 应用预设配置到选项
    /// </summary>
    /// <param name="options">要配置的选项</param>
    /// <param name="preset">预设模式</param>
    public static void ApplyPreset(PulseServerOptions options, ServerPreset preset)
    {
        switch (preset)
        {
            case ServerPreset.Default:
                ApplyDefaultPreset(options);
                break;
            case ServerPreset.HighThroughput:
                ApplyHighThroughputPreset(options);
                break;
            case ServerPreset.LowLatency:
                ApplyLowLatencyPreset(options);
                break;
            case ServerPreset.Balanced:
                ApplyBalancedPreset(options);
                break;
            case ServerPreset.Minimal:
                ApplyMinimalPreset(options);
                break;
            case ServerPreset.Development:
                ApplyDevelopmentPreset(options);
                break;
            default:
                ApplyDefaultPreset(options);
                break;
        }
    }

    /// <summary>
    /// 默认配置
    /// </summary>
    private static void ApplyDefaultPreset(PulseServerOptions options)
    {
        options.DefaultOperationTimeout = TimeSpan.FromSeconds(30);
        options.MaxConcurrentOperations = 1000;
        options.EnableDetailedLogging = false;

        // BackpressurePolicy 默认配置
        options.BackpressurePolicy.ThrottleThreshold = 0.7;
        options.BackpressurePolicy.RejectThreshold = 0.9;
        options.BackpressurePolicy.Hysteresis = 0.1;
        options.BackpressurePolicy.ThrottleRate = 0.5;
    }

    /// <summary>
    /// 高吞吐量配置 - 优化批量处理
    /// </summary>
    private static void ApplyHighThroughputPreset(PulseServerOptions options)
    {
        options.DefaultOperationTimeout = TimeSpan.FromSeconds(60);
        options.MaxConcurrentOperations = 5000;
        options.EnableDetailedLogging = false;

        // BackpressurePolicy 高吞吐配置 - 更高阈值容忍更多压力
        options.BackpressurePolicy.ThrottleThreshold = 0.85;
        options.BackpressurePolicy.RejectThreshold = 0.95;
        options.BackpressurePolicy.Hysteresis = 0.05;
        options.BackpressurePolicy.ThrottleRate = 0.3;
    }

    /// <summary>
    /// 低延迟配置 - 优化响应速度
    /// </summary>
    private static void ApplyLowLatencyPreset(PulseServerOptions options)
    {
        options.DefaultOperationTimeout = TimeSpan.FromSeconds(10);
        options.MaxConcurrentOperations = 2000;
        options.EnableDetailedLogging = false;

        // BackpressurePolicy 低延迟配置 - 较低阈值快速限流
        options.BackpressurePolicy.ThrottleThreshold = 0.5;
        options.BackpressurePolicy.RejectThreshold = 0.75;
        options.BackpressurePolicy.Hysteresis = 0.15;
        options.BackpressurePolicy.ThrottleRate = 0.7;
    }

    /// <summary>
    /// 平衡配置
    /// </summary>
    private static void ApplyBalancedPreset(PulseServerOptions options)
    {
        options.DefaultOperationTimeout = TimeSpan.FromSeconds(30);
        options.MaxConcurrentOperations = 2000;
        options.EnableDetailedLogging = false;

        // BackpressurePolicy 平衡配置
        options.BackpressurePolicy.ThrottleThreshold = 0.65;
        options.BackpressurePolicy.RejectThreshold = 0.85;
        options.BackpressurePolicy.Hysteresis = 0.1;
        options.BackpressurePolicy.ThrottleRate = 0.5;
    }

    /// <summary>
    /// 最小资源配置
    /// </summary>
    private static void ApplyMinimalPreset(PulseServerOptions options)
    {
        options.DefaultOperationTimeout = TimeSpan.FromSeconds(30);
        options.MaxConcurrentOperations = 100;
        options.EnableDetailedLogging = false;

        // BackpressurePolicy 最小配置
        options.BackpressurePolicy.ThrottleThreshold = 0.6;
        options.BackpressurePolicy.RejectThreshold = 0.8;
        options.BackpressurePolicy.Hysteresis = 0.1;
        options.BackpressurePolicy.ThrottleRate = 0.6;
    }

    /// <summary>
    /// 开发模式配置
    /// </summary>
    private static void ApplyDevelopmentPreset(PulseServerOptions options)
    {
        // 基于默认配置
        ApplyDefaultPreset(options);

        // 启用详细日志
        options.EnableDetailedLogging = true;

        // 延长超时便于调试
        options.DefaultOperationTimeout = TimeSpan.FromMinutes(5);
    }
}
