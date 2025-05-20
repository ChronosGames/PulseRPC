namespace PulseRPC.Client.Unity
{
    /// <summary>
    /// 事件处理器接口
    /// </summary>
    public interface IEventHandler<TSubscriber> where TSubscriber : class, IEventSubscriber
    {
        /// <summary>
        /// 订阅事件
        /// </summary>
        ISubscriptionToken Subscribe(TSubscriber subscriber);

        /// <summary>
        /// 取消订阅
        /// </summary>
        void Unsubscribe(TSubscriber subscriber);
    }
}
