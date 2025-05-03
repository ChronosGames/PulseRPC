using System.Threading.Tasks;
using PulseRPC.Protocol.Network;
using PulseRPC.Protocol.Serialization;

namespace PulseRPC.Protocol.Handlers
{
    /// <summary>
    /// 请求处理器基类
    /// </summary>
    /// <typeparam name="TRequest">请求消息类型</typeparam>
    /// <typeparam name="TResponse">响应消息类型</typeparam>
    public abstract class RequestHandlerBase<TRequest, TResponse> : IMessageHandler<TRequest>
        where TRequest : IMessage
        where TResponse : class, IMessage
    {
        /// <summary>
        /// 处理请求消息
        /// </summary>
        /// <param name="context">会话上下文</param>
        /// <param name="request">请求消息</param>
        public async Task HandleAsync(SessionContext context, TRequest request)
        {
            // 处理请求
            var response = await ProcessRequestAsync(context, request);

            // 发送响应
            await SendResponseAsync(context, response);
        }

        /// <summary>
        /// 处理请求并生成响应的抽象方法
        /// </summary>
        /// <param name="context">会话上下文</param>
        /// <param name="request">请求消息</param>
        /// <returns>响应消息</returns>
        protected abstract Task<TResponse> ProcessRequestAsync(SessionContext context, TRequest request);

        /// <summary>
        /// 发送响应
        /// </summary>
        /// <param name="context">会话上下文</param>
        /// <param name="response">响应消息</param>
        private static async Task SendResponseAsync(SessionContext context, TResponse response)
        {
            var messageId = MessageRegistry.GetMessageId<TResponse>();
            var data = MessageSerializer.Serialize(response);

            await context.SendAsync(messageId, data);
        }
    }
}
