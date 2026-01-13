namespace PulseRPC.Server;

/// <summary>
/// 用户-连接映射接口
/// </summary>
/// <remarks>
/// <para>
/// 管理用户ID与连接ID之间的映射关系。
/// 一个用户可以有多个连接（多设备登录），此接口用于追踪这种关系。
/// </para>
/// <para>
/// <strong>主要用途</strong>：
/// </para>
/// <list type="bullet">
/// <item><description>通过 UserId 查找用户的所有连接</description></item>
/// <item><description>通过 ConnectionId 查找连接所属的用户</description></item>
/// <item><description>支持 <see cref="IHubClients{TReceiver}.User"/> 方法的实现</description></item>
/// </list>
/// </remarks>
public interface IUserConnectionMapping
{
    /// <summary>
    /// 添加用户-连接映射
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="connectionId">连接ID</param>
    void Add(string userId, string connectionId);

    /// <summary>
    /// 移除用户-连接映射
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="connectionId">连接ID</param>
    void Remove(string userId, string connectionId);

    /// <summary>
    /// 通过连接ID移除映射
    /// </summary>
    /// <param name="connectionId">连接ID</param>
    /// <returns>被移除连接所属的用户ID，如果不存在返回 null</returns>
    string? RemoveByConnection(string connectionId);

    /// <summary>
    /// 获取用户的所有连接ID
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>连接ID集合，如果用户不存在返回空集合</returns>
    IReadOnlyCollection<string> GetConnections(string userId);

    /// <summary>
    /// 获取多个用户的所有连接ID
    /// </summary>
    /// <param name="userIds">用户ID集合</param>
    /// <returns>连接ID集合</returns>
    IReadOnlyCollection<string> GetConnections(IEnumerable<string> userIds);

    /// <summary>
    /// 获取连接所属的用户ID
    /// </summary>
    /// <param name="connectionId">连接ID</param>
    /// <returns>用户ID，如果连接不存在返回 null</returns>
    string? GetUserId(string connectionId);

    /// <summary>
    /// 检查用户是否在线
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>如果用户有至少一个活动连接返回 true</returns>
    bool IsUserOnline(string userId);

    /// <summary>
    /// 获取在线用户数量
    /// </summary>
    int OnlineUserCount { get; }

    /// <summary>
    /// 获取所有在线用户ID
    /// </summary>
    /// <returns>在线用户ID集合</returns>
    IReadOnlyCollection<string> GetOnlineUsers();
}

