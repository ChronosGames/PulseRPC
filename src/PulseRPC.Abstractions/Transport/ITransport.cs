// PulseRPC.Transport/ITransport.cs

using System.Net;

namespace PulseRPC.Transport
{
    /// <summary>
    /// 通用传输接口
    /// </summary>
    public interface ITransport : IDisposable
    {
        /// <summary>
        /// 传输名称
        /// </summary>
        string Name { get; }

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
        event System.EventHandler<TransportStateEventArgs>? StateChanged;

        /// <summary>
        /// 数据接收事件
        /// </summary>
        event System.EventHandler<TransportDataEventArgs>? DataReceived;

        /// <summary>
        /// 发送数据
        /// </summary>
        Task<bool> SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// 客户端传输接口
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
    /// </summary>
    public interface IServerConnection : ITransport
    {
        /// <summary>
        /// 连接ID
        /// </summary>
        string ConnectionId { get; }

        /// <summary>
        /// 关闭连接
        /// </summary>
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
        event System.EventHandler<ServerConnectionEventArgs> ConnectionAccepted;

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
    /// 传输类型
    /// </summary>
    public enum TransportType
    {
        Tcp,
        Udp,
        Kcp,
        WebSocket,
        Custom
    }

    /// <summary>
    /// 连接状态
    /// </summary>
    public enum ConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
        Disconnecting,
        Reconnecting,
        Failed
    }

    /// <summary>
    /// 传输状态事件参数
    /// </summary>
    public class TransportStateEventArgs : EventArgs
    {
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
            ConnectionState previousState,
            ConnectionState currentState,
            string? reason = null,
            Exception? exception = null)
        {
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
        public IServerConnection Connection { get; }

        public ServerConnectionEventArgs(IServerConnection connection)
        {
            Connection = connection;
        }
    }
}
