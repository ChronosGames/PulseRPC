using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC.Server.Hubs;

/// <summary>
/// 组提供器接口 - 用于在 Hub 内创建/获取组实例
/// </summary>
/// <remarks>
/// <para>
/// 参考 <see href="https://cysharp.github.io/MagicOnion/streaminghub/group">MagicOnion Group</see> 设计。
/// </para>
/// <para>
/// 在 Hub 方法内使用 <c>Group</c> 属性访问此提供器：
/// </para>
/// <code>
/// public async Task JoinRoomAsync(string roomName)
/// {
///     // 添加当前客户端到组，如果组不存在则创建
///     this.room = await Group.AddAsync(roomName);
///
///     // 通知房间内其他人
///     room.Except(ConnectionId).OnPlayerJoined(playerInfo);
/// }
/// </code>
/// </remarks>
/// <typeparam name="TReceiver">客户端接收器接口</typeparam>
public interface IGroupProvider<TReceiver> where TReceiver : IPulseReceiver
{
    /// <summary>
    /// 将当前连接添加到指定组（如果组不存在则创建）
    /// </summary>
    /// <param name="groupName">组名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>组实例</returns>
    /// <remarks>
    /// 与 MagicOnion 的 <c>Group.AddAsync(groupName)</c> 对应。
    /// </remarks>
    Task<IGroup<TReceiver>> AddAsync(string groupName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取指定组（不添加当前连接）
    /// </summary>
    /// <param name="groupName">组名称</param>
    /// <returns>组实例，如果组不存在则返回 null</returns>
    IGroup<TReceiver>? Get(string groupName);

    /// <summary>
    /// 检查组是否存在
    /// </summary>
    /// <param name="groupName">组名称</param>
    bool Exists(string groupName);
}

