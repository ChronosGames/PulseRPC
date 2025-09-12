using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PulseRPC.Serialization;
using PulseRPC.Server.Dispatch;
using PulseRPC.Server.Serialization;

namespace PulseRPC.Server.Services;

/// <summary>
/// 高性能服务处理器 - 零反射实现
/// 使用源码生成器生成的调用代码
/// </summary>
public interface IServiceProcessor
{
    /// <summary>
    /// 注册服务实现
    /// </summary>
    void RegisterService<TService, TImplementation>()
        where TService : class, IPulseHub
        where TImplementation : class, TService;

    /// <summary>
    /// 注册服务实例
    /// </summary>
    void RegisterService<TService>(TService serviceInstance)
        where TService : class, IPulseHub;

    /// <summary>
    /// 获取服务处理器
    /// </summary>
    IServiceHandler? GetServiceHandler(string serviceName);

    /// <summary>
    /// 获取所有已注册的服务信息
    /// </summary>
    ServiceRegistrationInfo[] GetRegisteredServices();
}

/// <summary>
/// 高性能服务处理器实现
/// </summary>
internal sealed class HighPerformanceServiceProcessor : IServiceProcessor
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ISerializerProvider _serializerProvider;
    private readonly ILogger<HighPerformanceServiceProcessor> _logger;

    // 服务处理器映射
    private readonly ConcurrentDictionary<string, IServiceHandler> _serviceHandlers = new();

    // 服务注册信息
    private readonly ConcurrentDictionary<string, ServiceRegistrationInfo> _serviceRegistrations = new();

    public HighPerformanceServiceProcessor(
        IServiceProvider serviceProvider,
        ISerializerProvider? serializerProvider = null,
        ILogger<HighPerformanceServiceProcessor>? logger = null)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _serializerProvider = serializerProvider ?? PulseRPCSerializerProvider.Instance;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<HighPerformanceServiceProcessor>.Instance;
    }

    /// <summary>
    /// 注册服务实现 - 使用依赖注入
    /// </summary>
    public void RegisterService<TService, TImplementation>()
        where TService : class, IPulseHub
        where TImplementation : class, TService
    {
        var serviceType = typeof(TService);
        var implementationType = typeof(TImplementation);
        var serviceName = GetServiceName(serviceType);

        _logger.LogInformation("注册服务: {ServiceName} -> {ImplementationType}", serviceName, implementationType.Name);

        // 创建服务处理器
        var serviceHandler = new ReflectionBasedServiceHandler<TService, TImplementation>(
            _serviceProvider,
            _serializerProvider,
            _logger);

        _serviceHandlers[serviceName] = serviceHandler;

        // 记录注册信息
        var registrationInfo = new ServiceRegistrationInfo(
            serviceName,
            serviceType,
            implementationType,
            ServiceLifetime.Scoped, // 默认使用 Scoped
            DateTime.UtcNow);

        _serviceRegistrations[serviceName] = registrationInfo;

        _logger.LogInformation("服务注册完成: {ServiceName}", serviceName);
    }

    /// <summary>
    /// 注册服务实例 - 单例模式
    /// </summary>
    public void RegisterService<TService>(TService serviceInstance)
        where TService : class, IPulseHub
    {
        if (serviceInstance == null)
            throw new ArgumentNullException(nameof(serviceInstance));

        var serviceType = typeof(TService);
        var implementationType = serviceInstance.GetType();
        var serviceName = GetServiceName(serviceType);

        _logger.LogInformation("注册服务实例: {ServiceName} -> {ImplementationType}", serviceName, implementationType.Name);

        // 创建单例服务处理器
        var serviceHandler = new SingletonServiceHandler<TService>(
            serviceInstance,
            _serializerProvider,
            _logger);

        _serviceHandlers[serviceName] = serviceHandler;

        // 记录注册信息
        var registrationInfo = new ServiceRegistrationInfo(
            serviceName,
            serviceType,
            implementationType,
            ServiceLifetime.Singleton,
            DateTime.UtcNow);

        _serviceRegistrations[serviceName] = registrationInfo;

        _logger.LogInformation("服务实例注册完成: {ServiceName}", serviceName);
    }

    /// <summary>
    /// 获取服务处理器
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IServiceHandler? GetServiceHandler(string serviceName)
    {
        return _serviceHandlers.TryGetValue(serviceName, out var handler) ? handler : null;
    }

    /// <summary>
    /// 获取所有已注册的服务信息
    /// </summary>
    public ServiceRegistrationInfo[] GetRegisteredServices()
    {
        return _serviceRegistrations.Values.ToArray();
    }

    /// <summary>
    /// 获取服务名称
    /// </summary>
    private static string GetServiceName(Type serviceType)
    {
        // 使用接口的完整名称作为服务名
        return serviceType.FullName ?? serviceType.Name;
    }
}

