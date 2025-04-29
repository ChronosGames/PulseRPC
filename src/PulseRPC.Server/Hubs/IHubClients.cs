namespace PulseRPC.Server.Hubs;

/// <summary>
/// 提供对所有连接客户端的访问
/// </summary>
public interface IHubClients
{
    /// <summary>
    /// 获取对调用此方法的客户端的引用
    /// </summary>
    dynamic Caller { get; }

    /// <summary>
    /// 获取对所有连接的客户端的引用
    /// </summary>
    dynamic All { get; }

    /// <summary>
    /// 获取对特定组中所有客户端的引用
    /// </summary>
    /// <param name="groupName">组名称</param>
    /// <returns>组客户端引用</returns>
    dynamic Group(string groupName);

    /// <summary>
    /// 获取对除指定组外的所有客户端的引用
    /// </summary>
    /// <param name="excludedGroups">要排除的组名称</param>
    /// <returns>排除特定组的客户端引用</returns>
    dynamic AllExcept(params string[] excludedGroups);
}

/// <summary>
/// 提供对所有连接客户端的类型安全访问
/// </summary>
/// <typeparam name="TReceiver">客户端接收器接口类型</typeparam>
public interface IHubClients<out TReceiver> where TReceiver : class
{
    /// <summary>
    /// 获取对调用此方法的客户端的引用
    /// </summary>
    TReceiver Caller { get; }

    /// <summary>
    /// 获取对所有连接的客户端的引用
    /// </summary>
    TReceiver All { get; }

    /// <summary>
    /// 获取对特定组中所有客户端的引用
    /// </summary>
    /// <param name="groupName">组名称</param>
    /// <returns>组客户端引用</returns>
    TReceiver Group(string groupName);

    /// <summary>
    /// 获取对除指定组外的所有客户端的引用
    /// </summary>
    /// <param name="excludedGroups">要排除的组名称</param>
    /// <returns>排除特定组的客户端引用</returns>
    TReceiver AllExcept(params string[] excludedGroups);
}
