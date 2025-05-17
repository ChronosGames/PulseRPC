using System.Threading.Tasks;
using PulseRPC;

namespace ChatApp.Shared.Services
{
    /// <summary>
    /// 聊天服务接口 - 客户端调用服务端的API
    /// </summary>
    public interface IChatService : IService<IChatService>
    {
        /// <summary>
        /// 生成异常（测试错误处理）
        /// </summary>
        /// <param name="message">错误消息</param>
        /// <returns>是否成功</returns>
        [ServiceMethod(1)]
        Task<bool> GenerateException(string message);

        /// <summary>
        /// 发送报告
        /// </summary>
        /// <param name="message">报告消息</param>
        /// <returns>是否成功</returns>
        [ServiceMethod(2)]
        Task<bool> SendReportAsync(string message);
    }
}
