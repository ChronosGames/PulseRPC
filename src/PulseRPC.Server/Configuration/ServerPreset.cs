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
    public static void ApplyPreset(UnifiedServerOptions options, ServerPreset preset)
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
    private static void ApplyDefaultPreset(UnifiedServerOptions options)
    {
        options.DefaultOperationTimeout = TimeSpan.FromSeconds(30);
        options.MaxConcurrentOperations = 1000;
        options.EnableDetailedLogging = false;

        // MessageReceiver 默认配置
        options.MessageReceiver.MaxBufferSize = 16 * 1024 * 1024; // 16MB

        // ResponseTransmitter 默认配置
        options.ResponseTransmitter.WorkerCount = 2;
        options.ResponseTransmitter.QueueCapacity = 10_000;
        options.ResponseTransmitter.MaxBatchSize = 50;
        options.ResponseTransmitter.MaxBatchDelayMs = 1;

        // BackpressurePolicy 默认配置
        options.BackpressurePolicy.ThrottleThreshold = 0.7;
        options.BackpressurePolicy.RejectThreshold = 0.9;
        options.BackpressurePolicy.Hysteresis = 0.1;
        options.BackpressurePolicy.ThrottleRate = 0.5;
    }

    /// <summary>
    /// 高吞吐量配置 - 优化批量处理
    /// </summary>
    private static void ApplyHighThroughputPreset(UnifiedServerOptions options)
    {
        options.DefaultOperationTimeout = TimeSpan.FromSeconds(60);
        options.MaxConcurrentOperations = 5000;
        options.EnableDetailedLogging = false;

        // MessageReceiver 高吞吐配置 - 更大的缓冲区
        options.MessageReceiver.MaxBufferSize = 64 * 1024 * 1024; // 64MB

        // ResponseTransmitter 高吞吐配置 - 更多工作线程、大批量
        options.ResponseTransmitter.WorkerCount = Environment.ProcessorCount;
        options.ResponseTransmitter.QueueCapacity = 50_000;
        options.ResponseTransmitter.MaxBatchSize = 128;
        options.ResponseTransmitter.MaxBatchDelayMs = 5;

        // BackpressurePolicy 高吞吐配置 - 更高阈值容忍更多压力
        options.BackpressurePolicy.ThrottleThreshold = 0.85;
        options.BackpressurePolicy.RejectThreshold = 0.95;
        options.BackpressurePolicy.Hysteresis = 0.05;
        options.BackpressurePolicy.ThrottleRate = 0.3;
    }

    /// <summary>
    /// 低延迟配置 - 优化响应速度
    /// </summary>
    private static void ApplyLowLatencyPreset(UnifiedServerOptions options)
    {
        options.DefaultOperationTimeout = TimeSpan.FromSeconds(10);
        options.MaxConcurrentOperations = 2000;
        options.EnableDetailedLogging = false;

        // MessageReceiver 低延迟配置 - 较小缓冲区快速处理
        options.MessageReceiver.MaxBufferSize = 8 * 1024 * 1024; // 8MB

        // ResponseTransmitter 低延迟配置 - 小批量、无延迟
        options.ResponseTransmitter.WorkerCount = 4;
        options.ResponseTransmitter.QueueCapacity = 5_000;
        options.ResponseTransmitter.MaxBatchSize = 8;
        options.ResponseTransmitter.MaxBatchDelayMs = 0; // 立即发送

        // BackpressurePolicy 低延迟配置 - 较低阈值快速限流
        options.BackpressurePolicy.ThrottleThreshold = 0.5;
        options.BackpressurePolicy.RejectThreshold = 0.75;
        options.BackpressurePolicy.Hysteresis = 0.15;
        options.BackpressurePolicy.ThrottleRate = 0.7;
    }

    /// <summary>
    /// 平衡配置
    /// </summary>
    private static void ApplyBalancedPreset(UnifiedServerOptions options)
    {
        options.DefaultOperationTimeout = TimeSpan.FromSeconds(30);
        options.MaxConcurrentOperations = 2000;
        options.EnableDetailedLogging = false;

        // MessageReceiver 平衡配置
        options.MessageReceiver.MaxBufferSize = 32 * 1024 * 1024; // 32MB

        // ResponseTransmitter 平衡配置
        options.ResponseTransmitter.WorkerCount = Math.Max(2, Environment.ProcessorCount / 2);
        options.ResponseTransmitter.QueueCapacity = 20_000;
        options.ResponseTransmitter.MaxBatchSize = 64;
        options.ResponseTransmitter.MaxBatchDelayMs = 2;

        // BackpressurePolicy 平衡配置
        options.BackpressurePolicy.ThrottleThreshold = 0.65;
        options.BackpressurePolicy.RejectThreshold = 0.85;
        options.BackpressurePolicy.Hysteresis = 0.1;
        options.BackpressurePolicy.ThrottleRate = 0.5;
    }

    /// <summary>
    /// 最小资源配置
    /// </summary>
    private static void ApplyMinimalPreset(UnifiedServerOptions options)
    {
        options.DefaultOperationTimeout = TimeSpan.FromSeconds(30);
        options.MaxConcurrentOperations = 100;
        options.EnableDetailedLogging = false;

        // MessageReceiver 最小配置
        options.MessageReceiver.MaxBufferSize = 4 * 1024 * 1024; // 4MB

        // ResponseTransmitter 最小配置
        options.ResponseTransmitter.WorkerCount = 1;
        options.ResponseTransmitter.QueueCapacity = 1_000;
        options.ResponseTransmitter.MaxBatchSize = 16;
        options.ResponseTransmitter.MaxBatchDelayMs = 5;

        // BackpressurePolicy 最小配置
        options.BackpressurePolicy.ThrottleThreshold = 0.6;
        options.BackpressurePolicy.RejectThreshold = 0.8;
        options.BackpressurePolicy.Hysteresis = 0.1;
        options.BackpressurePolicy.ThrottleRate = 0.6;
    }

    /// <summary>
    /// 开发模式配置
    /// </summary>
    private static void ApplyDevelopmentPreset(UnifiedServerOptions options)
    {
        // 基于默认配置
        ApplyDefaultPreset(options);

        // 启用详细日志
        options.EnableDetailedLogging = true;

        // 延长超时便于调试
        options.DefaultOperationTimeout = TimeSpan.FromMinutes(5);
    }
}
