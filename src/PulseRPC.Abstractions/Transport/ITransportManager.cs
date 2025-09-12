using System.Collections.Concurrent;
using System.Net;
using PulseRPC.Authentication;

namespace PulseRPC.Transport;

/// <summary>
/// 传输管理器类型
/// </summary>
public enum TransportManagerType
{
    Client,
    Server
}

/// <summary>
/// 传输管理器统计信息
/// </summary>
public sealed class TransportManagerStatistics
{
    public int ActiveTransports { get; set; }
    public long TotalTransportsCreated { get; set; }
    public long TotalTransportsRemoved { get; set; }
    public long TotalMessagesProcessed { get; set; }
    public long TotalMessagesDropped { get; set; }
    public long TotalBytesSent { get; set; }
    public long TotalBytesReceived { get; set; }
    public TimeSpan AverageConnectionDuration { get; set; }
    public DateTime LastResetTime { get; set; }
    public TransportManagerType ManagerType { get; set; }
}

/// <summary>
/// 传输连接事件参数
/// </summary>
public sealed class TransportConnectedEventArgs : EventArgs
{
    public TransportContext TransportContext { get; }
    public ITransport Transport { get; }
    public DateTime Timestamp { get; }

    public TransportConnectedEventArgs(TransportContext transportContext, ITransport transport)
    {
        TransportContext = transportContext ?? throw new ArgumentNullException(nameof(transportContext));
        Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        Timestamp = DateTime.UtcNow;
    }
}

/// <summary>
/// 传输断开事件参数
/// </summary>
public sealed class TransportDisconnectedEventArgs : EventArgs
{
    public TransportContext TransportContext { get; }
    public string? DisconnectReason { get; }
    public DateTime Timestamp { get; }

    public TransportDisconnectedEventArgs(TransportContext transportContext, string? disconnectReason = null)
    {
        TransportContext = transportContext ?? throw new ArgumentNullException(nameof(transportContext));
        DisconnectReason = disconnectReason;
        Timestamp = DateTime.UtcNow;
    }
}

/// <summary>
/// 传输认证事件参数
/// </summary>
public sealed class TransportAuthenticatedEventArgs : EventArgs
{
    public TransportContext TransportContext { get; }
    public IAuthenticationContext AuthenticationContext { get; }
    public DateTime Timestamp { get; }

    public TransportAuthenticatedEventArgs(TransportContext transportContext, IAuthenticationContext authenticationContext)
    {
        TransportContext = transportContext ?? throw new ArgumentNullException(nameof(transportContext));
        AuthenticationContext = authenticationContext ?? throw new ArgumentNullException(nameof(authenticationContext));
        Timestamp = DateTime.UtcNow;
    }
}

