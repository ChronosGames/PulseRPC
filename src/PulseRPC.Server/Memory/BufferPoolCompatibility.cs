using PulseRPC.Server.Scheduling;

namespace PulseRPC.Server.Memory;

/// <summary>
/// 消息状态枚举
/// 注意: MessageEnvelope 已移至 HighPerformanceMessageEngine.cs 中定义
/// </summary>
public enum MessageStatus : byte
{
    /// <summary>
    /// 等待处理
    /// </summary>
    Pending = 0,

    /// <summary>
    /// 正在处理
    /// </summary>
    Processing = 1,

    /// <summary>
    /// 处理完成
    /// </summary>
    Completed = 2,

    /// <summary>
    /// 处理失败
    /// </summary>
    Failed = 3,

    /// <summary>
    /// 关键消息
    /// </summary>
    Critical = 4
}

