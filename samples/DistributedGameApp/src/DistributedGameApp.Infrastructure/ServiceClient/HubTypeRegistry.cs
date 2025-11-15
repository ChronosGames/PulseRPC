using System.Collections.Concurrent;

namespace DistributedGameApp.Infrastructure.ServiceClient;

/// <summary>
/// Hub 类型注册表 - 管理 Hub 接口类型到服务类型的映射
/// </summary>
public class HubTypeRegistry
{
    private readonly ConcurrentDictionary<Type, string> _hubToServiceName = new();

    /// <summary>
    /// 注册 Hub 类型映射
    /// </summary>
    /// <typeparam name="THub">Hub 接口类型</typeparam>
    /// <param name="serviceName">服务名称 (如 "BackendServer", "BattleServer")</param>
    public void Register<THub>(string serviceName) where THub : class
    {
        var hubType = typeof(THub);
        _hubToServiceName[hubType] = serviceName;
    }

    /// <summary>
    /// 注册 Hub 类型映射
    /// </summary>
    public void Register(Type hubType, string serviceName)
    {
        if (hubType == null)
            throw new ArgumentNullException(nameof(hubType));

        if (string.IsNullOrEmpty(serviceName))
            throw new ArgumentException("Service name cannot be null or empty", nameof(serviceName));

        _hubToServiceName[hubType] = serviceName;
    }

    /// <summary>
    /// 获取 Hub 对应的服务名称
    /// </summary>
    public string? GetServiceName<THub>() where THub : class
    {
        return GetServiceName(typeof(THub));
    }

    /// <summary>
    /// 获取 Hub 对应的服务名称
    /// </summary>
    public string? GetServiceName(Type hubType)
    {
        if (hubType == null)
            throw new ArgumentNullException(nameof(hubType));

        if (_hubToServiceName.TryGetValue(hubType, out var serviceName))
        {
            return serviceName;
        }

        // 尝试从接口名推断 (如 IBackendHub -> BackendServer)
        var hubName = hubType.Name;
        if (hubName.StartsWith("I") && hubName.EndsWith("Hub"))
        {
            var baseName = hubName.Substring(1, hubName.Length - 4); // 移除 "I" 前缀和 "Hub" 后缀
            return $"{baseName}Server";
        }

        return null;
    }

    /// <summary>
    /// 清除所有注册
    /// </summary>
    public void Clear()
    {
        _hubToServiceName.Clear();
    }
}
