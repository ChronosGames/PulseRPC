using System.Net;
using PulseRPC.Server.Transport;
using PulseRPC.Transport;
using TransportContext = System.Net.TransportContext;

namespace PulseRPC.Server;

/// <summary>
/// PulseRPC 服务器运行时接口
/// </summary>
public interface IPulseServer : IAsyncDisposable, IDisposable
{
    // === 生命周期管理 ===
    /// <summary>
    /// 启动服务器
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止服务器
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 服务器运行状态
    /// </summary>
    ServerState State { get; }

    /// <summary>
    /// 服务器是否正在运行
    /// </summary>
    bool IsRunning => State == ServerState.Running;

    /// <summary>
    /// 获取已配置的传输信息
    /// </summary>
    IReadOnlyDictionary<string, TransportInfo> GetTransports();

    /// <summary>
    /// 获取默认传输信息
    /// </summary>
    TransportInfo? GetDefaultTransport();

    // === 连接管理 ===
    /// <summary>
    /// 获取当前连接数
    /// </summary>
    int ActiveConnectionCount { get; }

    /// <summary>
    /// 获取所有活动连接信息
    /// </summary>
    IReadOnlyList<ConnectionInfo> GetActiveConnections();

    /// <summary>
    /// 广播消息到所有连接
    /// </summary>
    /// <param name="data">要发送的数据</param>
    /// <param name="filter">过滤条件，null表示发送给所有连接</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>成功发送的连接数量</returns>
    Task<int> BroadcastAsync(ReadOnlyMemory<byte> data, Func<TransportContext, bool>? filter = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 向指定连接发送数据
    /// </summary>
    /// <param name="connectionId">连接ID</param>
    /// <param name="data">要发送的数据</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否发送成功</returns>
    Task<bool> SendAsync(string connectionId, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

    // === 服务管理 ===
    /// <summary>
    /// 获取已注册的服务列表
    /// </summary>
    IReadOnlyList<ServiceInfo> GetRegisteredServices();

    // === 性能监控 ===
    /// <summary>
    /// 获取服务器性能统计
    /// </summary>
    ServerPerformanceMetrics GetPerformanceMetrics();

    /// <summary>
    /// 重置性能统计
    /// </summary>
    void ResetPerformanceMetrics();

    // === 事件通知 ===
    /// <summary>
    /// 服务器状态变更事件
    /// </summary>
    event EventHandler<ServerStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// 客户端连接事件
    /// </summary>
    event EventHandler<ClientConnectedEventArgs>? ClientConnected;

    /// <summary>
    /// 客户端断开连接事件
    /// </summary>
    event EventHandler<ClientDisconnectedEventArgs>? ClientDisconnected;
}

/// <summary>
/// 命名 PulseRPC 服务器接口
/// 支持在同一进程中运行多个服务器实例
/// </summary>
public interface INamedPulseServer : IPulseServer
{
    /// <summary>
    /// 服务器名称（唯一标识）
    /// </summary>
    string ServerName { get; }
}

/// <summary>
/// 服务器状态枚举
/// </summary>
public enum ServerState
{
    Stopped,
    Starting,
    Running,
    Stopping
}

/// <summary>
/// 传输信息
/// </summary>
public sealed class TransportInfo
{
    public required string Name { get; init; }
    public PulseRPC.Transport.TransportType Type { get; init; }
    public int Port { get; init; }
    public bool IsDefault { get; init; }
    public bool IsListening { get; init; }
    public EndPoint? LocalEndPoint { get; init; }
}

/// <summary>
/// 连接信息
/// </summary>
public sealed class ConnectionInfo
{
    public required string ConnectionId { get; init; }
    public required EndPoint RemoteEndPoint { get; init; }
    public TransportType TransportType { get; init; }
    public bool IsAuthenticated { get; init; }
    public DateTime ConnectedTime { get; init; }
    public DateTime LastActiveTime { get; init; }
}

/// <summary>
/// 服务信息
/// </summary>
public sealed class ServiceInfo
{
    public required string ServiceName { get; init; }
    public required Type ServiceType { get; init; }
    public required Type ImplementationType { get; init; }
    public Microsoft.Extensions.DependencyInjection.ServiceLifetime Lifetime { get; init; }
    public IReadOnlyDictionary<string, string> Tags { get; init; } = new Dictionary<string, string>();
}

/// <summary>
/// 服务器性能指标
/// </summary>
public sealed class ServerPerformanceMetrics
{
    public int ActiveConnections { get; init; }
    public long TotalConnectionsAccepted { get; init; }
    public long TotalMessagesProcessed { get; init; }
    public long TotalMessagesDropped { get; init; }
    public double AverageLatencyMs { get; init; }
    public double ThroughputMsgsPerSec { get; init; }
    public double MemoryUsageMB { get; init; }
    public double CpuUsagePercent { get; init; }
    public DateTime LastResetTime { get; init; }
}

/// <summary>
/// 事件参数类
/// </summary>
public sealed class ServerStateChangedEventArgs : EventArgs
{
    public ServerState OldState { get; }
    public ServerState NewState { get; }
    public DateTime Timestamp { get; }

    public ServerStateChangedEventArgs(ServerState oldState, ServerState newState)
    {
        OldState = oldState;
        NewState = newState;
        Timestamp = DateTime.UtcNow;
    }
}

public sealed class ClientConnectedEventArgs : EventArgs
{
    public IServerChannel Channel { get; }
    public DateTime Timestamp { get; }

    public ClientConnectedEventArgs(IServerChannel channel)
    {
        Channel = channel ?? throw new ArgumentNullException(nameof(channel));
        Timestamp = DateTime.UtcNow;
    }
}

public sealed class ClientDisconnectedEventArgs : EventArgs
{
    public IServerChannel Channel { get; }
    public DateTime Timestamp { get; }
    public string? DisconnectReason { get; }

    public ClientDisconnectedEventArgs(IServerChannel channel, string? disconnectReason = null)
    {
        Channel = channel ?? throw new ArgumentNullException(nameof(channel));
        DisconnectReason = disconnectReason;
        Timestamp = DateTime.UtcNow;
    }
}
