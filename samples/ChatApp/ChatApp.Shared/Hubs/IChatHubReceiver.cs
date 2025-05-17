using System.Threading.Tasks;
using ChatApp.Shared.Models;
using PulseRPC;

namespace ChatApp.Shared.Hubs
{
    /// <summary>
    /// 聊天Hub接收器接口 - 服务端调用客户端的API
    /// </summary>
    public interface IChatHubReceiver : IReceiver<IChatHubReceiver>
    {
        /// <summary>
        /// 有用户加入时触发
        /// </summary>
        /// <param name="name">用户名</param>
        [ReceiverMethod(1)]
        void OnJoin(string name);

        /// <summary>
        /// 有用户离开时触发
        /// </summary>
        /// <param name="name">用户名</param>
        [ReceiverMethod(2)]
        void OnLeave(string name);

        /// <summary>
        /// 收到新消息时触发
        /// </summary>
        /// <param name="message">消息内容</param>
        [ReceiverMethod(3)]
        void OnSendMessage(MessageResponse message);

        /// <summary>
        /// 测试带返回值的接收器方法
        /// </summary>
        /// <param name="name">用户名</param>
        /// <param name="age">年龄</param>
        /// <returns>格式化后的字符串</returns>
        [ReceiverMethod(4)]
        Task<string> HelloAsync(string name, int age);
    }
}
