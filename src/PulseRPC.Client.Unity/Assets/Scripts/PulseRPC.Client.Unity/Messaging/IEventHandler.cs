using System;
using PulseRPC.Client.Channels;

namespace PulseRPC.Messaging
{
    /// <summary>
    /// 事件处理器接口
    /// </summary>
    public interface IEventHandler
    {
        /// <summary>
        /// 订阅事件处理程序
        /// </summary>
        /// <param name="handler">事件处理程序</param>
        /// <returns>订阅令牌</returns>
        ISubscriptionToken Subscribe(object handler);

        /// <summary>
        /// 取消订阅
        /// </summary>
        /// <param name="token">订阅令牌</param>
        void Unsubscribe(ISubscriptionToken token);
    }

    /// <summary>
    /// 特定类型的事件处理器接口
    /// </summary>
    /// <typeparam name="TEvents">事件接口类型</typeparam>
    public interface IEventHandler<TEvents> : IEventHandler where TEvents : class
    {
        /// <summary>
        /// 订阅事件处理程序
        /// </summary>
        /// <param name="handler">事件处理程序</param>
        /// <returns>订阅令牌</returns>
        ISubscriptionToken Subscribe(TEvents handler);
    }
}
