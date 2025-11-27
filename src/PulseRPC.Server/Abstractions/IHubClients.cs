using System.Collections.Generic;

namespace PulseRPC.Server;

/// <summary>
/// 客户端选择器接口 - 选择推送目标
/// </summary>
/// <typeparam name="TReceiver">接收器接口类型，必须继承 <see cref="IPulseReceiver"/></typeparam>
/// <remarks>
/// <para>
/// 参考 <see href="https://cysharp.github.io/MagicOnion/streaminghub/call-client">MagicOnion</see> 设计，
/// 提供简洁的客户端选择 API。
/// </para>
/// <para>
/// <strong>调用示例</strong>：
/// </para>
/// <code>
/// // 推送给所有客户端
/// Clients.All.OnEventAsync(data);
///
/// // 推送给单个连接
/// Clients.Single(connectionId).OnEventAsync(data);
///
/// // 推送给多个连接
/// Clients.Only(new[] { conn1, conn2 }).OnEventAsync(data);
///
/// // 推送给除指定连接外的所有客户端
/// Clients.Except(connectionId).OnEventAsync(data);
///
/// // 推送给指定用户（可能有多个连接）
/// Clients.User("user-123").OnEventAsync(data);
///
/// // 推送给指定组
/// Clients.Group("room-456").OnEventAsync(data);
/// </code>
/// </remarks>
public interface IHubClients<out TReceiver> where TReceiver : class, IPulseReceiver
{
    /// <summary>
    /// 获取所有已认证客户端的代理
    /// </summary>
    TReceiver All { get; }

    /// <summary>
    /// 获取单个连接的代理
    /// </summary>
    /// <param name="connectionId">连接ID</param>
    /// <returns>指定连接的代理</returns>
    TReceiver Single(string connectionId);

    /// <summary>
    /// 获取指定的多个连接的代理
    /// </summary>
    /// <param name="connectionIds">连接ID集合</param>
    /// <returns>指定连接集合的代理</returns>
    TReceiver Only(IReadOnlyList<string> connectionIds);

    /// <summary>
    /// 获取除指定连接外所有客户端的代理
    /// </summary>
    /// <param name="connectionId">要排除的连接ID</param>
    /// <returns>排除指定连接后的所有客户端代理</returns>
    /// <remarks>
    /// 常用于广播消息时排除发送者自己：
    /// <code>
    /// Clients.Except(ConnectionId).OnPlayerMoved(position);
    /// </code>
    /// </remarks>
    TReceiver Except(string connectionId);

    /// <summary>
    /// 获取除指定连接外所有客户端的代理
    /// </summary>
    /// <param name="connectionIds">要排除的连接ID集合</param>
    /// <returns>排除指定连接后的所有客户端代理</returns>
    TReceiver Except(IReadOnlyList<string> connectionIds);

    /// <summary>
    /// 获取指定用户的代理（通过 UserId）
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>指定用户的代理</returns>
    /// <remarks>
    /// 一个用户可能有多个连接，此方法会向该用户的所有连接推送消息。
    /// </remarks>
    TReceiver User(string userId);

    /// <summary>
    /// 获取多个用户的代理
    /// </summary>
    /// <param name="userIds">用户ID集合</param>
    /// <returns>指定用户集合的代理</returns>
    TReceiver Users(IReadOnlyList<string> userIds);

    /// <summary>
    /// 获取指定组的代理
    /// </summary>
    /// <param name="groupName">组名称</param>
    /// <returns>指定组的代理</returns>
    TReceiver Group(string groupName);

    /// <summary>
    /// 获取多个组的代理
    /// </summary>
    /// <param name="groupNames">组名称集合</param>
    /// <returns>指定组集合的代理</returns>
    TReceiver Groups(IReadOnlyList<string> groupNames);

    /// <summary>
    /// 获取组内除指定连接外所有客户端的代理
    /// </summary>
    /// <param name="groupName">组名称</param>
    /// <param name="excludedConnectionId">要排除的连接ID</param>
    /// <returns>组内排除指定连接后的所有客户端代理</returns>
    TReceiver GroupExcept(string groupName, string excludedConnectionId);

    /// <summary>
    /// 获取组内除指定连接外所有客户端的代理
    /// </summary>
    /// <param name="groupName">组名称</param>
    /// <param name="excludedConnectionIds">要排除的连接ID集合</param>
    /// <returns>组内排除指定连接后的所有客户端代理</returns>
    TReceiver GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds);
}