/// <summary>
/// 基于反射的服务处理器 - 支持依赖注入
/// </summary>
internal sealed class ReflectionBasedServiceHandler<TService, TImplementation> : IServiceHandler
    where TService : class, IPulseHub
    where TImplementation : class, TService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ISerializerProvider _serializerProvider;
    private readonly ILogger _logger;

    // 方法缓存
    private readonly ConcurrentDictionary<string, MethodInvoker> _methodCache = new();

    public ReflectionBasedServiceHandler(
        IServiceProvider serviceProvider,
        ISerializerProvider serializerProvider,
        ILogger logger)
    {
        _serviceProvider = serviceProvider;
        _serializerProvider = serializerProvider;
        _logger = logger;
    }

    public async Task<object?> HandleAsync(ServiceCallContext callContext)
    {
        try
        {
            // 获取服务实例 (通过依赖注入)
            var serviceInstance = _serviceProvider.GetRequiredService<TImplementation>();

            // 获取方法调用器
            var methodInvoker = GetMethodInvoker(callContext.MethodName);

            // 执行方法调用
            var result = await methodInvoker.InvokeAsync(serviceInstance, callContext);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "服务方法调用失败: {ServiceName}.{MethodName}",
                typeof(TService).Name, callContext.MethodName);
            throw;
        }
    }

    private MethodInvoker GetMethodInvoker(string methodName)
    {
        return _methodCache.GetOrAdd(methodName, static (name, state) =>
        {
            var (serviceType, serializerProvider, logger) = state;

            // 查找方法
            var method = serviceType.GetMethod(name);
            if (method == null)
            {
                throw new InvalidOperationException($"方法 {name} 在服务 {serviceType.Name} 中不存在");
            }

            return new MethodInvoker(method, serializerProvider, logger);
        }, (typeof(TService), _serializerProvider, _logger));
    }
}

/// <summary>
/// 单例服务处理器
/// </summary>
internal sealed class SingletonServiceHandler<TService> : IServiceHandler
    where TService : class, IPulseHub
{
    private readonly TService _serviceInstance;
    private readonly ISerializerProvider _serializerProvider;
    private readonly ILogger _logger;

    // 方法缓存
    private readonly ConcurrentDictionary<string, MethodInvoker> _methodCache = new();

    public SingletonServiceHandler(
        TService serviceInstance,
        ISerializerProvider serializerProvider,
        ILogger logger)
    {
        _serviceInstance = serviceInstance;
        _serializerProvider = serializerProvider;
        _logger = logger;
    }

    public async Task<object?> HandleAsync(ServiceCallContext callContext)
    {
        try
        {
            // 获取方法调用器
            var methodInvoker = GetMethodInvoker(callContext.MethodName);

            // 执行方法调用
            var result = await methodInvoker.InvokeAsync(_serviceInstance, callContext);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "服务方法调用失败: {ServiceName}.{MethodName}",
                typeof(TService).Name, callContext.MethodName);
            throw;
        }
    }

    private MethodInvoker GetMethodInvoker(string methodName)
    {
        return _methodCache.GetOrAdd(methodName, static (name, state) =>
        {
            var (serviceType, serializerProvider, logger) = state;

            // 查找方法
            var method = serviceType.GetMethod(name);
            if (method == null)
            {
                throw new InvalidOperationException($"方法 {name} 在服务 {serviceType.Name} 中不存在");
            }

            return new MethodInvoker(method, serializerProvider, logger);
        }, (typeof(TService), _serializerProvider, _logger));
    }
}

