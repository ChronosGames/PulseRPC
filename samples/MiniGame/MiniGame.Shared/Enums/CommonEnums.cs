namespace PulseRPC.Samples.Shared;

/// <summary>
/// 响应状态枚举
/// </summary>
public enum ResponseStatus
{
    /// <summary>
    /// 成功
    /// </summary>
    Success = 0,

    /// <summary>
    /// 一般错误
    /// </summary>
    Error = 1,

    /// <summary>
    /// 参数无效
    /// </summary>
    InvalidParameter = 2,

    /// <summary>
    /// 认证失败
    /// </summary>
    AuthenticationFailed = 3,

    /// <summary>
    /// 权限不足
    /// </summary>
    PermissionDenied = 4,

    /// <summary>
    /// 资源不存在
    /// </summary>
    NotFound = 5,

    /// <summary>
    /// 资源已存在
    /// </summary>
    AlreadyExists = 6,

    /// <summary>
    /// 服务器内部错误
    /// </summary>
    ServerError = 7,

    /// <summary>
    /// 服务不可用
    /// </summary>
    ServiceUnavailable = 8,

    /// <summary>
    /// 超时
    /// </summary>
    Timeout = 9
}

/// <summary>
/// 通知类型枚举
/// </summary>
public enum NotificationType
{
    /// <summary>
    /// 系统通知
    /// </summary>
    System = 1,

    /// <summary>
    /// 用户通知
    /// </summary>
    User = 2,

    /// <summary>
    /// 活动通知
    /// </summary>
    Activity = 3,

    /// <summary>
    /// 紧急通知
    /// </summary>
    Urgent = 4,

    /// <summary>
    /// 维护通知
    /// </summary>
    Maintenance = 5
}

/// <summary>
/// 用户状态枚举
/// </summary>
public enum UserStatus
{
    /// <summary>
    /// 在线
    /// </summary>
    Online = 1,

    /// <summary>
    /// 离线
    /// </summary>
    Offline = 2,

    /// <summary>
    /// 离开
    /// </summary>
    Away = 3,

    /// <summary>
    /// 忙碌
    /// </summary>
    Busy = 4,

    /// <summary>
    /// 隐身
    /// </summary>
    Invisible = 5,

    /// <summary>
    /// 禁用
    /// </summary>
    Disabled = 6
}

