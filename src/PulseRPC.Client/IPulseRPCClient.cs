using System;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC.Client
{
    /// <summary>
    /// PulseRPC 客户端接口，定义客户端的核心功能
    /// </summary>
    public interface IPulseRPCClient : IDisposable
    {
        /// <summary>
        /// 客户端是否已连接
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// 连接到服务器
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>连接任务</returns>
        Task ConnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 断开与服务器的连接
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>断开连接任务</returns>
        Task DisconnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 发送 RPC 请求
        /// </summary>
        /// <typeparam name="TRequest">请求类型</typeparam>
        /// <typeparam name="TResponse">响应类型</typeparam>
        /// <param name="request">请求对象</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>响应对象</returns>
        Task<TResponse> CallAsync<TRequest, TResponse>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : class
            where TResponse : class;

        /// <summary>
        /// 发送单向请求（不需要响应）
        /// </summary>
        /// <typeparam name="TRequest">请求类型</typeparam>
        /// <param name="request">请求对象</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>发送任务</returns>
        Task SendAsync<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : class;

        /// <summary>
        /// 连接状态变化事件
        /// </summary>
        event PulseRPC.EventHandler<ConnectionStateChangedEventArgs> ConnectionStateChanged;

        /// <summary>
        /// 错误发生事件
        /// </summary>
        event PulseRPC.EventHandler<ErrorEventArgs> ErrorOccurred;
    }

    /// <summary>
    /// 连接状态变化事件参数
    /// </summary>
    public class ConnectionStateChangedEventArgs : EventArgs, PulseRPC.IEventData
    {
        public bool IsConnected { get; }
        public string? DisconnectReason { get; }

        public ConnectionStateChangedEventArgs(bool isConnected, string? disconnectReason = null)
        {
            IsConnected = isConnected;
            DisconnectReason = disconnectReason;
        }
    }

    /// <summary>
    /// 错误事件参数
    /// </summary>
    public class ErrorEventArgs : EventArgs, PulseRPC.IEventData
    {
        public Exception Exception { get; }
        public string? Context { get; }

        public ErrorEventArgs(Exception exception, string? context = null)
        {
            Exception = exception;
            Context = context;
        }
    }
}
