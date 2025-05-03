using System.Threading.Tasks;
using PulseRPC.Protocol.Network;

namespace PulseRPC.Protocol.Handlers
{
    /// <summary>
    /// 消息处理器接口
    /// </summary>
    /// <typeparam name="TMessage">消息类型</typeparam>
    public interface IMessageHandler<TMessage> where TMessage : IMessage
    {
        /// <summary>
        /// 处理消息
        /// </summary>
        /// <param name="context">会话上下文</param>
        /// <param name="message">消息实例</param>
        Task HandleAsync(SessionContext context, TMessage message);
    }
}
