using PulseRPC;
using System.Threading.Tasks;
using MemoryPack;

namespace ChatApp
{
    /// <summary>
    /// 聊天Hub服务接口 - 客户端调用服务端的API（流式）
    /// </summary>
    /// <remarks>
    /// <para><strong>服务隔离架构说明</strong>:</para>
    /// <list type="bullet">
    /// <item><description>每个聊天室（RoomId）对应一个独立的 ChatRoomService 实例</description></item>
    /// <item><description>ServiceSchedulingKey = { ServiceName: "ChatRoom", ServiceId: "ChatRoom:{RoomId}" }</description></item>
    /// <item><description>相同房间的所有消息在同一线程顺序处理（无需加锁）</description></item>
    /// <item><description>不同房间的消息可并发处理（不同线程）</description></item>
    /// <item><description>单个房间故障不影响其他房间</description></item>
    /// </list>
    /// <para><strong>客户端使用示例</strong>:</para>
    /// <code>
    /// // 1. 加入房间（服务端会根据 RoomName 创建或获取对应的 ChatRoomService 实例）
    /// await chatHub.JoinAsync(new JoinRequest { RoomName = "room-123", UserName = "Alice" });
    ///
    /// // 2. 发送消息（消息会路由到对应房间的服务实例，顺序处理）
    /// await chatHub.SendMessageAsync("Hello, World!");
    ///
    /// // 3. 离开房间
    /// await chatHub.LeaveAsync();
    /// </code>
    /// </remarks>
    [Channel("TcpChannel")]
    [PulseHub(Provider = "GameServer")]
    public interface IChatHub : IPulseHub
    {
        /// <summary>
        /// 加入聊天室
        /// </summary>
        /// <param name="request">加入请求（包含房间名称和用户名）</param>
        /// <returns>是否成功</returns>
        /// <remarks>
        /// 服务端会根据 RoomName 创建或获取对应的 ChatRoomService 实例。
        /// 该实例的 ServiceId 为 "ChatRoom:{RoomName}"，确保相同房间的所有请求路由到同一线程。
        /// </remarks>
        Task<bool> JoinAsync(JoinRequest request);

        /// <summary>
        /// 离开聊天室
        /// </summary>
        /// <returns>是否成功</returns>
        /// <remarks>
        /// 需要先调用 JoinAsync 加入房间。服务端会从认证上下文中获取当前用户和房间信息。
        /// </remarks>
        Task<bool> LeaveAsync();

        /// <summary>
        /// 发送消息
        /// </summary>
        /// <param name="message">消息内容</param>
        /// <returns>是否成功</returns>
        /// <remarks>
        /// 需要先调用 JoinAsync 加入房间。消息会在对应房间的服务实例中顺序处理。
        /// 需要 "chat.send" 权限（通过 [RequirePermission] 特性验证）。
        /// </remarks>
        Task<bool> SendMessageAsync(string message);

        /// <summary>
        /// 生成异常（测试错误处理）
        /// </summary>
        /// <param name="message">错误消息</param>
        /// <returns>是否成功</returns>
        /// <remarks>
        /// 用于测试服务隔离架构的异常处理机制。单个房间的异常不会影响其他房间。
        /// </remarks>
        Task<bool> GenerateException(string message);
    }

    /// <summary>
    /// 聊天Hub接收器接口 - 服务端调用客户端的API
    /// </summary>
    [Channel("TcpChannel")]
    [PulseHub(Provider = "UnityClient")]
    public interface IChatHubReceiver : IPulseReceiver
    {
        /// <summary>
        /// 有用户加入时触发
        /// </summary>
        /// <param name="name">用户名</param>
        void OnJoin(string name);

        /// <summary>
        /// 有用户离开时触发
        /// </summary>
        /// <param name="name">用户名</param>
        void OnLeave(string name);

        /// <summary>
        /// 收到新消息时触发
        /// </summary>
        /// <param name="message">消息内容</param>
        void OnSendMessage(MessageResponse message);

        /// <summary>
        /// 测试带返回值的接收器方法
        /// </summary>
        /// <param name="name">用户名</param>
        /// <param name="age">年龄</param>
        /// <returns>格式化后的字符串</returns>
        Task<string> HelloAsync(string name, int age);
    }

    /// <summary>
    /// 加入聊天室请求
    /// </summary>
    /// <remarks>
    /// <para><strong>服务隔离路由说明</strong>:</para>
    /// <list type="bullet">
    /// <item><description>RoomName 用于确定 ServiceId（格式: "ChatRoom:{RoomName}"）</description></item>
    /// <item><description>服务端会根据 RoomName 创建或获取对应的 ChatRoomService 实例</description></item>
    /// <item><description>相同 RoomName 的所有请求会路由到同一个服务实例（同一线程）</description></item>
    /// <item><description>UserName 用于标识加入房间的用户</description></item>
    /// </list>
    /// </remarks>
    [MemoryPackable]
    public partial struct JoinRequest
    {
        /// <summary>
        /// 房间名称（用于服务实例路由）
        /// </summary>
        /// <remarks>
        /// 该字段决定了消息的 ServiceSchedulingKey.ServiceId。
        /// 例如: RoomName = "lobby" → ServiceId = "ChatRoom:lobby"
        /// </remarks>
        [MemoryPackOrder(0)]
        public string RoomName { get; set; }

        /// <summary>
        /// 用户名称
        /// </summary>
        [MemoryPackOrder(1)]
        public string UserName { get; set; }
    }

    /// <summary>
    /// Message information
    /// </summary>
    [MemoryPackable]
    public partial struct MessageResponse
    {
        [MemoryPackOrder(0)]
        public string UserName { get; set; }

        [MemoryPackOrder(1)]
        public string Message { get; set; }
    }
}
