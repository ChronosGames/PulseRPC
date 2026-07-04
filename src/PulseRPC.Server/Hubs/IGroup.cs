using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC.Server.Hubs;

/// <summary>
/// 组接口 - 代表一个客户端组，提供向组内客户端发送消息的能力
/// </summary>
/// <remarks>
/// <para>
/// 参考 <see href="https://cysharp.github.io/MagicOnion/streaminghub/group">MagicOnion Group</see> 设计。
/// </para>
/// <para>
/// 组是可持有的实例对象，而不是字符串引用。这使得 API 更加流畅：
/// </para>
/// <code>
/// // 获取或创建组
/// this.room = await Group.AddAsync("room-123");
/// 
/// // 向组内所有客户端发送消息
/// this.room.All.OnPlayerJoined(player);
/// 
/// // 向除自己外的客户端发送消息
/// this.room.Except(ConnectionId).OnPlayerMoved(position);
/// </code>
/// </remarks>
/// <typeparam name="TReceiver">客户端接收器接口</typeparam>
public interface IGroup<TReceiver> where TReceiver : IPulseHub
{
    /// <summary>
    /// 组名称
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// 组内成员数量
    /// </summary>
    int MemberCount { get; }
    
    /// <summary>
    /// 获取组内所有连接ID
    /// </summary>
    IReadOnlyList<string> GetMemberConnectionIds();
    
    /// <summary>
    /// 向组内所有客户端发送消息
    /// </summary>
    /// <example>
    /// <code>
    /// room.All.OnMessage("Hello, everyone!");
    /// </code>
    /// </example>
    TReceiver All { get; }
    
    /// <summary>
    /// 向组内单个客户端发送消息
    /// </summary>
    /// <param name="connectionId">目标连接ID</param>
    /// <example>
    /// <code>
    /// room.Single(targetConnectionId).OnPrivateMessage("Hello!");
    /// </code>
    /// </example>
    TReceiver Single(string connectionId);
    
    /// <summary>
    /// 向组内指定的多个客户端发送消息
    /// </summary>
    /// <param name="connectionIds">目标连接ID列表</param>
    /// <example>
    /// <code>
    /// room.Only(new[] { conn1, conn2 }).OnTeamMessage("Team A wins!");
    /// </code>
    /// </example>
    TReceiver Only(IReadOnlyList<string> connectionIds);
    
    /// <summary>
    /// 向组内除指定客户端外的所有客户端发送消息
    /// </summary>
    /// <param name="connectionId">要排除的连接ID</param>
    /// <example>
    /// <code>
    /// // 向房间内除自己外的所有人广播
    /// room.Except(ConnectionId).OnPlayerMoved(myPosition);
    /// </code>
    /// </example>
    TReceiver Except(string connectionId);
    
    /// <summary>
    /// 向组内除指定客户端外的所有客户端发送消息
    /// </summary>
    /// <param name="connectionIds">要排除的连接ID列表</param>
    TReceiver Except(IReadOnlyList<string> connectionIds);
    
    /// <summary>
    /// 从组中移除指定连接
    /// </summary>
    /// <param name="connectionId">要移除的连接ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task RemoveAsync(string connectionId, CancellationToken cancellationToken = default);
}

