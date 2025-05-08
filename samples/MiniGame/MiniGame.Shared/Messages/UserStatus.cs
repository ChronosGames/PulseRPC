namespace PulseRPC.Samples.Shared.Messages;

/// <summary>
/// 用户状态
/// </summary>
public enum UserStatus
{
    /// <summary>
    /// 离线
    /// </summary>
    Offline = 0,

    /// <summary>
    /// 在线
    /// </summary>
    Online = 1,

    /// <summary>
    /// 忙碌
    /// </summary>
    Busy = 2,

    /// <summary>
    /// 离开
    /// </summary>
    Away = 3,

    /// <summary>
    /// 隐身
    /// </summary>
    Invisible = 4
}
