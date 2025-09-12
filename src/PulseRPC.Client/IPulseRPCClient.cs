using PulseRPC.Client.Core;
using PulseRPC.Transport;

namespace PulseRPC.Client.Redesign;

/// <summary>
/// 游戏客户端扩展方法
/// </summary>
public static class GameClientExtensions
{
    /// <summary>
    /// 连接到核心游戏服务器
    /// </summary>
    public static async Task<IConnectionContext> ConnectToCoreServerAsync(
        this IPulseRPCClient client,
        string serviceName,
        TransportType transport = TransportType.Tcp,
        CancellationToken cancellationToken = default)
    {
        var config = new ConnectionConfig
        {
            Name = $"core-{serviceName}",
            ServiceName = serviceName,
            Transport = transport,
            Lifetime = ConnectionLifetime.Persistent,
            AutoReconnect = true,
            Tags = { ["type"] = "core", ["service"] = serviceName }
        };

        return await client.Connections.ConnectAsync(config, cancellationToken);
    }

    /// <summary>
    /// 连接到战斗服务器
    /// </summary>
    public static async Task<IConnectionContext> ConnectToBattleServerAsync(
        this IPulseRPCClient client,
        string battleId,
        string host,
        int port,
        CancellationToken cancellationToken = default)
    {
        var config = new ConnectionConfig
        {
            Name = $"battle-{battleId}",
            Host = host,
            Port = port,
            Transport = TransportType.Kcp, // 战斗服优先使用KCP
            Lifetime = ConnectionLifetime.Transient,
            AutoReconnect = false,
            IdleTimeout = TimeSpan.FromMinutes(1), // 1分钟无活动自动断开
            Tags = { ["type"] = "battle", ["battleId"] = battleId }
        };

        return await client.Connections.ConnectAsync(config, cancellationToken);
    }

    /// <summary>
    /// 连接到副本服务器
    /// </summary>
    public static async Task<IConnectionContext> ConnectToInstanceServerAsync(
        this IPulseRPCClient client,
        string instanceId,
        string host,
        int port,
        CancellationToken cancellationToken = default)
    {
        var config = new ConnectionConfig
        {
            Name = $"instance-{instanceId}",
            Host = host,
            Port = port,
            Transport = TransportType.Tcp,
            Lifetime = ConnectionLifetime.Transient,
            AutoReconnect = true,
            IdleTimeout = TimeSpan.FromMinutes(5),
            Tags = { ["type"] = "instance", ["instanceId"] = instanceId }
        };

        return await client.Connections.ConnectAsync(config, cancellationToken);
    }

    /// <summary>
    /// 连接到地图服务器
    /// </summary>
    public static async Task<IConnectionContext> ConnectToMapServerAsync(
        this IPulseRPCClient client,
        string mapId,
        string? serviceName = null,
        CancellationToken cancellationToken = default)
    {
        var config = new ConnectionConfig
        {
            Name = $"map-{mapId}",
            ServiceName = serviceName ?? $"map-server-{mapId}",
            Transport = TransportType.Tcp,
            Lifetime = ConnectionLifetime.Session,
            AutoReconnect = true,
            IdleTimeout = TimeSpan.FromMinutes(10),
            Tags = { ["type"] = "map", ["mapId"] = mapId }
        };

        return await client.Connections.ConnectAsync(config, cancellationToken);
    }

    /// <summary>
    /// 批量断开特定类型的连接
    /// </summary>
    public static async Task DisconnectByTypeAsync(
        this IPulseRPCClient client,
        string type,
        CancellationToken cancellationToken = default)
    {
        await client.Connections.DisconnectAsync(
            conn => conn.Config.Tags.GetValueOrDefault("type") == type,
            cancellationToken);
    }

    /// <summary>
    /// 离开战斗（断开战斗服连接）
    /// </summary>
    public static async Task LeaveBattleAsync(
        this IPulseRPCClient client,
        string battleId,
        CancellationToken cancellationToken = default)
    {
        await client.Connections.DisconnectAsync($"battle-{battleId}", cancellationToken);
    }

    /// <summary>
    /// 离开副本（断开副本服连接）
    /// </summary>
    public static async Task LeaveInstanceAsync(
        this IPulseRPCClient client,
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        await client.Connections.DisconnectAsync($"instance-{instanceId}", cancellationToken);
    }

    /// <summary>
    /// 切换地图（断开旧地图，连接新地图）
    /// </summary>
    public static async Task<IConnectionContext> SwitchMapAsync(
        this IPulseRPCClient client,
        string oldMapId,
        string newMapId,
        string? serviceName = null,
        CancellationToken cancellationToken = default)
    {
        // 先连接新地图
        var newConnection = await client.ConnectToMapServerAsync(newMapId, serviceName, cancellationToken);

        // 再断开旧地图
        await client.Connections.DisconnectAsync($"map-{oldMapId}", cancellationToken);

        return newConnection;
    }

    /// <summary>
    /// 使用临时连接执行操作（自动清理）
    /// </summary>
    public static async Task WithTemporaryConnectionAsync(
        this IPulseRPCClient client,
        ConnectionConfig config,
        Func<IConnectionContext, Task> action,
        CancellationToken cancellationToken = default)
    {
        var connection = await client.Connections.ConnectAsync(config, cancellationToken);
        try
        {
            await action(connection);
        }
        finally
        {
            await client.Connections.DisconnectAsync(config.Name, cancellationToken);
        }
    }
}
