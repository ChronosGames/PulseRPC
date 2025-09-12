using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC.Transport;

/// <summary>
/// 传输连接基础接口 - 表示一个底层的网络连接
/// 这是三层抽象架构中的传输层(Transport Layer)基础抽象
/// </summary>
public interface ITransportConnection : IDisposable
{
    /// <summary>
    /// 连接唯一标识符
    /// </summary>
    string ConnectionId { get; }

    /// <summary>
    /// 当前连接状态
    /// </summary>
    ConnectionState State { get; }

    /// <summary>
    /// 远程端点地址
    /// </summary>
    EndPoint RemoteEndPoint { get; }

    /// <summary>
    /// 本地端点地址
    /// </summary>
    EndPoint LocalEndPoint { get; }

    /// <summary>
    /// 连接建立时间
    /// </summary>
    DateTime ConnectedAt { get; }

    /// <summary>
    /// 最后活动时间
    /// </summary>
    DateTime LastActivityAt { get; }

    /// <summary>
    /// 传输类型
    /// </summary>
    TransportType TransportType { get; }

    /// <summary>
    /// 是否已连接
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// 发送数据到远程端点
    /// </summary>
    /// <param name="data">要发送的数据</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否发送成功</returns>
    Task<bool> SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

    /// <summary>
    /// 关闭连接
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>关闭任务</returns>
    Task CloseAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 连接状态变化事件
    /// </summary>
    event EventHandler<ConnectionStateChangedEventArgs> StateChanged;

    /// <summary>
    /// 数据接收事件
    /// </summary>
    event EventHandler<TransportDataEventArgs> DataReceived;
}

/// <summary>
/// 连接状态变化事件参数
/// </summary>
public class ConnectionStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// 连接ID
    /// </summary>
    public string ConnectionId { get; }

    /// <summary>
    /// 旧状态
    /// </summary>
    public ConnectionState PreviousState { get; }

    /// <summary>
    /// 新状态
    /// </summary>
    public ConnectionState CurrentState { get; }

    /// <summary>
    /// 状态变更原因
    /// </summary>
    public string? Reason { get; }

    /// <summary>
    /// 关联异常
    /// </summary>
    public Exception? Exception { get; }

    /// <summary>
    /// 状态变更时间
    /// </summary>
    public DateTime ChangedAt { get; }

    public ConnectionStateChangedEventArgs(
        string connectionId,
        ConnectionState previousState,
        ConnectionState currentState,
        string? reason = null,
        Exception? exception = null)
    {
        ConnectionId = connectionId;
        PreviousState = previousState;
        CurrentState = currentState;
        Reason = reason;
        Exception = exception;
        ChangedAt = DateTime.UtcNow;
    }
}