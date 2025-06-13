using System;

namespace PulseRPC.Client.Channels;

/// <summary>
/// TransportChannel 配置选项
/// </summary>
public class TransportChannelOptions
{
    /// <summary>
    /// 默认超时时间
    /// </summary>
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 最大重试次数
    /// </summary>
    public int MaxRetryCount { get; set; } = 3;

    /// <summary>
    /// 重试间隔基数（秒）
    /// </summary>
    public double RetryBaseDelay { get; set; } = 2.0;

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
    /// 是否启用消息压缩
    /// </summary>
    public bool EnableMessageCompression { get; set; } = false;

    /// <summary>
    /// 消息压缩阈值（字节）
    /// </summary>
    public int MessageCompressionThreshold { get; set; } = 1024;

    /// <summary>
    /// 是否启用消息加密
    /// </summary>
    public bool EnableMessageEncryption { get; set; } = false;

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

    /// <summary>
    /// 重连间隔基数（秒）
    /// </summary>
    public double ReconnectBaseDelay { get; set; } = 2.0;
} 