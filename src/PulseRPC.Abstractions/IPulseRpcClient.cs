using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PulseRPC.Transport;

namespace PulseRPC;

/// <summary>
/// PulseRPC 客户端接口
/// </summary>
public interface IPulseRpcClient : IDisposable
{
    /// <summary>
    /// 连接到服务器
    /// </summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 断开连接
    /// </summary>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 是否已连接
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// 获取通道管理器
    /// </summary>
    IChannelManager GetChannelManager();

    /// <summary>
    /// 获取已配置的传输信息
    /// </summary>
    IReadOnlyDictionary<string, (TransportType Type, string Host, int Port, bool IsDefault)> GetTransports();
}

// 接口定义已移至其他文件：
// - IChannelManager 在 src/PulseRPC.Client/Channels/ChannelManager.cs
// - IClientChannel 在 src/PulseRPC.Abstractions/Channels/MessageHeader.cs

// ISubscriptionToken 和 IPulseReceiver 已在 Events/EventHandler.cs 中定义 