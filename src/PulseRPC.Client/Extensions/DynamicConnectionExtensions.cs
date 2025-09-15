using PulseRPC.Transport;

namespace PulseRPC.Client.Extensions;

/// <summary>
/// 动态连接扩展方法
/// </summary>
public static class DynamicConnectionExtensions
{
    /// <summary>
    /// 添加动态 TCP 连接
    /// </summary>
    /// <param name="client">客户端实例</param>
    /// <param name="name">连接名称</param>
    /// <param name="host">主机地址</param>
    /// <param name="port">端口号</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>动态连接令牌</returns>
    public static async Task<IDynamicConnectionToken> AddDynamicTcpConnectionAsync(
        this IPulseRPCClient client,
        string name,
        string host,
        int port,
        CancellationToken cancellationToken = default)
    {
        var options = new TransportOptions
        {
            ConnectionTimeout = 10000,
            KeepAlive = true,
            NoDelay = true
        };

        return await client.AddDynamicConnectionAsync(name, TransportType.Tcp, host, port, options, cancellationToken);
    }

    /// <summary>
    /// 添加动态 KCP 连接
    /// </summary>
    /// <param name="client">客户端实例</param>
    /// <param name="name">连接名称</param>
    /// <param name="host">主机地址</param>
    /// <param name="port">端口号</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>动态连接令牌</returns>
    public static async Task<IDynamicConnectionToken> AddDynamicKcpConnectionAsync(
        this IPulseRPCClient client,
        string name,
        string host,
        int port,
        CancellationToken cancellationToken = default)
    {
        var options = new TransportOptions
        {
            ConnectionTimeout = 10000,
            KeepAlive = true,
            Kcp = new KcpOptions
            {
                NoDelay = 1,
                Interval = 10,
                Resend = 2,
                DisableFlowControl = false
            }
        };

        return await client.AddDynamicConnectionAsync(name, TransportType.Kcp, host, port, options, cancellationToken);
    }

    /// <summary>
    /// 添加临时战斗服连接
    /// </summary>
    /// <param name="client">客户端实例</param>
    /// <param name="battleServerId">战斗服务器ID</param>
    /// <param name="host">主机地址</param>
    /// <param name="port">端口号</param>
    /// <param name="useKcp">是否使用KCP协议</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>动态连接令牌</returns>
    public static async Task<IDynamicConnectionToken> AddBattleServerConnectionAsync(
        this IPulseRPCClient client,
        string battleServerId,
        string host,
        int port,
        bool useKcp = true,
        CancellationToken cancellationToken = default)
    {
        var connectionName = $"battle-{battleServerId}";
        
        if (useKcp)
        {
            return await client.AddDynamicKcpConnectionAsync(connectionName, host, port, cancellationToken);
        }
        else
        {
            return await client.AddDynamicTcpConnectionAsync(connectionName, host, port, cancellationToken);
        }
    }

    /// <summary>
    /// 添加临时副本服连接
    /// </summary>
    /// <param name="client">客户端实例</param>
    /// <param name="instanceId">副本ID</param>
    /// <param name="host">主机地址</param>
    /// <param name="port">端口号</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>动态连接令牌</returns>
    public static async Task<IDynamicConnectionToken> AddInstanceServerConnectionAsync(
        this IPulseRPCClient client,
        string instanceId,
        string host,
        int port,
        CancellationToken cancellationToken = default)
    {
        var connectionName = $"instance-{instanceId}";
        return await client.AddDynamicTcpConnectionAsync(connectionName, host, port, cancellationToken);
    }

    /// <summary>
    /// 使用 using 语句安全地管理动态连接生命周期
    /// </summary>
    /// <param name="client">客户端实例</param>
    /// <param name="name">连接名称</param>
    /// <param name="type">传输类型</param>
    /// <param name="host">主机地址</param>
    /// <param name="port">端口号</param>
    /// <param name="action">使用连接的操作</param>
    /// <param name="options">传输选项</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns></returns>
    public static async Task WithDynamicConnectionAsync(
        this IPulseRPCClient client,
        string name,
        TransportType type,
        string host,
        int port,
        Func<IDynamicConnectionToken, Task> action,
        TransportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var connection = await client.AddDynamicConnectionAsync(name, type, host, port, options, cancellationToken);
        try
        {
            await action(connection);
        }
        finally
        {
            await client.RemoveDynamicConnectionAsync(connection, cancellationToken);
        }
    }

    /// <summary>
    /// 使用 using 语句安全地管理战斗服连接生命周期
    /// </summary>
    /// <param name="client">客户端实例</param>
    /// <param name="battleServerId">战斗服务器ID</param>
    /// <param name="host">主机地址</param>
    /// <param name="port">端口号</param>
    /// <param name="action">使用连接的操作</param>
    /// <param name="useKcp">是否使用KCP协议</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns></returns>
    public static async Task WithBattleServerAsync(
        this IPulseRPCClient client,
        string battleServerId,
        string host,
        int port,
        Func<IDynamicConnectionToken, Task> action,
        bool useKcp = true,
        CancellationToken cancellationToken = default)
    {
        var connection = await client.AddBattleServerConnectionAsync(battleServerId, host, port, useKcp, cancellationToken);
        try
        {
            await action(connection);
        }
        finally
        {
            await client.RemoveDynamicConnectionAsync(connection, cancellationToken);
        }
    }
}