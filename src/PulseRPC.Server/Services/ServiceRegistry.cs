using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using MemoryPack;

namespace PulseRPC.Server.Services;

/// <summary>
/// 高性能服务注册中心，优化从网络字节流到服务调用的全流程
/// </summary>
public class ServiceRegistry
{
    // 服务和实现的映射
    private readonly Dictionary<string, ServiceInfo> _services = new();
    private readonly Dictionary<Type, object> _serviceInstances = new();

    // 缓存方法调用委托，避免重复反射
    private readonly ConcurrentDictionary<string, Func<object, object[], object>> _methodCache = new();

    // 缓存参数反序列化委托
    private readonly ConcurrentDictionary<Type, Func<byte[], object>> _deserializerCache = new();

    // 缓存直接调用方法的委托（零拷贝模式）
    private readonly ConcurrentDictionary<string, Delegate> _directMethodCache = new();

    // 保持单例模式
    private static ServiceRegistry? _instance;
    private static readonly object _lock = new();

    public static ServiceRegistry Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new ServiceRegistry();
                }
            }
            return _instance;
        }
    }

    private ServiceRegistry() { }

    /// <summary>
    /// 注册服务
    /// </summary>
    public void RegisterService<TInterface, TImplementation>(TImplementation implementation)
        where TInterface : class, INetworkService
        where TImplementation : class, TInterface
    {
        var interfaceType = typeof(TInterface);
        var serviceName = interfaceType.FullName!;

        if (_services.ContainsKey(serviceName))
        {
            throw new InvalidOperationException($"服务已注册: {serviceName}");
        }

        // 创建服务信息
        var serviceInfo = CreateServiceInfo(interfaceType, implementation);

        // 保存服务信息和实例
        _services[serviceName] = serviceInfo;
        _serviceInstances[interfaceType] = implementation;

        // 预编译所有方法的调用委托
        foreach (var methodPair in serviceInfo.Methods)
        {
            var methodName = methodPair.Key;
            var methodInfo = methodPair.Value.MethodInfo;

            // 创建缓存键
            var cacheKey = $"{serviceName}.{methodName}";

            // 获取参数类型
            var parameterType = methodInfo.GetParameters()[0].ParameterType;

            // 缓存参数类型的反序列化委托
            _deserializerCache.TryAdd(parameterType, CreateDeserializerDelegate(parameterType));

            // ValueTask类型的方法通过直接调用处理，不创建表达式树委托
            if (IsValueTaskType(methodInfo.ReturnType))
            {
                // 缓存直接调用方法的委托
                TryAddDirectMethodDelegate(cacheKey, methodInfo, implementation.GetType(), parameterType);
            }
            else
            {
                // 对于Task和同步方法，使用表达式树委托
                _methodCache.TryAdd(cacheKey, CreateMethodDelegate(methodInfo));
            }
        }
    }

    /// <summary>
    /// 尝试添加直接调用方法的委托（零拷贝模式）
    /// </summary>
    private void TryAddDirectMethodDelegate(string cacheKey, MethodInfo methodInfo, Type implementationType, Type parameterType)
    {
        try
        {
            Console.WriteLine($"为方法创建直接调用委托: {cacheKey}, 返回类型: {methodInfo.ReturnType}");

            // 创建直接调用委托的方法
            if (methodInfo.ReturnType.IsGenericType &&
                methodInfo.ReturnType.GetGenericTypeDefinition() == typeof(ValueTask<>))
            {
                // 针对ValueTask<T>返回类型的特殊处理
                var returnType = methodInfo.ReturnType.GetGenericArguments()[0];
                Console.WriteLine($"  - ValueTask<T>返回类型: ValueTask<{returnType.Name}>");

                // 创建泛型委托类型
                var delegateType = typeof(Func<,,,>).MakeGenericType(
                    implementationType, typeof(byte[]), typeof(CancellationToken), methodInfo.ReturnType);

                Console.WriteLine($"  - 委托类型: {delegateType.Name}");

                try
                {
                    // 获取用于创建包装器的静态方法
                    var wrapperMethod = GetType().GetMethod(nameof(CreateValueTaskTResultWrapper),
                        BindingFlags.NonPublic | BindingFlags.Static);

                    if (wrapperMethod == null)
                    {
                        Console.WriteLine("  - 错误: 找不到CreateValueTaskTResultWrapper方法");
                        return;
                    }

                    // 创建泛型方法
                    var genericWrapperMethod = wrapperMethod.MakeGenericMethod(
                        implementationType, parameterType, returnType);

                    // 创建委托
                    var directDelegate = Delegate.CreateDelegate(delegateType, null, genericWrapperMethod);

                    // 添加到缓存
                    _directMethodCache.TryAdd(cacheKey, directDelegate);
                    Console.WriteLine($"  - 成功创建ValueTask<{returnType.Name}>的直接调用委托");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  - 创建ValueTask<T>委托失败: {ex.Message}");
                    throw;
                }
            }
            else if (methodInfo.ReturnType == typeof(ValueTask))
            {
                Console.WriteLine("  - 无返回值ValueTask类型");

                // 针对无返回值ValueTask类型
                var delegateType = typeof(Func<,,>).MakeGenericType(
                    implementationType, typeof(byte[]), typeof(CancellationToken), typeof(ValueTask));

                try
                {
                    // 获取用于创建包装器的静态方法
                    var wrapperMethod = GetType().GetMethod(nameof(CreateVoidValueTaskWrapper),
                        BindingFlags.NonPublic | BindingFlags.Static);

                    if (wrapperMethod == null)
                    {
                        Console.WriteLine("  - 错误: 找不到CreateVoidValueTaskWrapper方法");
                        return;
                    }

                    // 创建泛型方法
                    var genericWrapperMethod = wrapperMethod.MakeGenericMethod(implementationType, parameterType);

                    // 创建委托
                    var directDelegate = Delegate.CreateDelegate(delegateType, null, genericWrapperMethod);

                    // 添加到缓存
                    _directMethodCache.TryAdd(cacheKey, directDelegate);
                    Console.WriteLine("  - 成功创建ValueTask的直接调用委托");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  - 创建ValueTask委托失败: {ex.Message}");
                    throw;
                }
            }
        }
        catch (Exception ex)
        {
            // 如果创建直接委托失败，记录错误但继续执行
            Console.WriteLine($"无法为 {cacheKey} 创建直接调用委托: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }

    /// <summary>
    /// 创建ValueTask<TResult>方法的静态包装器
    /// </summary>
    private static ValueTask<TResult> CreateValueTaskTResultWrapper<TImpl, TParam, TResult>(
        TImpl implementation, byte[] data, CancellationToken cancellationToken)
        where TImpl : class
    {
        try
        {
            Console.WriteLine($"执行ValueTask<{typeof(TResult).Name}>方法包装器");

            // 使用MemoryPack直接反序列化
            var param = MemoryPackSerializer.Deserialize<TParam>(data)!;

            // 查找匹配的方法
            var methods = typeof(TImpl).GetMethods().Where(m =>
                    m.GetParameters().Length > 0 &&
                    m.GetParameters()[0].ParameterType == typeof(TParam) &&
                    m.ReturnType.IsGenericType &&
                    m.ReturnType.GetGenericTypeDefinition() == typeof(ValueTask<>) &&
                    m.ReturnType.GetGenericArguments()[0] == typeof(TResult))
                .ToList();

            if (methods.Count == 0)
            {
                throw new InvalidOperationException(
                    $"找不到匹配的方法: {typeof(TImpl).Name} 返回 ValueTask<{typeof(TResult).Name}>, 参数: {typeof(TParam).Name}");
            }

            if (methods.Count > 1)
            {
                Console.WriteLine($"警告: 找到多个匹配的方法，使用第一个: {methods[0].Name}");
            }

            var method = methods[0];

            // 调用方法
            if (method.GetParameters().Length > 1 &&
                method.GetParameters()[1].ParameterType == typeof(CancellationToken))
            {
                return (ValueTask<TResult>)method.Invoke(implementation, new object[] { param, cancellationToken })!;
            }
            else
            {
                return (ValueTask<TResult>)method.Invoke(implementation, new object[] { param })!;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ValueTask<{typeof(TResult).Name}>包装器执行失败: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            throw;
        }
    }

    /// <summary>
    /// 创建无返回值的ValueTask方法包装器
    /// </summary>
    private static ValueTask CreateVoidValueTaskWrapper<TImpl, TParam>(
        TImpl implementation, byte[] data, CancellationToken cancellationToken)
        where TImpl : class
    {
        try
        {
            Console.WriteLine("执行ValueTask方法包装器");

            // 使用MemoryPack直接反序列化
            var param = MemoryPackSerializer.Deserialize<TParam>(data)!;

            // 查找匹配的方法
            var methods = typeof(TImpl).GetMethods().Where(m =>
                    m.GetParameters().Length > 0 &&
                    m.GetParameters()[0].ParameterType == typeof(TParam) &&
                    m.ReturnType == typeof(ValueTask))
                .ToList();

            if (methods.Count == 0)
            {
                throw new InvalidOperationException(
                    $"找不到匹配的方法: {typeof(TImpl).Name} 返回 ValueTask, 参数: {typeof(TParam).Name}");
            }

            if (methods.Count > 1)
            {
                Console.WriteLine($"警告: 找到多个匹配的方法，使用第一个: {methods[0].Name}");
            }

            var method = methods[0];

            // 调用方法
            if (method.GetParameters().Length > 1 &&
                method.GetParameters()[1].ParameterType == typeof(CancellationToken))
            {
                return (ValueTask)method.Invoke(implementation, new object[] { param, cancellationToken })!;
            }
            else
            {
                return (ValueTask)method.Invoke(implementation, new object[] { param })!;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ValueTask包装器执行失败: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            throw;
        }
    }

    /// <summary>
    /// 创建方法调用委托
    /// </summary>
    private static Func<object, object[], object> CreateMethodDelegate(MethodInfo methodInfo)
    {
        // 创建参数表达式
        var instanceParam = Expression.Parameter(typeof(object), "instance");
        var argumentsParam = Expression.Parameter(typeof(object[]), "arguments");

        // 创建方法调用表达式
        var instance = Expression.Convert(instanceParam, methodInfo.DeclaringType!);

        var parameters = methodInfo.GetParameters();
        var arguments = new Expression[parameters.Length];

        for (int i = 0; i < parameters.Length; i++)
        {
            var parameterType = parameters[i].ParameterType;
            var argument = Expression.ArrayIndex(argumentsParam, Expression.Constant(i));
            arguments[i] = Expression.Convert(argument, parameterType);
        }

        var call = Expression.Call(instance, methodInfo, arguments);

        // 处理返回值
        Expression resultExpression;

        if (methodInfo.ReturnType == typeof(void))
        {
            resultExpression = Expression.Block(call, Expression.Constant(null));
        }
        else if (typeof(Task).IsAssignableFrom(methodInfo.ReturnType))
        {
            // 异步方法返回Task
            resultExpression = call;
        }
        else if (IsValueTaskType(methodInfo.ReturnType))
        {
            // 对于ValueTask或ValueTask<T>，我们需要特殊处理
            // 创建一个匿名方法，将ValueTask包装为Task
            if (methodInfo.ReturnType.IsGenericType)
            {
                // 对于ValueTask<T>，使用AsTask()转换
                var asTaskMethod = methodInfo.ReturnType.GetMethod("AsTask");
                resultExpression = Expression.Call(call, asTaskMethod!);
            }
            else
            {
                // 对于ValueTask，使用AsTask()转换
                var asTaskMethod = methodInfo.ReturnType.GetMethod("AsTask");
                resultExpression = Expression.Call(call, asTaskMethod!);
            }
        }
        else
        {
            // 同步方法，装箱返回值
            resultExpression = Expression.Convert(call, typeof(object));
        }

        // 创建Lambda表达式
        var lambdaType = typeof(Func<object, object[], object>);
        var lambda = Expression.Lambda(lambdaType, resultExpression, instanceParam, argumentsParam);

        return (Func<object, object[], object>)lambda.Compile();
    }

    /// <summary>
    /// 创建反序列化委托
    /// </summary>
    private static Func<byte[], object> CreateDeserializerDelegate(Type type)
    {
        try
        {
            // 创建参数表达式
            var dataParam = Expression.Parameter(typeof(byte[]), "data");

            // 获取MemoryPackSerializer.Deserialize<T>方法
            var deserializeMethod = typeof(MemoryPackSerializer)
                .GetMethod(nameof(MemoryPackSerializer.Deserialize),
                    new Type[] { typeof(ReadOnlySpan<byte>) })!
                .MakeGenericMethod(type);

            if (deserializeMethod == null)
            {
                throw new InvalidOperationException($"无法获取MemoryPackSerializer.Deserialize<{type.Name}>方法");
            }

            // 创建ReadOnlySpan<byte>构造函数调用
            var readOnlySpanCtor = typeof(ReadOnlySpan<byte>).GetConstructor(new[] { typeof(byte[]) });
            if (readOnlySpanCtor == null)
            {
                throw new InvalidOperationException("无法获取ReadOnlySpan<byte>构造函数");
            }

            // 创建方法调用表达式
            var spanExpr = Expression.New(readOnlySpanCtor, dataParam);

            var call = Expression.Call(null, deserializeMethod, spanExpr);

            // 将结果转换为object
            var resultExpr = Expression.Convert(call, typeof(object));

            // 编译Lambda表达式
            var lambda = Expression.Lambda<Func<byte[], object>>(resultExpr, dataParam);

            return lambda.Compile();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"为类型 {type.Name} 创建反序列化委托失败: {ex.Message}");
            Console.WriteLine(ex.StackTrace);

            // 返回一个基本的反序列化函数，尽管不是最高效的
            return (data) =>
            {
                try
                {
                    // 使用直接调用方式
                    var method = typeof(MemoryPackSerializer)
                        .GetMethod(nameof(MemoryPackSerializer.Deserialize),
                            new Type[] { typeof(byte[]) })!
                        .MakeGenericMethod(type);

                    return method.Invoke(null, new object[] { data })!;
                }
                catch
                {
                    // 如果反序列化失败，返回该类型的默认值
                    return type.IsValueType ? Activator.CreateInstance(type)! : null!;
                }
            };
        }
    }

    /// <summary>
    /// 使用零拷贝方式高性能调用服务方法
    /// </summary>
    public async Task<object?> InvokeMethodAsync(string serviceName, string methodName, byte[] requestBytes,
        CancellationToken cancellationToken = default)
    {
        // 查找服务
        if (!_services.TryGetValue(serviceName, out var serviceInfo))
            throw new InvalidOperationException($"服务未找到: {serviceName}");

        // 查找方法
        if (!serviceInfo.Methods.TryGetValue(methodName, out var methodInfo))
            throw new InvalidOperationException($"方法未找到: {methodName} in service {serviceName}");

        // 获取实例
        var instance = serviceInfo.Implementation;

        // 获取缓存键
        var cacheKey = $"{serviceName}.{methodName}";

        try
        {
            // 首先尝试使用直接调用委托（对ValueTask类型特别有效）
            if (_directMethodCache.TryGetValue(cacheKey, out var directDelegate))
            {
                try
                {
                    // 使用直接委托调用
                    dynamic dynDelegate = directDelegate;
                    dynamic dynInstance = instance;
                    dynamic result = dynDelegate(dynInstance, requestBytes, cancellationToken);

                    // 等待ValueTask结果
                    await result;

                    // 获取结果值
                    return GetValueTaskResult(result);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"直接调用失败: {ex.Message}，回退到标准调用");
                }
            }

            // 检查方法返回类型是否为ValueTask
            if (IsValueTaskType(methodInfo.MethodInfo.ReturnType))
            {
                // ValueTask方法应该使用直接调用，如果没有直接委托则无法继续
                throw new InvalidOperationException(
                    $"无法调用ValueTask返回类型的方法: {methodName}，缺少直接调用委托");
            }

            // 标准调用路径（适用于Task和同步方法）
            if (!_methodCache.TryGetValue(cacheKey, out var methodDelegate))
            {
                // 动态创建委托
                methodDelegate = _methodCache.GetOrAdd(cacheKey, key => CreateMethodDelegate(methodInfo.MethodInfo));
            }

            // 获取参数类型
            var parameterType = methodInfo.MethodInfo.GetParameters()[0].ParameterType;

            // 获取缓存的反序列化委托
            var deserializer = _deserializerCache.GetOrAdd(parameterType, type => CreateDeserializerDelegate(type));

            // 反序列化请求对象
            var request = deserializer(requestBytes);

            // 准备参数
            object[] parameters;
            if (methodInfo.MethodInfo.GetParameters().Length > 1 &&
                methodInfo.MethodInfo.GetParameters()[1].ParameterType == typeof(CancellationToken))
            {
                parameters = [request, cancellationToken];
            }
            else
            {
                parameters = [request];
            }

            // 调用方法
            var methodResult = methodDelegate(instance, parameters);

            // 处理异步结果
            if (methodResult is Task task)
            {
                await task;

                // 检查是否有返回值
                var resultProperty = task.GetType().GetProperty("Result");
                return resultProperty != null ? resultProperty.GetValue(task) : null;
            }

            return methodResult;
        }
        catch (TargetInvocationException ex)
        {
            // 如果是目标调用异常，提取内部异常
            throw ex.InnerException ?? ex;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"执行方法异常: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 获取ValueTask的结果值
    /// </summary>
    private static object? GetValueTaskResult(object valueTask)
    {
        var type = valueTask.GetType();

        // 检查是否是ValueTask<T>
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            // 使用反射获取Result属性
            var resultProperty = type.GetProperty("Result");
            if (resultProperty != null)
            {
                return resultProperty.GetValue(valueTask);
            }
        }

        // 如果是普通ValueTask或无法获取结果，返回null
        return null;
    }

    /// <summary>
    /// 处理ValueTask返回类型
    /// </summary>
    private async Task<object?> HandleValueTaskAsync(object valueTask, Type valueTaskType)
    {
        if (!valueTaskType.IsGenericType)
        {
            // 普通ValueTask，无返回值
            var asTaskMethod = valueTaskType.GetMethod("AsTask");
            if (asTaskMethod != null)
            {
                var task = (Task)asTaskMethod.Invoke(valueTask, null)!;
                await task;
                return null;
            }
        }
        else
        {
            // ValueTask<T>，有返回值
            var resultType = valueTaskType.GetGenericArguments()[0];

            // 创建一个能接收所有类型的通用处理方法
            var method = GetType().GetMethod(nameof(HandleValueTaskWithResultAsync),
                BindingFlags.NonPublic | BindingFlags.Instance)!.MakeGenericMethod(resultType);

            return await (Task<object?>)method.Invoke(this, [valueTask])!;
        }

        return null;
    }

    /// <summary>
    /// 处理带返回值的ValueTask
    /// </summary>
    private async Task<object?> HandleValueTaskWithResultAsync<T>(ValueTask<T> valueTask)
    {
        var result = await valueTask;
        return result;
    }

    /// <summary>
    /// 直接调用已知服务和方法 - 高效路径
    /// </summary>
    public async Task<TResponse> DirectInvokeAsync<TRequest, TResponse>(
        INetworkService service, string methodName, byte[] requestBytes)
    {
        // 反序列化请求
        var request = MemoryPackSerializer.Deserialize<TRequest>(requestBytes)!;

        // 动态调用方法
        var method = service.GetType().GetMethod(methodName);
        if (method == null)
        {
            throw new InvalidOperationException($"方法未找到: {methodName}");
        }

        var result = method.Invoke(service, [request]);

        // 处理返回值
        if (result is ValueTask<TResponse> valueTask)
        {
            return await valueTask;
        }

        throw new InvalidOperationException($"返回类型不匹配: {result?.GetType().Name}");
    }

    /// <summary>
    /// 获取服务的通道名称
    /// </summary>
    public string GetServiceChannel(string serviceName, string methodName)
    {
        if (!_services.TryGetValue(serviceName, out var serviceInfo))
            throw new InvalidOperationException($"服务未找到: {serviceName}");

        // 查找方法
        if (!serviceInfo.Methods.TryGetValue(methodName, out var methodInfo))
            throw new InvalidOperationException($"方法未找到: {methodName} in service {serviceName}");

        // 如果方法有自定义通道，则使用方法的通道
        return methodInfo.Channel ?? serviceInfo.DefaultChannel;
    }

    /// <summary>
    /// 创建服务信息
    /// </summary>
    private ServiceInfo CreateServiceInfo(Type interfaceType, object implementation)
    {
        var methods = new Dictionary<string, MethodInfo2>();
        var defaultChannel = GetChannelName(interfaceType) ?? "DefaultChannel";

        // 获取所有操作方法
        foreach (var method in interfaceType.GetMethods())
        {
            // 检查是否有Operation特性
            if (method.GetCustomAttribute<OperationAttribute>() != null)
            {
                // 获取方法通道
                var methodChannel = GetChannelName(method);

                // 添加方法信息
                methods.Add(method.Name, new MethodInfo2
                {
                    MethodInfo = method,
                    Channel = methodChannel
                });
            }
        }

        return new ServiceInfo
        {
            Implementation = implementation,
            DefaultChannel = defaultChannel,
            Methods = methods
        };
    }

    /// <summary>
    /// 获取通道名称
    /// </summary>
    private string? GetChannelName(MemberInfo member)
    {
        var channelAttr = member.GetCustomAttribute<ChannelAttribute>();
        return channelAttr?.ChannelName;
    }

    /// <summary>
    /// 检查是否是ValueTask类型
    /// </summary>
    private static bool IsValueTaskType(Type type)
    {
        return type == typeof(ValueTask) ||
               (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValueTask<>));
    }

    private class ServiceInfo
    {
        public required object Implementation { get; init; }
        public required string DefaultChannel { get; init; }
        public required Dictionary<string, MethodInfo2> Methods { get; init; }
    }

    private class MethodInfo2
    {
        public required MethodInfo MethodInfo { get; init; }
        public required string? Channel { get; init; }
    }
}
