namespace PulseRPC.Protocol;

/// <summary>
/// 消息类型枚举
/// </summary>
public enum MessageType
{
    /// <summary>
    /// 请求消息
    /// </summary>
    Request = 1,

    /// <summary>
    /// 响应消息
    /// </summary>
    Response = 2,

    /// <summary>
    /// 通知消息
    /// </summary>
    Notification = 3,

    /// <summary>
    /// 广播消息
    /// </summary>
    Broadcast = 4,

    /// <summary>
    /// 内部消息
    /// </summary>
    Internal = 5,

    /// <summary>
    /// 特殊消息
    /// </summary>
    Special = 6
}