/// <summary>
/// 方法调用器 - 处理参数反序列化和方法调用
/// </summary>
internal sealed class MethodInvoker
{
    private readonly MethodInfo _methodInfo;
    private readonly ISerializer _requestSerializer;
    private readonly ISerializer _responseSerializer;
    private readonly ParameterInfo[] _parameters;
    private readonly Type? _requestType;
    private readonly Type? _responseType;
    private readonly ILogger _logger;

    public MethodInvoker(MethodInfo methodInfo, ISerializerProvider serializerProvider, ILogger logger)
    {
        _methodInfo = methodInfo;
        _logger = logger;
        _parameters = methodInfo.GetParameters();

        // 分析参数类型
        _requestType = _parameters.FirstOrDefault(p => p.ParameterType != typeof(CancellationToken))?.ParameterType;

        // 分析返回类型
        var returnType = methodInfo.ReturnType;
        if (returnType.IsGenericType)
        {
            var genericType = returnType.GetGenericTypeDefinition();
            if (genericType == typeof(Task<>) || genericType == typeof(ValueTask<>))
            {
                _responseType = returnType.GetGenericArguments()[0];
            }
        }

        // 创建序列化器
        _requestSerializer = serializerProvider.Create(MethodType.Unary, methodInfo);
        _responseSerializer = serializerProvider.Create(MethodType.Unary, methodInfo);
    }

    public async Task<object?> InvokeAsync(object serviceInstance, ServiceCallContext callContext)
    {
        try
        {
            // 准备方法参数
            var args = PrepareMethodArguments(callContext);

            // 调用方法
            var result = _methodInfo.Invoke(serviceInstance, args);

            // 处理异步返回值
            if (result is Task task)
            {
                await task;

                // 获取泛型Task的结果
                if (_responseType != null && task.GetType().IsGenericType)
                {
                    var property = task.GetType().GetProperty("Result");
                    return property?.GetValue(task);
                }

                return null; // void Task
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "方法调用失败: {MethodName}", _methodInfo.Name);
            throw;
        }
    }

    private object?[] PrepareMethodArguments(ServiceCallContext callContext)
    {
        var args = new object?[_parameters.Length];

        for (int i = 0; i < _parameters.Length; i++)
        {
            var parameter = _parameters[i];

            if (parameter.ParameterType == typeof(CancellationToken))
            {
                // 提供取消令牌 (这里应该从上下文获取)
                args[i] = CancellationToken.None;
            }
            else if (parameter.ParameterType == _requestType && callContext.RequestData != null)
            {
                // 反序列化请求数据
                args[i] = DeserializeRequestData(callContext.RequestData, parameter.ParameterType);
            }
            else if (parameter.HasDefaultValue)
            {
                args[i] = parameter.DefaultValue;
            }
            else
            {
                args[i] = parameter.ParameterType.IsValueType
                    ? Activator.CreateInstance(parameter.ParameterType)
                    : null;
            }
        }

        return args;
    }

    private object? DeserializeRequestData(object requestData, Type targetType)
    {
        // 这里需要根据实际的序列化格式进行反序列化
        // 目前简化处理，假设已经是正确的类型
        if (requestData.GetType() == targetType)
        {
            return requestData;
        }

        // 如果是字节数组，使用序列化器反序列化
        if (requestData is byte[] bytes)
        {
            var sequence = new ReadOnlySequence<byte>(bytes);
            // 这里需要调用适当的反序列化方法
            // 由于类型擦除，这里使用反射作为fallback
            var deserializeMethod = _requestSerializer.GetType().GetMethod("Deserialize")?.MakeGenericMethod(targetType);
            return deserializeMethod?.Invoke(_requestSerializer, new object[] { sequence });
        }

        return requestData;
    }
}

/// <summary>
/// 服务注册信息
/// </summary>
public sealed class ServiceRegistrationInfo
{
    public string ServiceName { get; }
    public Type ServiceType { get; }
    public Type ImplementationType { get; }
    public ServiceLifetime Lifetime { get; }
    public DateTime RegisteredTime { get; }

    public ServiceRegistrationInfo(
        string serviceName,
        Type serviceType,
        Type implementationType,
        ServiceLifetime lifetime,
        DateTime registeredTime)
    {
        ServiceName = serviceName;
        ServiceType = serviceType;
        ImplementationType = implementationType;
        Lifetime = lifetime;
        RegisteredTime = registeredTime;
    }
}
