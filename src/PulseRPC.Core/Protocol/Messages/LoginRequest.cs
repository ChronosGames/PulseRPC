using MemoryPack;
using PulseRPC.Protocol.Attributes;
using PulseRPC.Protocol.Handlers;

namespace PulseRPC.Protocol.Messages
{
    /// <summary>
    /// 登录请求消息
    /// </summary>
    [MemoryPackable]
    [Message(1001, MessageType.Request)]
    [Handler(typeof(LoginRequestHandler))]
    public partial class LoginRequest : IMessage
    {
        /// <summary>
        /// 用户名
        /// </summary>
        [MemoryPackOrder(0)]
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// 密码
        /// </summary>
        [MemoryPackOrder(1)]
        public string Password { get; set; } = string.Empty;

        /// <summary>
        /// 客户端版本
        /// </summary>
        [MemoryPackOrder(2)]
        public int ClientVersion { get; set; }

        /// <summary>
        /// 是否有效 (不参与序列化)
        /// </summary>
        [MemoryPackIgnore]
        public bool IsValid => !string.IsNullOrEmpty(Username) && !string.IsNullOrEmpty(Password);
    }
}
