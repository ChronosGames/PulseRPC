using System;

namespace PulseRPC.Client.Channels;

/// <summary>
/// TransportChannel 配置选项
/// </summary>
/// <remarks>
/// <para>推荐使用 <see cref="PulseRPC.Client.ChannelPresets"/> 预设配置。</para>
/// <code>
/// // 使用预设
/// var options = ChannelPresets.LowLatency;
/// var options = ChannelPresets.HighReliability;
/// </code>
/// </remarks>
public class TransportChannelOptions
{
    #region 核心配置

    /// <summary>
    /// 默认超时时间
    /// </summary>
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 消息队列容量
    /// </summary>
    public int MessageQueueCapacity { get; set; } = 1000;

    /// <summary>
    /// 消息处理并发数
    /// </summary>
    public int MessageProcessingConcurrency { get; set; } = 4;

    /// <summary>
    /// 消息处理超时时间
    /// </summary>
    public TimeSpan MessageProcessingTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 心跳间隔
    /// </summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 心跳超时时间
    /// </summary>
    public TimeSpan HeartbeatTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 是否启用自动重连
    /// </summary>
    public bool EnableAutoReconnect { get; set; } = true;

    /// <summary>
    /// 最大重连次数
    /// </summary>
    public int MaxReconnectAttempts { get; set; } = 5;

    #endregion

    #region 性能优化配置

    /// <summary>
    /// 是否立即刷新发送缓冲区
    /// </summary>
    /// <remarks>
    /// <para>设为 false 可启用批量发送，提高吞吐量但可能增加延迟。</para>
    /// <para>设为 true 则每次请求立即发送，延迟更低但吞吐量下降。</para>
    /// <para>默认为 true 保持向后兼容；高吞吐场景可设为 false。</para>
    /// </remarks>
    public bool ImmediateFlush { get; set; } = true;

    /// <summary>
    /// 发送缓冲区 L1 批量大小
    /// </summary>
    /// <remarks>
    /// 达到此数量后自动刷新到 L2 队列。默认 16。
    /// </remarks>
    public int SendBufferL1BatchSize { get; set; } = 16;

    /// <summary>
    /// 发送缓冲区 L2 批量大小
    /// </summary>
    /// <remarks>
    /// L2 批量发送的消息数量。默认 64。
    /// </remarks>
    public int SendBufferL2BatchSize { get; set; } = 64;

    #endregion

    #region 高级配置（一般无需修改）

    /// <summary>
    /// 重连间隔基数（秒）
    /// </summary>
    public double ReconnectBaseDelay { get; set; } = 2.0;

    /// <summary>
    /// 最大重试次数（用于消息重试）
    /// </summary>
    /// <remarks>
    /// 推荐使用 <see cref="PulseRPC.Client.RetryPresets"/> 配置重试策略。
    /// </remarks>
    public int MaxRetryCount { get; set; } = 3;

    /// <summary>
    /// 重试间隔基数（秒）
    /// </summary>
    public double RetryBaseDelay { get; set; } = 2.0;

    #endregion

    #region 保留配置（未实现功能）

    /// <summary>
    /// 是否启用消息压缩（保留，尚未实现）
    /// </summary>
    [Obsolete("此功能尚未实现，设置无效")]
    public bool EnableMessageCompression { get; set; } = false;

    /// <summary>
    /// 消息压缩阈值（字节）（保留，尚未实现）
    /// </summary>
    [Obsolete("此功能尚未实现，设置无效")]
    public int MessageCompressionThreshold { get; set; } = 1024;

    /// <summary>
    /// 是否启用消息加密（保留，尚未实现）
    /// </summary>
    [Obsolete("此功能尚未实现，设置无效")]
    public bool EnableMessageEncryption { get; set; } = false;

    #endregion
}
