using System;
using System.Collections.Generic;
using PulseRPC.Authentication;

namespace PulseRPC.Transport
{
    /// <summary>
    /// 传输通道接口，管理连接的生命周期和认证状态
    /// </summary>
    public interface ITransportChannel : IDisposable
    {
        /// <summary>
        /// 连接ID，唯一标识一个连接
        /// </summary>
        string ConnectionId { get; }

        /// <summary>
        /// 底层传输连接
        /// </summary>
        IServerTransport Transport { get; }

        /// <summary>
        /// 认证上下文，包含用户或服务的认证信息
        /// </summary>
        IAuthenticationContext? AuthenticationContext { get; set; }

        /// <summary>
        /// 是否已认证
        /// </summary>
        bool IsAuthenticated => AuthenticationContext?.IsAuthenticated ?? false;

        /// <summary>
        /// 通道属性字典，用于存储自定义数据
        /// </summary>
        IDictionary<string, object> Properties { get; }

        /// <summary>
        /// 远程终结点地址
        /// </summary>
        string RemoteAddress { get; }

        /// <summary>
        /// 连接建立时间
        /// </summary>
        DateTime ConnectedTime { get; }

        /// <summary>
        /// 最后活动时间
        /// </summary>
        DateTime LastActiveTime { get; set; }

        /// <summary>
        /// 设置认证信息
        /// </summary>
        /// <param name="authContext">认证上下文</param>
        void SetAuthentication(IAuthenticationContext authContext);

        /// <summary>
        /// 清除认证信息
        /// </summary>
        void ClearAuthentication();

        /// <summary>
        /// 发送数据
        /// </summary>
        /// <param name="data">要发送的数据</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>是否发送成功</returns>
        Task<bool> SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

        /// <summary>
        /// 关闭通道
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        Task CloseAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 通道状态变更事件
        /// </summary>
        event System.EventHandler<TransportStateEventArgs>? StateChanged;

        /// <summary>
        /// 数据接收事件
        /// </summary>
        event System.EventHandler<TransportDataEventArgs>? DataReceived;
    }
}
