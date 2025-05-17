using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace PulseRPC.Server;

/// <summary>
/// 服务方法信息
/// </summary>
public class ServiceMethodInfo
{
    /// <summary>
    /// 方法ID
    /// </summary>
    public ushort MethodId { get; set; }

    /// <summary>
    /// 方法信息
    /// </summary>
    public MethodInfo Method { get; set; } = null!;

    /// <summary>
    /// 参数类型列表
    /// </summary>
    public Type[] ParameterTypes { get; set; } = Array.Empty<Type>();

    /// <summary>
    /// 返回值类型
    /// </summary>
    public Type? ReturnType { get; set; }

    /// <summary>
    /// 是否为异步方法
    /// </summary>
    public bool IsAsync { get; set; }
}

/// <summary>
/// 服务注册表
/// </summary>
public class ServiceRegistry
{
    private readonly ILogger<ServiceRegistry> _logger;
    private readonly IServiceProvider _serviceProvider;

    // 服务类型映射 - 类型名称 -> 实现类型
    private readonly ConcurrentDictionary<string, Type> _serviceTypes = new();

    // 服务方法映射 - 服务类型名称 -> 方法ID -> 方法信息
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<ushort, ServiceMethodInfo>> _serviceMethods = new();

    // 接收器类型映射 - 类型名称 -> 方法ID -> 方法信息
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<ushort, ServiceMethodInfo>> _receiverMethods = new();

