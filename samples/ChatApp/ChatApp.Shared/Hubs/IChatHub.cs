using ChatApp.Shared.Models;
using PulseRPC;
using System.Threading.Tasks;

namespace ChatApp.Shared.Hubs
{
    /// <summary>
    /// 聊天Hub服务接口 - 客户端调用服务端的API（流式）
    /// </summary>
    public interface IChatHub : IService<IChatHub>
    {
        /// <summary>
        /// 加入聊天室
        /// </summary>
        /// <param name="request">加入请求</param>
        /// <returns>是否成功</returns>
        [ServiceMethod(1)]
        Task<bool> JoinAsync(JoinRequest request);

        /// <summary>
        /// 离开聊天室
        /// </summary>
        /// <returns>是否成功</returns>
        [ServiceMethod(2)]
        Task<bool> LeaveAsync();

        /// <summary>
        /// 发送消息
        /// </summary>
        /// <param name="message">消息内容</param>
        /// <returns>是否成功</returns>
        [ServiceMethod(3)]
        Task<bool> SendMessageAsync(string message);

        /// <summary>
        /// 生成异常（测试错误处理）
        /// </summary>
        /// <param name="message">错误消息</param>
        /// <returns>是否成功</returns>
        [ServiceMethod(4)]
        Task<bool> GenerateException(string message);
    }
}
