using MemoryPack;
using PulseRPC.Protocol.Attributes;

namespace PulseRPC.Protocol.Messages
{
    /// <summary>
    /// 登录响应消息
    /// </summary>
    [MemoryPackable]
    [Message(1002, MessageType.Response)]
    public partial class LoginResponse : IMessage
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
        public string ErrorMessage { get; set; } = string.Empty;

        /// <summary>
        /// 登录令牌
        /// </summary>
        [MemoryPackOrder(2)]
        public string Token { get; set; } = string.Empty;

        /// <summary>
        /// 玩家ID
        /// </summary>
        [MemoryPackOrder(3)]
        public int PlayerId { get; set; }
    }

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
        /// 失败
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
        PermissionDenied = 4
    }
}
