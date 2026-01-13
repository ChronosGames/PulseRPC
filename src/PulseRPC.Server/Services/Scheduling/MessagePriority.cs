namespace PulseRPC.Server.Services.Scheduling;

/// <summary>
/// 消息优先级枚举
/// </summary>
public enum MessagePriority : byte
{
    /// <summary>关键消息 - 最高优先级</summary>
    Critical = 0,

    /// <summary>高优先级消息</summary>
    High = 1,

    /// <summary>普通优先级消息</summary>
    Normal = 2,

    /// <summary>低优先级消息</summary>
    Low = 3,

    /// <summary>批量处理消息 - 最低优先级</summary>
    Bulk = 4
}
