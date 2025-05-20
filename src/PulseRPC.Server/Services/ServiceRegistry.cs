// PulseRPC.Server/Services/ServiceRegistry.cs

using System.Reflection;

namespace PulseRPC.Server;

/// <summary>
/// 服务注册中心
/// </summary>
public class ServiceRegistry
{
    private readonly Dictionary<string, ServiceInfo> _services = new();
    private readonly Dictionary<Type, object> _serviceInstances = new();

    /// <summary>
    /// 注册服务
    /// </summary>
    public void RegisterService<TInterface, TImplementation>(TImplementation implementation)
        where TInterface : class, INetworkService
        where TImplementation : class, TInterface
    {
        Type interfaceType = typeof(TInterface);
        string serviceName = interfaceType.FullName!;

        if (_services.ContainsKey(serviceName))
            throw new InvalidOperationException($"Service already registered: {serviceName}");

        // 创建服务信息
        var serviceInfo = CreateServiceInfo(interfaceType, implementation);

        // 保存服务信息和实例
        _services[serviceName] = serviceInfo;
        _serviceInstances[interfaceType] = implementation;
    }

    /// <summary>
    /// 调用服务方法
    /// </summary>
    public async Task<object?> InvokeMethodAsync(string serviceName, string methodName, object request, CancellationToken cancellationToken)
    {
        // 查找服务
        if (!_services.TryGetValue(serviceName, out var serviceInfo))
            throw new InvalidOperationException($"Service not found: {serviceName}");

        // 查找方法
        if (!serviceInfo.Methods.TryGetValue(methodName, out var methodInfo))
            throw new InvalidOperationException($"Method not found: {methodName} in service {serviceName}");

        try
        {
            // 准备参数
            var parameters = new object[] { request, cancellationToken };

            // 调用方法
            var result = methodInfo.MethodInfo.Invoke(serviceInfo.Implementation, parameters);

            // 处理异步结果
            if (result is Task task)
            {
                await task;

                // 检查是否有返回值
                var resultProperty = task.GetType().GetProperty("Result");
                if (resultProperty != null)
                {
                    return resultProperty.GetValue(task);
                }

                return null;
            }
            else if (result is ValueTask valueTask)
            {
                await valueTask;

                // 检查是否有返回值
                var taskType = valueTask.GetType();
                var taskAwaiter = taskType.GetMethod("GetAwaiter")!.Invoke(valueTask, null);
                var taskResult = taskAwaiter!.GetType().GetMethod("GetResult")!.Invoke(taskAwaiter, null);

                return taskResult;
            }

            return result;
        }
        catch (TargetInvocationException ex)
        {
            // 展开反射异常
            throw ex.InnerException!;
        }
    }

    /// <summary>
    /// 获取服务执行通道
    /// </summary>
    public string GetServiceChannel(string serviceName, string methodName)
    {
        // 查找服务
        if (!_services.TryGetValue(serviceName, out var serviceInfo))
            return "default";

        // 查找方法
        if (!serviceInfo.Methods.TryGetValue(methodName, out var methodInfo))
            return serviceInfo.DefaultChannel;

        return methodInfo.Channel ?? serviceInfo.DefaultChannel;
    }

    /// <summary>
    /// 创建服务信息
    /// </summary>
    private ServiceInfo CreateServiceInfo(Type interfaceType, object implementation)
    {
        var serviceInfo = new ServiceInfo
        {
            Implementation = implementation,
            DefaultChannel = GetChannelName(interfaceType) ?? "default",
            Methods = new Dictionary<string, MethodInfo2>()
        };

        // 查找所有标记了Operation特性的方法
        var methods = interfaceType.GetMethods()
            .Where(m => m.GetCustomAttribute<OperationAttribute>() != null);

        foreach (var method in methods)
        {
            // 检查参数
            var parameters = method.GetParameters();
            if (parameters.Length < 1 || parameters.Length > 2)
                continue;

            // 检查返回类型
            var returnType = method.ReturnType;
            if (!typeof(Task).IsAssignableFrom(returnType) &&
                !IsValueTaskType(returnType))
                continue;

            // 获取方法通道
            string channelName = GetChannelName(method) ?? serviceInfo.DefaultChannel;

            // 创建方法信息
            serviceInfo.Methods[method.Name] = new MethodInfo2
            {
                MethodInfo = method,
                Channel = channelName
            };
        }

        return serviceInfo;
    }

    /// <summary>
    /// 获取通道名称
    /// </summary>
    private string? GetChannelName(MemberInfo member)
    {
        var attribute = member.GetCustomAttribute<ChannelAttribute>();
        return attribute?.ChannelName;
    }

    /// <summary>
    /// 检查是否为ValueTask类型
    /// </summary>
    private bool IsValueTaskType(Type type)
    {
        return type.FullName == "System.Threading.Tasks.ValueTask" ||
               (type.IsGenericType && type.GetGenericTypeDefinition().FullName == "System.Threading.Tasks.ValueTask`1");
    }

    // 内部类型
    private class ServiceInfo
    {
        public required object Implementation { get; init; }
        public required string DefaultChannel { get; init; }
        public required Dictionary<string, MethodInfo2> Methods { get; init; }
    }

    private class MethodInfo2
    {
        public required System.Reflection.MethodInfo MethodInfo { get; init; }
        public required string Channel { get; init; }
    }
}
