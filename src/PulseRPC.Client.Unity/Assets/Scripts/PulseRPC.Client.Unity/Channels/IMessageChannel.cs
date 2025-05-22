using System;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC.Client.Channels
{
    /// <summary>
    /// 消息通道接口
    /// </summary>
    public interface IMessageChannel
    {
        /// <summary>
        /// 通道名称
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 通道是否已连接
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// 发送请求并等待响应
        /// </summary>
        /// <param name="serviceName">服务名称</param>
        /// <param name="methodName">方法名称</param>
        /// <param name="requestData">请求数据</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>响应数据</returns>
        Task<byte[]> SendRequestAsync(
            string serviceName,
            string methodName,
            byte[] requestData,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 发送通知消息（不需要响应）
        /// </summary>
        /// <param name="eventName">事件名称</param>
        /// <param name="eventData">事件数据</param>
        /// <param name="cancellationToken">取消令牌</param>
        Task SendNotificationAsync(
            string eventName,
            byte[] eventData,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 订阅事件
        /// </summary>
        /// <typeparam name="TEvent">事件类型</typeparam>
        /// <param name="eventName">事件名称</param>
        /// <param name="handler">事件处理器</param>
        /// <returns>订阅令牌</returns>
        ISubscriptionToken SubscribeToEvent<TEvent>(
            string eventName,
            Action<object, TEvent> handler);

        /// <summary>
        /// 连接状态变化事件
        /// </summary>
        event Action<bool> ConnectionStateChanged;
    }

    /// <summary>
    /// 具有传输功能的通道接口
    /// </summary>
    public interface IHasTransport
    {
        /// <summary>
        /// 连接到服务器
        /// </summary>
        /// <param name="host">主机地址</param>
        /// <param name="port">端口</param>
        /// <param name="cancellationToken">取消令牌</param>
        Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default);

        /// <summary>
        /// 断开连接
        /// </summary>
        void Disconnect();
    }

    /// <summary>
    /// 订阅令牌接口
    /// </summary>
    public interface ISubscriptionToken : IDisposable
    {
        /// <summary>
        /// 令牌标识
        /// </summary>
        Guid Id { get; }

        /// <summary>
        /// 是否处于活动状态
        /// </summary>
        bool IsActive { get; }

        /// <summary>
        /// 取消订阅
        /// </summary>
        void Unsubscribe();
    }
}
