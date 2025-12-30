using System.Collections.Concurrent;
using System.Reflection;
using PulseRPC;

namespace DistributedGameApp.Infrastructure.ServiceClient;

/// <summary>
/// Hub 类型注册表 - 管理 Hub 接口类型到服务类型的映射
/// </summary>
/// <remarks>
/// 支持三种注册方式（按优先级排序）：
/// <list type="number">
/// <item><description>显式注册 - 通过 <see cref="Register{THub}(string)"/> 手动指定映射</description></item>
/// <item><description>特性自动注册 - 从 <see cref="ChannelAttribute"/> 读取服务名称</description></item>
/// <item><description>命名约定推断 - 从接口名推断（如 IBackendHub → BackendServer）</description></item>
/// </list>
/// </remarks>
public class HubTypeRegistry
{
    private readonly ConcurrentDictionary<Type, string> _hubToServiceName = new();

    /// <summary>
    /// 注册 Hub 类型映射（显式指定服务名称）
    /// </summary>
    /// <typeparam name="THub">Hub 接口类型</typeparam>
    /// <param name="serviceName">服务名称 (如 "BackendServer", "BattleServer")</param>
    public void Register<THub>(string serviceName) where THub : class
    {
        var hubType = typeof(THub);
        _hubToServiceName[hubType] = serviceName;
    }

    /// <summary>
    /// 注册 Hub 类型映射（显式指定服务名称）
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
    /// 从 [Channel] 特性自动注册单个 Hub 类型
    /// </summary>
    /// <typeparam name="THub">Hub 接口类型</typeparam>
    /// <returns>是否成功注册（如果找到 [Channel] 特性）</returns>
    public bool RegisterFromAttribute<THub>() where THub : class
    {
        return RegisterFromAttribute(typeof(THub));
    }

    /// <summary>
    /// 从 [Channel] 特性自动注册单个 Hub 类型
    /// </summary>
    /// <param name="hubType">Hub 接口类型</param>
    /// <returns>是否成功注册（如果找到 [Channel] 特性）</returns>
    public bool RegisterFromAttribute(Type hubType)
    {
        if (hubType == null)
            throw new ArgumentNullException(nameof(hubType));

        var serviceName = GetServiceNameFromAttribute(hubType);
        if (serviceName != null)
        {
            _hubToServiceName[hubType] = serviceName;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 扫描程序集，自动注册所有带有 [Channel] 特性的 IPulseHub 接口
    /// </summary>
    /// <param name="assembly">要扫描的程序集</param>
    /// <returns>成功注册的 Hub 类型数量</returns>
    public int RegisterFromAssembly(Assembly assembly)
    {
        if (assembly == null)
            throw new ArgumentNullException(nameof(assembly));

        var count = 0;
        var pulseHubType = typeof(IPulseHub);

        foreach (var type in assembly.GetTypes())
        {
            // 只处理接口且继承自 IPulseHub
            if (!type.IsInterface || !pulseHubType.IsAssignableFrom(type))
                continue;

            // 跳过 IPulseHub 本身
            if (type == pulseHubType)
                continue;

            if (RegisterFromAttribute(type))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// 扫描多个程序集，自动注册所有带有 [Channel] 特性的 IPulseHub 接口
    /// </summary>
    /// <param name="assemblies">要扫描的程序集列表</param>
    /// <returns>成功注册的 Hub 类型数量</returns>
    public int RegisterFromAssemblies(params Assembly[] assemblies)
    {
        var count = 0;
        foreach (var assembly in assemblies)
        {
            count += RegisterFromAssembly(assembly);
        }
        return count;
    }

    /// <summary>
    /// 扫描包含指定标记类型的程序集
    /// </summary>
    /// <typeparam name="TMarker">程序集中的任意类型（用于定位程序集）</typeparam>
    /// <returns>成功注册的 Hub 类型数量</returns>
    public int RegisterFromAssemblyContaining<TMarker>()
    {
        return RegisterFromAssembly(typeof(TMarker).Assembly);
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
    /// <remarks>
    /// 查找顺序：
    /// 1. 缓存中的显式注册
    /// 2. [Channel] 特性（并缓存）
    /// 3. 命名约定推断（IXxxHub → XxxServer）
    /// </remarks>
    public string? GetServiceName(Type hubType)
    {
        if (hubType == null)
            throw new ArgumentNullException(nameof(hubType));

        // 1. 优先从缓存获取
        if (_hubToServiceName.TryGetValue(hubType, out var serviceName))
        {
            return serviceName;
        }

        // 2. 尝试从 [Channel] 特性读取并缓存
        var attributeServiceName = GetServiceNameFromAttribute(hubType);
        if (attributeServiceName != null)
        {
            // 缓存以避免重复反射
            _hubToServiceName.TryAdd(hubType, attributeServiceName);
            return attributeServiceName;
        }

        // 3. 尝试从接口名推断 (如 IBackendHub -> BackendServer)
        var inferredName = InferServiceNameFromTypeName(hubType);
        if (inferredName != null)
        {
            // 缓存推断结果
            _hubToServiceName.TryAdd(hubType, inferredName);
            return inferredName;
        }

        return null;
    }

    /// <summary>
    /// 检查 Hub 类型是否已注册
    /// </summary>
    public bool IsRegistered<THub>() where THub : class
    {
        return IsRegistered(typeof(THub));
    }

    /// <summary>
    /// 检查 Hub 类型是否已注册
    /// </summary>
    public bool IsRegistered(Type hubType)
    {
        return _hubToServiceName.ContainsKey(hubType);
    }

    /// <summary>
    /// 获取所有已注册的 Hub 类型
    /// </summary>
    public IReadOnlyDictionary<Type, string> GetAllRegistrations()
    {
        return _hubToServiceName;
    }

    /// <summary>
    /// 清除所有注册
    /// </summary>
    public void Clear()
    {
        _hubToServiceName.Clear();
    }

    /// <summary>
    /// 从 [Channel] 特性读取服务名称
    /// </summary>
    private static string? GetServiceNameFromAttribute(Type hubType)
    {
        var channelAttr = hubType.GetCustomAttribute<ChannelAttribute>();
        return channelAttr?.ChannelName;
    }

    /// <summary>
    /// 从类型名推断服务名称
    /// </summary>
    private static string? InferServiceNameFromTypeName(Type hubType)
    {
        var hubName = hubType.Name;
        if (hubName.StartsWith("I") && hubName.EndsWith("Hub"))
        {
            // 移除 "I" 前缀和 "Hub" 后缀
            var baseName = hubName.Substring(1, hubName.Length - 4);
            return $"{baseName}Server";
        }
        return null;
    }
}
