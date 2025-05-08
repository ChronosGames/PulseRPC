using MemoryPack;
using PulseRPC.Protocol;
using PulseRPC.Protocol.Attributes;

namespace PulseRPC.Samples.Shared.Messages;

/// <summary>
/// 登录请求消息
/// </summary>
[MemoryPackable, Message(1001, MessageType.Request)]
public partial class LoginRequest : IMessage
{
    /// <summary>
    /// 请求ID
    /// </summary>
    public string RequestId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// 用户名
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// 密码
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// 客户端版本
    /// </summary>
    public int ClientVersion { get; set; }
}

/// <summary>
/// 登录响应消息
/// </summary>
[MemoryPackable, Message(1002, MessageType.Response)]
public partial class LoginResponse : IMessage
{
    /// <summary>
    /// 请求ID
    /// </summary>
    public string RequestId { get; set; } = string.Empty;

    /// <summary>
    /// 是否成功
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 错误代码
    /// </summary>
    public int ErrorCode { get; set; }

    /// <summary>
    /// 错误信息
    /// </summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>
    /// 登录令牌
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// 用户ID
    /// </summary>
    public int UserId { get; set; }

    /// <summary>
    /// 用户名
    /// </summary>
    public string Username { get; set; } = string.Empty;
}

/// <summary>
/// 注册请求消息
/// </summary>
[MemoryPackable, Message(1003, MessageType.Request)]
public partial class RegisterRequest : IMessage
{
    /// <summary>
    /// 请求ID
    /// </summary>
    public string RequestId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// 用户名
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// 密码
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// 邮箱
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// 手机号
    /// </summary>
    public string PhoneNumber { get; set; } = string.Empty;
}

/// <summary>
/// 注册响应消息
/// </summary>
[MemoryPackable, Message(1004, MessageType.Response)]
public partial class RegisterResponse : IMessage
{
    /// <summary>
    /// 请求ID
    /// </summary>
    public string RequestId { get; set; } = string.Empty;

    /// <summary>
    /// 是否成功
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 错误代码
    /// </summary>
    public int ErrorCode { get; set; }

    /// <summary>
    /// 错误信息
    /// </summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>
    /// 用户ID
    /// </summary>
    public int UserId { get; set; }

    /// <summary>
    /// 用户名
    /// </summary>
    public string Username { get; set; } = string.Empty;
}
