using PulseRPC.Scheduling;
using PulseRPC.Server.Abstractions;

namespace PulseRPC.Server.Pipeline;

/// <summary>
/// IUnifiedPulseService 检测器,用于判断服务实例是否实现了 IUnifiedPulseService 接口
/// </summary>
/// <remarks>
/// <para>
/// 此类提供静态方法用于检测服务对象是否实现 <see cref="IUnifiedPulseService"/> 接口,
/// 并提取 ServiceType 和 ServiceId 用于线程调度。
/// </para>
/// <para>
/// <strong>使用场景</strong>:
/// </para>
/// <list type="bullet">
/// <item><description>MessageDispatcher 在分发消息前检测服务类型</description></item>
/// <item><description>ServiceInvoker 在调用前判断是否需要健康检查</description></item>
/// <item><description>诊断端点查询服务实例信息</description></item>
/// </list>
/// </remarks>
public static class IPulseServiceDetector
{
    /// <summary>
    /// 检测服务对象是否实现 IUnifiedPulseService 接口
    /// </summary>
    /// <param name="serviceInstance">服务实例对象</param>
    /// <returns>如果实现了 IUnifiedPulseService 返回 true,否则返回 false</returns>
    public static bool IsIPulseService(object? serviceInstance)
    {
        return serviceInstance is IUnifiedPulseService;
    }

    /// <summary>
    /// 尝试从服务实例提取 ServiceSchedulingKey
    /// </summary>
    /// <param name="serviceInstance">服务实例对象</param>
    /// <param name="key">输出参数,提取的调度键</param>
    /// <returns>如果成功提取返回 true,否则返回 false</returns>
    /// <remarks>
    /// 仅当服务实例实现 <see cref="IUnifiedPulseService"/> 接口,且 ServiceType 和 ServiceId 都非空时返回 true。
    /// </remarks>
    public static bool TryGetSchedulingKey(object? serviceInstance, out ServiceSchedulingKey key)
    {
        if (serviceInstance is IUnifiedPulseService pulseService)
        {
            if (!string.IsNullOrWhiteSpace(pulseService.ServiceType) &&
                !string.IsNullOrWhiteSpace(pulseService.ServiceId))
            {
                key = new ServiceSchedulingKey(pulseService.ServiceType, pulseService.ServiceId);
                return true;
            }
        }

        key = default;
        return false;
    }

    /// <summary>
    /// 从服务实例提取 ServiceSchedulingKey (如果失败抛出异常)
    /// </summary>
    /// <param name="serviceInstance">服务实例对象</param>
    /// <returns>提取的调度键</returns>
    /// <exception cref="ArgumentNullException">当 serviceInstance 为 null 时抛出</exception>
    /// <exception cref="InvalidOperationException">当服务实例未实现 IUnifiedPulseService 或属性为空时抛出</exception>
    public static ServiceSchedulingKey GetSchedulingKey(object serviceInstance)
    {
        ArgumentNullException.ThrowIfNull(serviceInstance);

        if (serviceInstance is not IUnifiedPulseService pulseService)
        {
            throw new InvalidOperationException(
                $"Service instance of type '{serviceInstance.GetType().Name}' does not implement IUnifiedPulseService interface");
        }

        if (string.IsNullOrWhiteSpace(pulseService.ServiceType))
        {
            throw new InvalidOperationException(
                $"IUnifiedPulseService.ServiceType is null or empty for service instance of type '{serviceInstance.GetType().Name}'");
        }

        if (string.IsNullOrWhiteSpace(pulseService.ServiceId))
        {
            throw new InvalidOperationException(
                $"IUnifiedPulseService.ServiceId is null or empty for service instance of type '{serviceInstance.GetType().Name}'");
        }

        return new ServiceSchedulingKey(pulseService.ServiceType, pulseService.ServiceId);
    }

    /// <summary>
    /// 获取服务实例的描述字符串 (用于日志和诊断)
    /// </summary>
    /// <param name="serviceInstance">服务实例对象</param>
    /// <returns>描述字符串</returns>
    /// <remarks>
    /// 如果实现了 IUnifiedPulseService 返回 "ServiceType:ServiceId",否则返回类型名称。
    /// </remarks>
    public static string GetServiceDescription(object? serviceInstance)
    {
        if (serviceInstance is null)
        {
            return "null";
        }

        if (TryGetSchedulingKey(serviceInstance, out var key))
        {
            return key.ToString();
        }

        return serviceInstance.GetType().Name;
    }
}
