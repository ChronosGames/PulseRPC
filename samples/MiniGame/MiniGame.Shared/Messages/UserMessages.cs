using MemoryPack;
using PulseRPC.Protocol;
using PulseRPC.Protocol.Attributes;
using System;

namespace PulseRPC.Samples.Shared.Messages;

/// <summary>
/// 获取用户信息请求
/// </summary>
[MemoryPackable, Message(1101, MessageType.Request)]
public partial class GetUserInfoRequest : IMessage
{
    /// <summary>
    /// 用户ID
    /// </summary>
    public int UserId { get; set; }
}

/// <summary>
/// 获取用户信息响应
/// </summary>
[MemoryPackable, Message(1102, MessageType.Response)]
public partial class GetUserInfoResponse : IMessage
{
    /// <summary>
    /// 响应状态
    /// </summary>
    public ResponseStatus Status { get; set; }

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

    /// <summary>
    /// 用户昵称
    /// </summary>
    public string Nickname { get; set; } = string.Empty;

    /// <summary>
    /// 用户头像URL
    /// </summary>
    public string AvatarUrl { get; set; } = string.Empty;

    /// <summary>
    /// 用户状态
    /// </summary>
    public UserStatus UserStatus { get; set; }

    /// <summary>
    /// 注册时间
    /// </summary>
    public DateTime RegisterTime { get; set; }

    /// <summary>
    /// 最后登录时间
    /// </summary>
    public DateTime LastLoginTime { get; set; }
}

/// <summary>
/// 更新用户信息请求
/// </summary>
[MemoryPackable, Message(1103, MessageType.Request)]
public partial class UpdateUserInfoRequest : IMessage
{
    /// <summary>
    /// 用户ID
    /// </summary>
    public int UserId { get; set; }

    /// <summary>
    /// 用户昵称
    /// </summary>
    public string Nickname { get; set; } = string.Empty;

    /// <summary>
    /// 用户头像URL
    /// </summary>
    public string AvatarUrl { get; set; } = string.Empty;
}

/// <summary>
/// 更新用户信息响应
/// </summary>
[MemoryPackable, Message(1104, MessageType.Response)]
public partial class UpdateUserInfoResponse : IMessage
{
    /// <summary>
    /// 响应状态
    /// </summary>
    public ResponseStatus Status { get; set; }

    /// <summary>
    /// 错误信息
    /// </summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>
    /// 更新成功的字段数量
    /// </summary>
    public int UpdatedCount { get; set; }
}
