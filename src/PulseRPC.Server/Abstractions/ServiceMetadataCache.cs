using System.Collections.Concurrent;
using System.Reflection;

namespace PulseRPC.Server.Abstractions;

/// <summary>
/// 服务元数据缓存 - 缓存 PulseServiceAttribute 信息，避免重复反射
/// </summary>
public static class ServiceMetadataCache
{
    private static readonly ConcurrentDictionary<Type, ServiceMetadata> _cache = new();

    /// <summary>
    /// 获取服务元数据
    /// </summary>
    /// <typeparam name="TService">服务类型</typeparam>
    /// <returns>服务元数据</returns>
    public static ServiceMetadata Get<TService>() where TService : class, IUnifiedPulseService
    {
        return _cache.GetOrAdd(typeof(TService), static type =>
        {
            var attr = type.GetCustomAttribute<PulseServiceAttribute>();
            return new ServiceMetadata
            {
                ServiceType = type,
                InstanceScope = attr?.InstanceScope ?? ServiceInstanceScope.MultiInstance,
                DisplayName = attr?.DisplayName ?? type.Name,
                DefaultServiceId = GetDefaultServiceId(attr?.InstanceScope ?? ServiceInstanceScope.MultiInstance)
            };
        });
    }

    /// <summary>
    /// 获取服务元数据（非泛型版本）
    /// </summary>
    /// <param name="serviceType">服务类型</param>
    /// <returns>服务元数据</returns>
    public static ServiceMetadata Get(Type serviceType)
    {
        return _cache.GetOrAdd(serviceType, static type =>
        {
            var attr = type.GetCustomAttribute<PulseServiceAttribute>();
            return new ServiceMetadata
            {
                ServiceType = type,
                InstanceScope = attr?.InstanceScope ?? ServiceInstanceScope.MultiInstance,
                DisplayName = attr?.DisplayName ?? type.Name,
                DefaultServiceId = GetDefaultServiceId(attr?.InstanceScope ?? ServiceInstanceScope.MultiInstance)
            };
        });
    }

    private static string? GetDefaultServiceId(ServiceInstanceScope scope)
    {
        return scope switch
        {
            ServiceInstanceScope.Singleton => "default",
            ServiceInstanceScope.MultiInstance => null, // 需要动态提供
            _ => null
        };
    }
}

/// <summary>
/// 服务元数据
/// </summary>
public sealed class ServiceMetadata
{
    /// <summary>
    /// 服务类型
    /// </summary>
    public required Type ServiceType { get; init; }

    /// <summary>
    /// 实例范围
    /// </summary>
    public ServiceInstanceScope InstanceScope { get; init; }

    /// <summary>
    /// 显示名称
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// 默认 ServiceId（仅 Singleton 服务有值）
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description><see cref="ServiceInstanceScope.Singleton"/>: "default"</description></item>
    /// <item><description><see cref="ServiceInstanceScope.MultiInstance"/>: null</description></item>
    /// </list>
    /// </remarks>
    public string? DefaultServiceId { get; init; }

    /// <summary>
    /// 是否为单例服务
    /// </summary>
    public bool IsSingleton => InstanceScope is ServiceInstanceScope.Singleton;
}
