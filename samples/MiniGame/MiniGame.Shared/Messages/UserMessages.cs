using MemoryPack;
using PulseRPC.Protocol;
using PulseRPC.Protocol.Attributes;
using System;

namespace PulseRPC.Samples.Shared.Messages
{
    /// <summary>
    /// 获取用户信息请求
    /// </summary>
    [MemoryPackable]
    [Message(1101, MessageType.Request)]
    public partial class GetUserInfoRequest : IMessage
    {
        /// <summary>
        /// 用户ID
        /// </summary>
        [MemoryPackOrder(0)]
        public int UserId { get; set; }
    }

    /// <summary>
    /// 获取用户信息响应
    /// </summary>
    [MemoryPackable]
    [Message(1102, MessageType.Response)]
    public partial class GetUserInfoResponse : IMessage
    {
        /// <summary>
        /// 响应状态
        /// </summary>
        [MemoryPackOrder(0)]
        public ResponseStatus Status { get; set; }

        /// <summary>
        /// 错误信息
        /// </summary>
        [MemoryPackOrder(1)]
        public string ErrorMessage { get; set; }

        /// <summary>
        /// 用户ID
        /// </summary>
        [MemoryPackOrder(2)]
        public int UserId { get; set; }

        /// <summary>
        /// 用户名
        /// </summary>
        [MemoryPackOrder(3)]
        public string Username { get; set; }

        /// <summary>
        /// 用户昵称
        /// </summary>
        [MemoryPackOrder(4)]
        public string Nickname { get; set; }

        /// <summary>
        /// 用户头像URL
        /// </summary>
        [MemoryPackOrder(5)]
        public string AvatarUrl { get; set; }

        /// <summary>
        /// 用户状态
        /// </summary>
        [MemoryPackOrder(6)]
        public UserStatus Status { get; set; }

        /// <summary>
        /// 注册时间
        /// </summary>
        [MemoryPackOrder(7)]
        public DateTime RegisterTime { get; set; }

        /// <summary>
        /// 最后登录时间
        /// </summary>
        [MemoryPackOrder(8)]
        public DateTime LastLoginTime { get; set; }
    }

    /// <summary>
    /// 更新用户信息请求
    /// </summary>
    [MemoryPackable]
    [Message(1103, MessageType.Request)]
    public partial class UpdateUserInfoRequest : IMessage
    {
        /// <summary>
        /// 用户ID
        /// </summary>
        [MemoryPackOrder(0)]
        public int UserId { get; set; }

        /// <summary>
        /// 用户昵称
        /// </summary>
        [MemoryPackOrder(1)]
        public string Nickname { get; set; }

        /// <summary>
        /// 用户头像URL
        /// </summary>
        [MemoryPackOrder(2)]
        public string AvatarUrl { get; set; }
    }

    /// <summary>
    /// 更新用户信息响应
    /// </summary>
    [MemoryPackable]
    [Message(1104, MessageType.Response)]
    public partial class UpdateUserInfoResponse : IMessage
    {
        /// <summary>
        /// 响应状态
        /// </summary>
        [MemoryPackOrder(0)]
        public ResponseStatus Status { get; set; }

        /// <summary>
        /// 错误信息
        /// </summary>
        [MemoryPackOrder(1)]
        public string ErrorMessage { get; set; }

        /// <summary>
        /// 更新成功的字段数量
        /// </summary>
        [MemoryPackOrder(2)]
        public int UpdatedCount { get; set; }
    }
}
