namespace PulseRPC.Server;

/// <summary>
/// 组管理器接口
/// </summary>
/// <remarks>
/// <para>
/// 管理客户端连接的分组，支持组播功能。
/// </para>
/// <para>
/// <strong>使用场景</strong>：
/// </para>
/// <list type="bullet">
/// <item><description>聊天室 - 将用户加入房间组</description></item>
/// <item><description>游戏房间 - 将玩家加入游戏组</description></item>
/// <item><description>订阅主题 - 将连接加入主题组</description></item>
/// </list>
/// <para>
/// <strong>使用示例</strong>：
/// </para>
/// <code>
/// // 将连接加入组
/// await _groupManager.AddToGroupAsync(connectionId, "room-123");
///
/// // 向组推送消息
/// await _hubContext.Clients.Group("room-123").OnMessageAsync(message);
///
/// // 将连接从组移除
/// await _groupManager.RemoveFromGroupAsync(connectionId, "room-123");
/// </code>
/// </remarks>
public interface IGroupManager
{
    /// <summary>
    /// 将连接加入组
    /// </summary>
    /// <param name="connectionId">连接ID</param>
    /// <param name="groupName">组名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 将连接从组移除
    /// </summary>
    /// <param name="connectionId">连接ID</param>
    /// <param name="groupName">组名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 将连接从所有组移除
    /// </summary>
    /// <param name="connectionId">连接ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <remarks>
    /// 通常在连接断开时调用，清理该连接的所有组成员关系。
    /// </remarks>
    Task RemoveFromAllGroupsAsync(string connectionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取组内所有连接ID
    /// </summary>
    /// <param name="groupName">组名称</param>
    /// <returns>组内连接ID集合，如果组不存在返回空集合</returns>
    IReadOnlyCollection<string> GetGroupConnections(string groupName);

    /// <summary>
    /// 获取多个组内所有连接ID
    /// </summary>
    /// <param name="groupNames">组名称集合</param>
    /// <returns>组内连接ID集合（去重）</returns>
    IReadOnlyCollection<string> GetGroupConnections(IEnumerable<string> groupNames);

    /// <summary>
    /// 获取连接所属的所有组名称
    /// </summary>
    /// <param name="connectionId">连接ID</param>
    /// <returns>组名称集合</returns>
    IReadOnlyCollection<string> GetConnectionGroups(string connectionId);

    /// <summary>
    /// 检查连接是否在指定组中
    /// </summary>
    /// <param name="connectionId">连接ID</param>
    /// <param name="groupName">组名称</param>
    /// <returns>如果连接在组中返回 true</returns>
    bool IsInGroup(string connectionId, string groupName);

    /// <summary>
    /// 获取组的成员数量
    /// </summary>
    /// <param name="groupName">组名称</param>
    /// <returns>组成员数量</returns>
    int GetGroupSize(string groupName);

    /// <summary>
    /// 检查组是否存在（有至少一个成员）
    /// </summary>
    /// <param name="groupName">组名称</param>
    /// <returns>如果组存在返回 true</returns>
    bool GroupExists(string groupName);
}

