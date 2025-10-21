using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace PulseRPC.Transport;

/// <summary>
/// 通用传输接口
/// </summary>
public interface ITransport : IDisposable
{
    /// <summary>
    /// Connection唯一标识
    /// </summary>
    string Id { get; }

    /// <summary>
    /// 传输类型
    /// </summary>
    TransportType Type { get; }

    /// <summary>
    /// 是否已连接
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// 获取当前连接状态
    /// </summary>
    ConnectionState State { get; }

    /// <summary>
    /// 本地端点
    /// </summary>
    EndPoint LocalEndPoint { get; }

    /// <summary>
    /// 远程端点
    /// </summary>
    EndPoint RemoteEndPoint { get; }

    /// <summary>
    /// 状态变更事件
    /// </summary>
    event EventHandler<TransportStateEventArgs>? StateChanged;

    /// <summary>
    /// 数据接收事件
    /// </summary>
    event EventHandler<TransportDataEventArgs>? DataReceived;

    /// <summary>
    /// 发送数据
    /// </summary>
    Task<bool> SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);
}

/// <summary>
/// 客户端传输层接口 - 底层网络传输抽象
/// 实现思路：
/// - 协议抽象：支持TCP、KCP、WebSocket等多种协议
/// - 异步IO：使用异步IO提高性能
/// - 连接复用：支持连接复用和管道化
/// - 流量控制：实现发送和接收的流量控制
/// - 错误处理：统一的错误处理和重连机制
/// </summary>
public interface IClientTransport : ITransport
{
    /// <summary>
    /// 连接到远程主机
    /// </summary>
    Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default);

    /// <summary>
    /// 断开连接
    /// </summary>
    Task DisconnectAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 服务端连接接口
/// 继承ITransportConnection提供统一的连接抽象
/// </summary>
public interface IServerTransport : ITransport
{
    Task CloseAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 服务端监听器接口
/// </summary>
public interface IServerListener : IDisposable
{
    /// <summary>
    /// 监听器名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 传输类型
    /// </summary>
    TransportType Type { get; }

    /// <summary>
    /// 本地端点
    /// </summary>
    EndPoint LocalEndPoint { get; }

    /// <summary>
    /// 是否正在监听
    /// </summary>
    bool IsListening { get; }

    /// <summary>
    /// 客户端连接事件
    /// </summary>
    event EventHandler<ServerConnectionEventArgs> ConnectionAccepted;

    /// <summary>
    /// 启动监听
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止监听
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 传输类型枚举
/// </summary>
public enum TransportType
{
    /// <summary>
    /// TCP传输
    /// </summary>
    TCP,

    /// <summary>
    /// KCP传输
    /// </summary>
    KCP,
}

/// <summary>
/// 连接状态枚举
/// </summary>
public enum ConnectionState
{
    /// <summary>
    /// 未连接
    /// </summary>
    Disconnected,

    /// <summary>
    /// 连接中
    /// </summary>
    Connecting,

    /// <summary>
    /// 已连接
    /// </summary>
    Connected,

    /// <summary>
    /// 断开连接中
    /// </summary>
    Disconnecting,

    /// <summary>
    /// 连接失败
    /// </summary>
    Failed,

    /// <summary>
    /// 重连中
    /// </summary>
    Reconnecting
}

/// <summary>
/// 传输状态事件参数
/// </summary>
public class TransportStateEventArgs : EventArgs
{
    /// <summary>
    /// 连接唯一标识
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

    public TransportStateEventArgs(
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
    }
}

/// <summary>
/// 传输数据事件参数
/// </summary>
public class TransportDataEventArgs : EventArgs
{
    /// <summary>
    /// 接收到的数据
    /// </summary>
    public ReadOnlyMemory<byte> Data { get; }

    public TransportDataEventArgs(ReadOnlyMemory<byte> data)
    {
        Data = data;
    }
}

/// <summary>
/// 服务端连接事件参数
/// </summary>
public class ServerConnectionEventArgs : EventArgs
{
    /// <summary>
    /// 客户端连接
    /// </summary>
    public IServerTransport Transport { get; }

    public ServerConnectionEventArgs(IServerTransport transport)
    {
        Transport = transport;
    }
}

