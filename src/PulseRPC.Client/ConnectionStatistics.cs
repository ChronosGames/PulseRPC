using System;

namespace PulseRPC.Client;

/// <summary>
/// 连接统计信息
/// </summary>
public sealed class ConnectionStatistics
{
    /// <summary>
    /// 连接ID
    /// </summary>
    public string ConnectionId { get; set; } = string.Empty;

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 连接时间
    /// </summary>
    public DateTime? ConnectedAt { get; set; }

    /// <summary>
    /// 最后活动时间
    /// </summary>
    public DateTime LastActiveAt { get; set; }

    /// <summary>
    /// 发送的消息数
    /// </summary>
    public long MessagesSent { get; set; }

    /// <summary>
    /// 接收的消息数
    /// </summary>
    public long MessagesReceived { get; set; }

    /// <summary>
    /// 发送的字节数
    /// </summary>
    public long BytesSent { get; set; }

    /// <summary>
    /// 接收的字节数
    /// </summary>
    public long BytesReceived { get; set; }

    /// <summary>
    /// 错误计数
    /// </summary>
    public long ErrorCount { get; set; }

    /// <summary>
    /// 重连次数
    /// </summary>
    public int ReconnectCount { get; set; }

    /// <summary>
    /// 平均响应时间
    /// </summary>
    public TimeSpan AverageResponseTime { get; set; }

    /// <summary>
    /// 活跃请求数
    /// </summary>
    public int ActiveRequests { get; set; }

    /// <summary>
    /// 最后错误时间
    /// </summary>
    public DateTime? LastErrorAt { get; set; }

    /// <summary>
    /// 最后错误信息
    /// </summary>
    public string? LastError { get; set; }
}