    /// <summary>
    /// 创建服务注册表实例
    /// </summary>
    public ServiceRegistry(ILogger<ServiceRegistry> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// 从程序集中扫描并注册所有服务
    /// </summary>
    public void ScanAndRegisterServices(Assembly[] assemblies)
    {
        if (assemblies == null || assemblies.Length == 0)
        {
            assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && ShouldScanAssembly(a.GetName().Name!))
                .ToArray();
        }

        foreach (var assembly in assemblies)
        {
            try
            {
                ScanAssembly(assembly);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "扫描程序集 {Assembly} 时出错", assembly.GetName().Name);
            }
        }
    }

    private bool ShouldScanAssembly(string name)
    {
        // 跳过系统和第三方程序集
        return !name.StartsWith("System.", StringComparison.Ordinal) &&
               !name.StartsWith("Microsoft.", StringComparison.Ordinal) &&
               !name.StartsWith("JetBrains.", StringComparison.Ordinal) &&
               name != "netstandard" &&
               name != "mscorlib";
    }

    private void ScanAssembly(Assembly assembly)
    {
        foreach (var type in assembly.GetTypes())
        {
            // 检查是否为服务接口
            if (type.IsInterface && IsStreamingHubInterface(type))
            {
                RegisterStreamingHubInterface(type);
            }

            // 检查是否为接收器接口
            if (type.IsInterface && IsStreamingReceiverInterface(type))
            {
                RegisterReceiverInterface(type);
            }

            // 检查是否为服务实现
            if (!type.IsInterface && !type.IsAbstract && HasStreamingHubImplementation(type))
            {
                RegisterServiceImplementation(type);
            }
        }
    }

    private bool IsStreamingHubInterface(Type type)
    {
        return type.GetInterfaces().Any(i => i.IsGenericType &&
                                           i.GetGenericTypeDefinition() == typeof(IStreamingHub<>));
    }

    private bool IsStreamingReceiverInterface(Type type)
    {
        return type.GetInterfaces().Any(i => i.IsGenericType &&
                                           i.GetGenericTypeDefinition() == typeof(IStreamingReceiver<>));
    }

    private bool HasStreamingHubImplementation(Type type)
    {
        return type.GetInterfaces().Any(IsStreamingHubInterface);
    }

    private void RegisterStreamingHubInterface(Type serviceInterface)
    {
        var serviceName = serviceInterface.FullName!;

        // 注册服务方法
        var methodDict = _serviceMethods.GetOrAdd(serviceName, _ => new ConcurrentDictionary<ushort, ServiceMethodInfo>());

        foreach (var method in serviceInterface.GetMethods())
        {
            var attr = method.GetCustomAttribute<ServiceMethodAttribute>();
            if (attr == null)
            {
                // 自动生成方法ID
                var methodId = (ushort)FNV1A32.GetHashCode(serviceName + "." + method.Name);
                _logger.LogDebug("服务 {Service} 的方法 {Method} 没有指定MethodId，自动生成ID: {Id}", serviceName, method.Name, methodId);

                RegisterServiceMethod(methodDict, method, methodId);
            }
            else
            {
                RegisterServiceMethod(methodDict, method, attr.Id);
            }
        }

        _logger.LogInformation("已注册服务接口 {Service} 共 {Count} 个方法", serviceName, methodDict.Count);
    }

    private void RegisterReceiverInterface(Type receiverInterface)
    {
        var receiverName = receiverInterface.FullName!;

        // 注册接收器方法
        var methodDict = _receiverMethods.GetOrAdd(receiverName, _ => new ConcurrentDictionary<ushort, ServiceMethodInfo>());

        foreach (var method in receiverInterface.GetMethods())
        {
            var attr = method.GetCustomAttribute<ReceiverMethodAttribute>();
            if (attr == null)
            {
                // 自动生成方法ID
                var methodId = (ushort)FNV1A32.GetHashCode(receiverName + "." + method.Name);
                _logger.LogDebug("接收器 {Receiver} 的方法 {Method} 没有指定MethodId，自动生成ID: {Id}",
                    receiverName, method.Name, methodId);

                RegisterServiceMethod(methodDict, method, methodId);
            }
            else
            {
                RegisterServiceMethod(methodDict, method, attr.Id);
            }
        }

        _logger.LogInformation("已注册接收器接口 {Receiver} 共 {Count} 个方法", receiverName, methodDict.Count);
    }

    private void RegisterServiceImplementation(Type implementationType)
    {
        foreach (var serviceInterface in implementationType.GetInterfaces().Where(IsStreamingHubInterface))
        {
            var serviceName = serviceInterface.FullName!;
            _serviceTypes[serviceName] = implementationType;

            _logger.LogInformation("已注册服务实现 {Implementation} 对应接口 {Interface}", implementationType.FullName, serviceName);
        }
    }

    private void RegisterServiceMethod(ConcurrentDictionary<ushort, ServiceMethodInfo> methodDict, MethodInfo method, ushort methodId)
    {
        // 检查方法是否已注册
        if (methodDict.TryGetValue(methodId, out var existingMethod))
        {
            _logger.LogWarning("方法ID冲突：{ExistingMethod} 和 {NewMethod} 使用了相同的ID {Id}", existingMethod.Method.Name, method.Name, methodId);
            return;
        }

        // 获取参数类型
        var parameterTypes = method.GetParameters().Select(p => p.ParameterType).ToArray();

        // 检查返回类型
        var returnType = method.ReturnType;
        var isAsync = false;

        // 处理异步方法
        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            returnType = returnType.GetGenericArguments()[0];
            isAsync = true;
        }
        else if (returnType == typeof(Task))
        {
            returnType = null;
            isAsync = true;
        }

        var methodInfo = new ServiceMethodInfo
        {
            MethodId = methodId,
            Method = method,
            ParameterTypes = parameterTypes,
            ReturnType = returnType,
            IsAsync = isAsync
        };

        methodDict[methodId] = methodInfo;
    }

    /// <summary>
    /// 获取服务接口的所有方法
    /// </summary>
    public IReadOnlyDictionary<ushort, ServiceMethodInfo>? GetServiceMethods(string serviceTypeName)
    {
        if (_serviceMethods.TryGetValue(serviceTypeName, out var methods))
        {
            return methods;
        }
        return null;
    }

    /// <summary>
    /// 获取接收器接口的所有方法
    /// </summary>
    public IReadOnlyDictionary<ushort, ServiceMethodInfo>? GetReceiverMethods(string receiverTypeName)
    {
        if (_receiverMethods.TryGetValue(receiverTypeName, out var methods))
        {
            return methods;
        }
        return null;
    }

    /// <summary>
    /// 获取服务方法
    /// </summary>
    public ServiceMethodInfo? GetServiceMethod(string serviceTypeName, ushort methodId)
    {
        if (_serviceMethods.TryGetValue(serviceTypeName, out var methods) &&
            methods.TryGetValue(methodId, out var methodInfo))
        {
            return methodInfo;
        }
        return null;
    }

    /// <summary>
    /// 获取接收器方法
    /// </summary>
    public ServiceMethodInfo? GetReceiverMethod(string receiverTypeName, ushort methodId)
    {
        if (_receiverMethods.TryGetValue(receiverTypeName, out var methods) &&
            methods.TryGetValue(methodId, out var methodInfo))
        {
            return methodInfo;
        }
        return null;
    }

    /// <summary>
    /// 获取服务实例
    /// </summary>
    public object? GetServiceInstance(string serviceTypeName)
    {
        if (_serviceTypes.TryGetValue(serviceTypeName, out var implementationType))
        {
            return _serviceProvider.GetService(implementationType);
        }
        return null;
    }
}

/// <summary>
/// FNV-1a哈希函数实现，用于生成方法ID
/// </summary>
internal static class FNV1A32
{
    private const uint FnvPrime = 16777619;
    private const uint FnvOffsetBasis = 2166136261;

    /// <summary>
    /// 计算字符串的FNV-1a哈希值
    /// </summary>
    /// <param name="text">输入文本</param>
    /// <returns>哈希值</returns>
    public static int GetHashCode(string text)
    {
        uint hash = FnvOffsetBasis;

        foreach (var c in text)
        {
            hash ^= c;
            hash *= FnvPrime;
        }

        return (int)(hash & 0x7FFFFFFF);
    }
}
