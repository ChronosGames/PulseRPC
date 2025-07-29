using PulseRPC;
using System.Threading.Tasks;
using MemoryPack;

namespace ChatApp
{
    /// <summary>
    /// 聊天Hub服务接口 - 客户端调用服务端的API（流式）
    /// </summary>
    [Channel("TcpChannel")]
    public interface IChatHub : IPulseService
    {
        /// <summary>
        /// 加入聊天室
        /// </summary>
        /// <param name="request">加入请求</param>
        /// <returns>是否成功</returns>
        Task<bool> JoinAsync(JoinRequest request);

        /// <summary>
        /// 离开聊天室
        /// </summary>
        /// <returns>是否成功</returns>
        Task<bool> LeaveAsync();

        /// <summary>
        /// 发送消息
        /// </summary>
        /// <param name="message">消息内容</param>
        /// <returns>是否成功</returns>
        Task<bool> SendMessageAsync(string message);

        /// <summary>
        /// 生成异常（测试错误处理）
        /// </summary>
        /// <param name="message">错误消息</param>
        /// <returns>是否成功</returns>
        Task<bool> GenerateException(string message);
    }

    /// <summary>
    /// 聊天Hub接收器接口 - 服务端调用客户端的API
    /// </summary>
    [Channel("TcpChannel")]
    public interface IChatHubReceiver : IPulseEventHandler
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
    /// Room participation information
    /// </summary>
    [MemoryPackable]
    public partial struct JoinRequest
    {
        [MemoryPackOrder(0)]
        public string RoomName { get; set; }

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
