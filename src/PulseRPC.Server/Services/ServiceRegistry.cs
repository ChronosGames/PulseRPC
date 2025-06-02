using System.Buffers;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using MemoryPack;
using PulseRPC.Transport;
using PulseRPC.Server.Authentication;
using PulseRPC.Server.Transport;
using PulseRPC.Messaging;
using PulseRPC.Serialization;
using Microsoft.Extensions.Logging;

namespace PulseRPC.Server.Services;

/// <summary>
/// 高性能服务注册中心，优化从网络字节流到服务调用的全流程
/// </summary>
public partial class ServiceRegistry
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

    // 认证中间件
    private readonly AuthenticationMiddleware? _authMiddleware;

    // 添加RPC消息处理相关字段
    private readonly IServerChannelManager? _channelManager;
    private readonly ISerializerProvider? _serializerProvider;
    private readonly ILogger<ServiceRegistry>? _logger;
    private bool _rpcHandlingEnabled = false;

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

    private ServiceRegistry(AuthenticationMiddleware? authMiddleware = null,
        IServerChannelManager? channelManager = null,
        ISerializerProvider? serializerProvider = null,
        ILogger<ServiceRegistry>? logger = null)
    {
        _authMiddleware = authMiddleware;
        _channelManager = channelManager;
        _serializerProvider = serializerProvider;
        _logger = logger;
    }

    /// <summary>
    /// 创建带认证中间件的服务注册中心实例
    /// </summary>
    /// <param name="authMiddleware">认证中间件</param>
    /// <returns>服务注册中心实例</returns>
    public static ServiceRegistry CreateWithAuth(AuthenticationMiddleware authMiddleware)
    {
        return new ServiceRegistry(authMiddleware);
    }

    /// <summary>
    /// 创建带完整RPC处理能力的服务注册中心实例
    /// </summary>
    /// <param name="authMiddleware">认证中间件</param>
    /// <param name="channelManager">通道管理器</param>
    /// <param name="serializerProvider">序列化器提供程序</param>
    /// <param name="logger">日志记录器</param>
    /// <returns>服务注册中心实例</returns>
    public static ServiceRegistry CreateWithRpcHandling(
        AuthenticationMiddleware authMiddleware,
        IServerChannelManager channelManager,
        ISerializerProvider serializerProvider,
        ILogger<ServiceRegistry> logger)
    {
        var registry = new ServiceRegistry(authMiddleware, channelManager, serializerProvider, logger);
        registry.EnableRpcMessageHandling();
        return registry;
    }

    /// <summary>
    /// 启用RPC消息处理
    /// </summary>
    public void EnableRpcMessageHandling()
    {
        if (_rpcHandlingEnabled || _channelManager == null || _serializerProvider == null)
            return;

        _channelManager.ChannelConnected += OnChannelConnected;
        _channelManager.ChannelDisconnected += OnChannelDisconnected;
        _rpcHandlingEnabled = true;

        _logger?.LogInformation("ServiceRegistry RPC消息处理已启用");
    }

    /// <summary>
    /// 注册服务
    /// </summary>
    public void RegisterService<TInterface, TImplementation>(TImplementation implementation)
        where TInterface : class, IPulseHub
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

                if (!TryCreateValueTaskTResultDelegate(cacheKey, implementationType, parameterType, returnType))
                {
                    Console.WriteLine($"  - ValueTask<{returnType.Name}>委托创建失败");
                }
            }
            else if (methodInfo.ReturnType == typeof(ValueTask))
            {
                Console.WriteLine("  - 无返回值ValueTask类型");

                if (!TryCreateVoidValueTaskDelegate(cacheKey, implementationType, parameterType))
                {
                    Console.WriteLine("  - ValueTask委托创建失败");
                }
            }
        }
        catch (Exception ex)
        {
            // 如果创建直接委托失败，记录错误但继续执行
            Console.WriteLine($"无法为 {cacheKey} 创建直接调用委托: {ex.Message}");
            Console.WriteLine($"详细堆栈信息: {ex.StackTrace}");
        }
    }

    /// <summary>
    /// 尝试创建ValueTask&lt;TResult&gt;类型的委托
    /// </summary>
    private bool TryCreateValueTaskTResultDelegate(string cacheKey, Type implementationType, Type parameterType, Type returnType)
    {
        try
        {
            // 创建泛型委托类型
            var delegateType = typeof(Func<,,,>).MakeGenericType(
                implementationType, typeof(byte[]), typeof(CancellationToken), typeof(ValueTask<>).MakeGenericType(returnType));

            Console.WriteLine($"  - 委托类型: {delegateType}");

            // 获取用于创建包装器的静态方法 - 修复BindingFlags
            var wrapperMethod = typeof(ServiceRegistry).GetMethod(
                nameof(CreateValueTaskTResultWrapper),
                BindingFlags.NonPublic | BindingFlags.Static);

            if (wrapperMethod == null)
            {
                Console.WriteLine("  - 错误: 找不到CreateValueTaskTResultWrapper方法");
                Console.WriteLine("  - 尝试获取所有静态方法进行诊断...");
                LogAvailableStaticMethods();
                return false;
            }

            Console.WriteLine($"  - 找到包装器方法: {wrapperMethod}");

            // 创建泛型方法 - 确保泛型参数正确
            var genericWrapperMethod = wrapperMethod.MakeGenericMethod(
                implementationType, parameterType, returnType);

            Console.WriteLine($"  - 泛型包装器方法: {genericWrapperMethod}");

            // 验证方法签名
            if (!ValidateMethodSignature(genericWrapperMethod, delegateType))
            {
                Console.WriteLine("  - 错误: 方法签名不匹配");
                return false;
            }

            // 创建委托
            var directDelegate = Delegate.CreateDelegate(delegateType, genericWrapperMethod);

            // 添加到缓存
            _directMethodCache.TryAdd(cacheKey, directDelegate);
            Console.WriteLine($"  - 成功创建ValueTask<{returnType.Name}>的直接调用委托");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  - 创建ValueTask<T>委托失败: {ex.Message}");
            Console.WriteLine($"  - 详细错误: {ex}");
            return false;
        }
    }

    /// <summary>
    /// 尝试创建无返回值ValueTask类型的委托
    /// </summary>
    private bool TryCreateVoidValueTaskDelegate(string cacheKey, Type implementationType, Type parameterType)
    {
        try
        {
            // 创建委托类型
            var delegateType = typeof(Func<,,,>).MakeGenericType(
                implementationType, typeof(byte[]), typeof(CancellationToken), typeof(ValueTask));

            Console.WriteLine($"  - 委托类型: {delegateType}");

            // 获取用于创建包装器的静态方法 - 修复BindingFlags
            var wrapperMethod = typeof(ServiceRegistry).GetMethod(
                nameof(CreateVoidValueTaskWrapper),
                BindingFlags.NonPublic | BindingFlags.Static);

            if (wrapperMethod == null)
            {
                Console.WriteLine("  - 错误: 找不到CreateVoidValueTaskWrapper方法");
                Console.WriteLine("  - 尝试获取所有静态方法进行诊断...");
                LogAvailableStaticMethods();
                return false;
            }

            Console.WriteLine($"  - 找到包装器方法: {wrapperMethod}");

            // 创建泛型方法
            var genericWrapperMethod = wrapperMethod.MakeGenericMethod(implementationType, parameterType);

            Console.WriteLine($"  - 泛型包装器方法: {genericWrapperMethod}");

            // 验证方法签名
            if (!ValidateMethodSignature(genericWrapperMethod, delegateType))
            {
                Console.WriteLine("  - 错误: 方法签名不匹配");
                return false;
            }

            // 创建委托
            var directDelegate = Delegate.CreateDelegate(delegateType, genericWrapperMethod);

            // 添加到缓存
            _directMethodCache.TryAdd(cacheKey, directDelegate);
            Console.WriteLine("  - 成功创建ValueTask的直接调用委托");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  - 创建ValueTask委托失败: {ex.Message}");
            Console.WriteLine($"  - 详细错误: {ex}");
            return false;
        }
    }

    /// <summary>
    /// 验证方法签名与委托类型是否匹配
    /// </summary>
    private bool ValidateMethodSignature(MethodInfo method, Type delegateType)
    {
        try
        {
            var invokeMethod = delegateType.GetMethod("Invoke");
            if (invokeMethod == null) return false;

            var delegateParams = invokeMethod.GetParameters();
            var methodParams = method.GetParameters();

            // 验证参数数量
            if (delegateParams.Length != methodParams.Length)
            {
                Console.WriteLine($"    - 参数数量不匹配: 委托={delegateParams.Length}, 方法={methodParams.Length}");
                return false;
            }

            // 验证参数类型
            for (int i = 0; i < delegateParams.Length; i++)
            {
                if (delegateParams[i].ParameterType != methodParams[i].ParameterType)
                {
                    Console.WriteLine($"    - 参数{i}类型不匹配: 委托={delegateParams[i].ParameterType}, 方法={methodParams[i].ParameterType}");
                    return false;
                }
            }

            // 验证返回类型
            if (invokeMethod.ReturnType != method.ReturnType)
            {
                Console.WriteLine($"    - 返回类型不匹配: 委托={invokeMethod.ReturnType}, 方法={method.ReturnType}");
                return false;
            }

            Console.WriteLine("    - 方法签名验证通过");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    - 方法签名验证异常: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 记录可用的静态方法以便诊断
    /// </summary>
    private void LogAvailableStaticMethods()
    {
        try
        {
            var staticMethods = typeof(ServiceRegistry).GetMethods(BindingFlags.NonPublic | BindingFlags.Static);
            Console.WriteLine("  - 可用的静态方法:");
            foreach (var method in staticMethods)
            {
                Console.WriteLine($"    - {method.Name}: {method}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  - 无法获取静态方法列表: {ex.Message}");
        }
    }

    /// <summary>
    /// 创建ValueTask&lt;TResult&gt;方法的静态包装器
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
    /// 创建反序列化委托 - 修复版本
    /// </summary>
    private static Func<byte[], object> CreateDeserializerDelegate(Type type)
    {
        try
        {
            // 添加全面的空值和有效性检查
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type), "反序列化类型不能为 null");
            }

            Console.WriteLine($"[ServiceRegistry] 正在为类型 {type.FullName} 创建反序列化委托...");

            // 检查类型是否有 MemoryPackable 特性
            var memoryPackableAttrs = type.GetCustomAttributes(typeof(MemoryPackableAttribute), false);
            if (memoryPackableAttrs.Length == 0)
            {
                Console.WriteLine($"[ServiceRegistry] 警告: 类型 {type.Name} 没有 [MemoryPackable] 特性");
            }

            // 首先尝试直接创建一个简单的委托来测试 MemoryPack 是否可用
            try
            {
                // 测试序列化器是否可用
                var testMethod = typeof(MemoryPackSerializer)
                    .GetMethod(nameof(MemoryPackSerializer.Deserialize), new Type[] { typeof(byte[]) });

                if (testMethod == null)
                {
                    throw new InvalidOperationException("无法找到 MemoryPackSerializer.Deserialize(byte[]) 方法");
                }

                var testGenericMethod = testMethod.MakeGenericMethod(type);
                Console.WriteLine($"[ServiceRegistry] 成功创建测试泛型方法: {testGenericMethod}");

                // 如果测试成功，使用表达式树创建高性能委托
                return CreateExpressionTreeDelegate(type);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ServiceRegistry] 表达式树方法失败: {ex.Message}，使用反射回退方法");
                return CreateReflectionDelegate(type);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ServiceRegistry] 为类型 {type?.Name ?? "null"} 创建反序列化委托完全失败: {ex.Message}");
            Console.WriteLine($"[ServiceRegistry] 异常类型: {ex.GetType().Name}");
            Console.WriteLine($"[ServiceRegistry] 堆栈跟踪: {ex.StackTrace}");

            // 最后的回退方案
            return CreateFallbackDelegate(type);
        }
    }

    /// <summary>
    /// 使用表达式树创建高性能委托
    /// </summary>
    private static Func<byte[], object> CreateExpressionTreeDelegate(Type type)
    {
        // 创建参数表达式
        var dataParam = Expression.Parameter(typeof(byte[]), "data");

        // 获取 MemoryPackSerializer.Deserialize<T>(byte[]) 方法
        var deserializeMethod = typeof(MemoryPackSerializer)
            .GetMethod(nameof(MemoryPackSerializer.Deserialize), [typeof(byte[])])!
            .MakeGenericMethod(type);

        // 创建方法调用表达式
        var call = Expression.Call(null, deserializeMethod, dataParam);

        // 将结果转换为 object
        var resultExpr = Expression.Convert(call, typeof(object));

        // 编译 Lambda 表达式
        var lambda = Expression.Lambda<Func<byte[], object>>(resultExpr, dataParam);
        var compiledDelegate = lambda.Compile();

        Console.WriteLine($"[ServiceRegistry] 成功为类型 {type.Name} 创建表达式树委托");
        return compiledDelegate;
    }

    /// <summary>
    /// 使用反射创建委托（性能较低但更稳定）
    /// </summary>
    private static Func<byte[], object> CreateReflectionDelegate(Type type)
    {
        Console.WriteLine($"[ServiceRegistry] 为类型 {type.Name} 创建反射委托");

        return (data) =>
        {
            try
            {
                if (data == null || data.Length == 0)
                {
                    Console.WriteLine($"[ServiceRegistry] 警告: 尝试反序列化空数据，返回默认值，类型: {type.Name}");
                    return type.IsValueType ? Activator.CreateInstance(type)! : null!;
                }

                // 使用反射调用
                var method = typeof(MemoryPackSerializer)
                    .GetMethod(nameof(MemoryPackSerializer.Deserialize), new Type[] { typeof(byte[]) });

                if (method == null)
                {
                    throw new InvalidOperationException("无法找到 MemoryPackSerializer.Deserialize 方法");
                }

                var genericMethod = method.MakeGenericMethod(type);
                var result = genericMethod.Invoke(null, new object[] { data });

                Console.WriteLine($"[ServiceRegistry] 反射反序列化成功，类型: {type.Name}，数据长度: {data.Length}，结果: {result != null}");

                // 确保不返回null值
                if (result == null)
                {
                    Console.WriteLine($"[ServiceRegistry] 警告: 反序列化返回null，创建默认实例，类型: {type.Name}");
                    return type.IsValueType ? Activator.CreateInstance(type)! : Activator.CreateInstance(type)!;
                }

                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ServiceRegistry] 反射反序列化失败，类型: {type.Name}，错误: {ex.Message}");
                Console.WriteLine($"[ServiceRegistry] 详细错误: {ex}");

                // 发生异常时返回默认实例而不是null
                try
                {
                    var defaultInstance = Activator.CreateInstance(type);
                    Console.WriteLine($"[ServiceRegistry] 返回默认实例，类型: {type.Name}");
                    return defaultInstance!;
                }
                catch (Exception createEx)
                {
                    Console.WriteLine($"[ServiceRegistry] 无法创建默认实例: {createEx.Message}");
                    throw new InvalidOperationException($"反序列化失败且无法创建默认实例，类型: {type.Name}", ex);
                }
            }
        };
    }

    /// <summary>
    /// 最终回退方案
    /// </summary>
    private static Func<byte[], object> CreateFallbackDelegate(Type? type)
    {
        Console.WriteLine($"[ServiceRegistry] 为类型 {type?.Name ?? "null"} 创建回退委托");

        return (data) =>
        {
            try
            {
                if (type == null)
                {
                    throw new ArgumentNullException(nameof(type), "类型不能为 null");
                }

                if (data == null || data.Length == 0)
                {
                    Console.WriteLine($"[ServiceRegistry] 回退: 空数据，返回默认值，类型: {type.Name}");
                    return type.IsValueType ? Activator.CreateInstance(type)! : null!;
                }

                // 尝试使用反射
                var method = typeof(MemoryPackSerializer).GetMethod(nameof(MemoryPackSerializer.Deserialize), [typeof(byte[])]);

                if (method == null)
                {
                    throw new InvalidOperationException("MemoryPackSerializer.Deserialize 方法不可用");
                }

                var genericMethod = method.MakeGenericMethod(type);
                var result = genericMethod.Invoke(null, new object[] { data });

                Console.WriteLine($"[ServiceRegistry] 回退反序列化成功，类型: {type.Name}");
                return result!;
            }
            catch (Exception fallbackEx)
            {
                Console.WriteLine($"[ServiceRegistry] 回退反序列化也失败: {fallbackEx.Message}");

                // 返回默认值
                try
                {
                    if (type != null)
                    {
                        var defaultValue = type.IsValueType ? Activator.CreateInstance(type)! : null!;
                        Console.WriteLine($"[ServiceRegistry] 返回默认值，类型: {type.Name}");
                        return defaultValue;
                    }
                    return null!;
                }
                catch (Exception createEx)
                {
                    Console.WriteLine($"[ServiceRegistry] 创建默认实例也失败: {createEx.Message}");
                    return null!;
                }
            }
        };
    }

    /// <summary>
    /// 调用服务方法
    /// </summary>
    private async Task<object?> InvokeMethodAsync(string serviceName, string methodName,
        byte[] requestData, IServerTransport transport)
    {
        _logger?.LogDebug("[方法调用] 开始调用方法: {ServiceName}.{MethodName}, 连接ID: {ConnectionId}",
            serviceName, methodName, transport.ConnectionId);

        // 查找服务
        if (!_services.TryGetValue(serviceName, out var serviceInfo))
        {
            _logger?.LogError("[方法调用] 未找到服务: {ServiceName}, 可用服务: [{Services}]",
                serviceName, string.Join(", ", _services.Keys));
            throw new InvalidOperationException($"未找到服务: {serviceName}");
        }

        _logger?.LogDebug("[方法调用] 找到服务: {ServiceName}, 默认通道: {DefaultChannel}",
            serviceName, serviceInfo.DefaultChannel);

        // 查找方法
        if (!serviceInfo.Methods.TryGetValue(methodName, out var methodInfo))
        {
            _logger?.LogError("[方法调用] 服务 {ServiceName} 中未找到方法: {MethodName}, 可用方法: [{Methods}]",
                serviceName, methodName, string.Join(", ", serviceInfo.Methods.Keys));
            throw new InvalidOperationException($"服务 {serviceName} 中未找到方法: {methodName}");
        }

        _logger?.LogDebug("[方法调用] 找到方法: {ServiceName}.{MethodName}, 方法通道: {MethodChannel}",
            serviceName, methodName, methodInfo.Channel ?? "Default");

        try
        {
            // 运行认证中间件
            _logger?.LogDebug("[方法调用] 开始认证检查: {ServiceName}.{MethodName}, 连接ID: {ConnectionId}",
                serviceName, methodName, transport.ConnectionId);

            await _authMiddleware!.AuthenticateRequestAsync(transport, serviceName, methodName, methodInfo.MethodInfo);

            _logger?.LogInformation("[方法调用] 认证检查通过: {ServiceName}.{MethodName}, 连接ID: {ConnectionId}",
                serviceName, methodName, transport.ConnectionId);

            // 设置请求上下文 - 这是修复RequestContext.Current返回null的关键
            _logger?.LogDebug("[方法调用] 设置请求上下文: {ConnectionId}", transport.ConnectionId);
            RequestContext.SetCurrent(transport);

            try
            {
                // 准备方法参数
                _logger?.LogDebug("[方法调用] 准备方法参数: {ServiceName}.{MethodName}", serviceName, methodName);

                object?[] parameters;
                if (requestData.Length == 0)
                {
                    _logger?.LogDebug("[方法调用] 方法无参数");
                    parameters = Array.Empty<object>();
                }
                else
                {
                    _logger?.LogDebug("[方法调用] 反序列化请求参数: Size={Size} bytes", requestData.Length);

                    var parameterTypes = methodInfo.MethodInfo.GetParameters().Select(p => p.ParameterType).ToArray();
                    _logger?.LogTrace("[方法调用] 参数类型: [{ParameterTypes}]",
                        string.Join(", ", parameterTypes.Select(t => t.Name)));

                    // 反序列化参数
                    var serializer = _serializerProvider!.Create(MethodType.Unary, null);
                    parameters = new object?[parameterTypes.Length];

                    if (parameterTypes.Length == 1)
                    {
                        // 单参数直接反序列化
                        var parameter = typeof(ISerializer)
                            .GetMethod(nameof(ISerializer.Deserialize))!
                            .MakeGenericMethod(parameterTypes[0])
                            .Invoke(serializer, new object[] { new ReadOnlySequence<byte>(requestData) });
                        parameters[0] = parameter;

                        _logger?.LogDebug("[方法调用] 单参数反序列化完成: Type={Type}", parameterTypes[0].Name);
                    }
                    else
                    {
                        _logger?.LogWarning("[方法调用] 多参数方法暂未实现: {ServiceName}.{MethodName}", serviceName, methodName);
                        throw new NotImplementedException("多参数方法反序列化暂未实现");
                    }
                }

                // 调用方法
                _logger?.LogInformation("[方法调用] 执行业务逻辑: {ServiceName}.{MethodName}, 参数数量: {ParameterCount}",
                    serviceName, methodName, parameters.Length);

                var result = methodInfo.MethodInfo.Invoke(serviceInfo.Implementation, parameters);

                // 处理异步结果
                if (result is Task task)
                {
                    _logger?.LogDebug("[方法调用] 等待异步方法完成: {ServiceName}.{MethodName}", serviceName, methodName);
                    await task;

                    if (task.GetType().IsGenericType)
                    {
                        result = task.GetType().GetProperty("Result")?.GetValue(task);
                        _logger?.LogDebug("[方法调用] 获取异步方法返回值: Type={Type}", result?.GetType().Name ?? "null");
                    }
                    else
                    {
                        _logger?.LogDebug("[方法调用] 异步方法无返回值");
                        result = null;
                    }
                }
                else if (result != null && IsValueTaskType(result.GetType()))
                {
                    _logger?.LogDebug("[方法调用] 等待ValueTask方法完成: {ServiceName}.{MethodName}", serviceName, methodName);

                    // 处理ValueTask
                    await (dynamic)result;

                    if (result.GetType().IsGenericType)
                    {
                        result = result.GetType().GetProperty("Result")?.GetValue(result);
                        _logger?.LogDebug("[方法调用] 获取ValueTask方法返回值: Type={Type}", result?.GetType().Name ?? "null");
                    }
                    else
                    {
                        _logger?.LogDebug("[方法调用] ValueTask方法无返回值");
                        result = null;
                    }
                }

                _logger?.LogInformation("[方法调用] 业务逻辑执行完成: {ServiceName}.{MethodName}, 返回值类型: {ReturnType}",
                    serviceName, methodName, result?.GetType().Name ?? "null");

                return result;
            }
            finally
            {
                // 清除请求上下文
                _logger?.LogDebug("[方法调用] 清除请求上下文: {ConnectionId}", transport.ConnectionId);
                RequestContext.Clear();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[方法调用] 调用方法失败: {ServiceName}.{MethodName}, 连接ID: {ConnectionId}",
                serviceName, methodName, transport.ConnectionId);
            throw;
        }
    }

    /// <summary>
    /// 直接调用已知服务和方法 - 高效路径
    /// </summary>
    public async Task<TResponse> DirectInvokeAsync<TRequest, TResponse>(
        IPulseHub service, string methodName, byte[] requestBytes)
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
            // 自动处理所有公共方法，不再检查 Operation 特性
            if (method.IsPublic)
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

    #region RPC消息处理

    /// <summary>
    /// 处理新通道连接
    /// </summary>
    private void OnChannelConnected(object? sender, ChannelEventArgs e)
    {
        try
        {
            // 为新通道订阅数据接收事件
            e.Channel.DataReceived += OnChannelDataReceived;
            _logger?.LogDebug("已为通道 {ConnectionId} 订阅DataReceived事件", e.Channel.ConnectionId);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "订阅通道 {ConnectionId} 的DataReceived事件时发生错误", e.Channel.ConnectionId);
        }
    }

    /// <summary>
    /// 处理通道断开
    /// </summary>
    private void OnChannelDisconnected(object? sender, ChannelEventArgs e)
    {
        try
        {
            // 取消订阅数据接收事件
            e.Channel.DataReceived -= OnChannelDataReceived;
            _logger?.LogDebug("已为通道 {ConnectionId} 取消订阅DataReceived事件", e.Channel.ConnectionId);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "取消订阅通道 {ConnectionId} 的DataReceived事件时发生错误", e.Channel.ConnectionId);
        }
    }

    /// <summary>
    /// 处理通道数据接收
    /// </summary>
    private async void OnChannelDataReceived(object? sender, TransportDataEventArgs e)
    {
        if (sender is not ITransportChannel channel)
        {
            _logger?.LogError("[ServiceRegistry] DataReceived事件发送者不是ITransportChannel类型: {SenderType}",
                sender?.GetType().Name ?? "null");
            return;
        }

        try
        {
            _logger?.LogInformation("[ServiceRegistry] {ConnectionId} 收到RPC数据: Size={Size} bytes, Data=[{DataHex}]",
                channel.ConnectionId, e.Data.Length, Convert.ToHexString(e.Data.Span[..Math.Min(e.Data.Length, 64)]));

            // 解析RPC消息
            _logger?.LogDebug("[ServiceRegistry] {ConnectionId} 开始解析RPC消息", channel.ConnectionId);
            var message = ParseRpcMessage(e.Data);

            if (message == null)
            {
                _logger?.LogError("[ServiceRegistry] {ConnectionId} 无法解析RPC消息，数据可能已损坏: Size={Size}",
                    channel.ConnectionId, e.Data.Length);
                await SendStructuredErrorResponse(channel, "Unknown", "ParseError", "MESSAGE_PARSE_FAILED",
                    "无法解析RPC消息，数据格式错误");
                return;
            }

            _logger?.LogInformation("[ServiceRegistry] {ConnectionId} RPC消息解析成功: 服务={ServiceName}, 方法={MethodName}, 请求ID={RequestId}",
                channel.ConnectionId, message.ServiceName, message.MethodName, message.RequestId);

            // 调用服务方法
            _logger?.LogDebug("[ServiceRegistry] {ConnectionId} 开始调用服务方法: {ServiceName}.{MethodName}",
                channel.ConnectionId, message.ServiceName, message.MethodName);

            var serverConnection = channel.Transport;
            var result = await InvokeMethodAsync(
                message.ServiceName,
                message.MethodName,
                message.RequestData,
                serverConnection);

            _logger?.LogInformation("[ServiceRegistry] {ConnectionId} 服务方法调用完成: {ServiceName}.{MethodName}, 请求ID={RequestId}",
                channel.ConnectionId, message.ServiceName, message.MethodName, message.RequestId);

            // 发送响应
            _logger?.LogDebug("[ServiceRegistry] {ConnectionId} 开始发送响应: {RequestId}", channel.ConnectionId, message.RequestId);
            await SendResponse(channel, message.RequestId, result);
            _logger?.LogDebug("[ServiceRegistry] {ConnectionId} 响应发送完成: {RequestId}", channel.ConnectionId, message.RequestId);
        }
        catch (Exception ex)
        {
            string? serviceName = null;
            string? methodName = null;
            string? requestId = "Unknown";

            // 尝试从解析的消息中获取服务和方法信息
            try
            {
                var parsedMessage = ParseRpcMessage(e.Data);
                if (parsedMessage != null)
                {
                    serviceName = parsedMessage.ServiceName;
                    methodName = parsedMessage.MethodName;
                    requestId = parsedMessage.RequestId;
                }
            }
            catch
            {
                // 忽略解析失败
            }

            // 根据异常类型进行专门处理
            switch (ex)
            {
                case UnauthorizedAccessException unauthorizedEx:
                    _logger?.LogWarning("认证失败 - 通道: {ConnectionId}, 服务: {ServiceName}, 方法: {MethodName}, 错误: {Error}",
                        channel.ConnectionId, serviceName ?? "未知", methodName ?? "未知", unauthorizedEx.Message);

                    // 发送结构化认证错误响应
                    try
                    {
                        await SendStructuredErrorResponse(channel, requestId, "AuthenticationError", "UNAUTHORIZED",
                            unauthorizedEx.Message, serviceName, methodName, new Dictionary<string, object>
                            {
                                { "ConnectionId", channel.ConnectionId },
                                { "AuthenticationRequired", true }
                            });
                    }
                    catch (Exception sendEx)
                    {
                        _logger?.LogError(sendEx, "发送认证错误响应失败");
                    }
                    break;

                case InvalidOperationException invalidOpEx when invalidOpEx.Message.Contains("服务未找到") || invalidOpEx.Message.Contains("方法未找到"):
                    _logger?.LogWarning("服务调用错误 - 通道: {ConnectionId}, 服务: {ServiceName}, 方法: {MethodName}, 错误: {Error}",
                        channel.ConnectionId, serviceName ?? "未知", methodName ?? "未知", invalidOpEx.Message);

                    try
                    {
                        await SendStructuredErrorResponse(channel, requestId, "ServiceError", "NOT_FOUND",
                            invalidOpEx.Message, serviceName, methodName);
                    }
                    catch (Exception sendEx)
                    {
                        _logger?.LogError(sendEx, "发送服务错误响应失败");
                    }
                    break;

                case TimeoutException timeoutEx:
                    _logger?.LogWarning("请求超时 - 通道: {ConnectionId}, 服务: {ServiceName}, 方法: {MethodName}, 错误: {Error}",
                        channel.ConnectionId, serviceName ?? "未知", methodName ?? "未知", timeoutEx.Message);

                    try
                    {
                        await SendStructuredErrorResponse(channel, requestId, "TimeoutError", "REQUEST_TIMEOUT",
                            timeoutEx.Message, serviceName, methodName);
                    }
                    catch (Exception sendEx)
                    {
                        _logger?.LogError(sendEx, "发送超时错误响应失败");
                    }
                    break;

                default:
                    _logger?.LogError(ex, "处理来自通道 {ConnectionId} 的RPC消息时发生错误 - 服务: {ServiceName}, 方法: {MethodName}",
                        channel.ConnectionId, serviceName ?? "未知", methodName ?? "未知");

                    // 发送通用错误响应
                    try
                    {
                        await SendStructuredErrorResponse(channel, requestId, "InternalError", "INTERNAL_SERVER_ERROR",
                            "服务器内部错误，请稍后重试", serviceName, methodName, new Dictionary<string, object>
                            {
                                { "ExceptionType", ex.GetType().Name }
                            });
                    }
                    catch (Exception sendEx)
                    {
                        _logger?.LogError(sendEx, "发送错误响应失败");
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// 解析RPC消息
    /// </summary>
    private RpcMessage? ParseRpcMessage(ReadOnlyMemory<byte> data)
    {
        try
        {
            if (_serializerProvider == null)
            {
                _logger?.LogError("[RPC解析] 序列化器提供程序为null，无法解析RPC消息");
                return null;
            }

            _logger?.LogDebug("[RPC解析] 开始解析RPC消息，数据长度: {Length} bytes", data.Length);

            if (data.Length < 4)
            {
                _logger?.LogError("[RPC解析] 收到的消息太短，无法包含头部长度: {Length} bytes", data.Length);
                return null;
            }

            // 读取头部长度（小端序）
            var headerLengthBytes = data.Slice(0, 4).ToArray();
            int headerLength = BitConverter.ToInt32(headerLengthBytes, 0);

            _logger?.LogDebug("[RPC解析] 解析头部长度: {HeaderLength} bytes", headerLength);

            // 检查头部长度合法性
            if (headerLength <= 0 || headerLength > data.Length - 4)
            {
                _logger?.LogError("[RPC解析] 收到无效的消息头长度: {HeaderLength}, 数据总长度: {DataLength}",
                    headerLength, data.Length);
                return null;
            }

            // 读取头部
            var headerBytes = data.Slice(4, headerLength).ToArray();
            _logger?.LogDebug("[RPC解析] 解析消息头部: Size={HeaderSize} bytes, Data=[{HeaderHex}]",
                headerBytes.Length, Convert.ToHexString(headerBytes, 0, Math.Min(headerBytes.Length, 32)));

            var serializer = _serializerProvider.Create(MethodType.Unary, null);
            var header = serializer.Deserialize<MessageHeader>(new System.Buffers.ReadOnlySequence<byte>(headerBytes));

            _logger?.LogDebug("[RPC解析] 成功反序列化消息头: Type={Type}, ServiceName={ServiceName}, MethodName={MethodName}, MessageId={MessageId}",
                header.Type, header.ServiceName, header.MethodName, header.MessageId);

            // 验证消息类型
            if (header.Type != MessageType.Request)
            {
                _logger?.LogWarning("[RPC解析] 忽略非请求消息类型: {Type}", header.Type);
                return null;
            }

            // 读取消息体
            var bodyStartIndex = 4 + headerLength;
            var bodyLength = data.Length - bodyStartIndex;
            byte[] bodyBytes = bodyLength > 0 ? data.Slice(bodyStartIndex, bodyLength).ToArray() : Array.Empty<byte>();

            _logger?.LogDebug("[RPC解析] 解析消息体: Size={BodySize} bytes", bodyLength);
            if (bodyLength > 0)
            {
                _logger?.LogTrace("[RPC解析] 消息体数据: [{BodyHex}]",
                    Convert.ToHexString(bodyBytes, 0, Math.Min(bodyBytes.Length, 32)));
            }

            _logger?.LogInformation("[RPC解析] RPC消息解析完成: 服务={ServiceName}, 方法={MethodName}, 请求ID={RequestId}, 体大小={BodySize}",
                header.ServiceName, header.MethodName, header.MessageId, bodyLength);

            return new RpcMessage
            {
                ServiceName = header.ServiceName ?? string.Empty,
                MethodName = header.MethodName ?? string.Empty,
                RequestId = header.MessageId.ToString(),
                RequestData = bodyBytes
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[RPC解析] 解析RPC消息失败，数据长度: {Length}", data.Length);
            return null;
        }
    }

    /// <summary>
    /// 发送响应
    /// </summary>
    private async Task SendResponse(ITransportChannel channel, string requestId, object? result)
    {
        try
        {
            if (_serializerProvider == null)
            {
                _logger?.LogError("序列化器提供程序为null，无法发送响应");
                return;
            }

            // 检查是否是void方法（无返回值）
            if (result == null)
            {
                _logger?.LogDebug("方法无返回值，不发送响应: RequestId={RequestId}", requestId);
                return;
            }

            _logger?.LogDebug("序列化响应，类型: {Type}", result.GetType().Name);

            var serializer = _serializerProvider.Create(MethodType.Unary, null);

            // 创建响应头
            var header = new MessageHeader
            {
                Type = MessageType.Response,
                MessageId = Guid.Parse(requestId)
            };

            // 序列化头部
            var headerWriter = new System.Buffers.ArrayBufferWriter<byte>();
            serializer.Serialize(headerWriter, in header);
            var headerBytes = headerWriter.WrittenMemory.ToArray();

            // 序列化响应体
            byte[] payloadBytes = Array.Empty<byte>();
            try
            {
                var payloadWriter = new System.Buffers.ArrayBufferWriter<byte>();

                // 使用result的实际类型进行序列化
                var resultType = result.GetType();
                _logger?.LogDebug("序列化响应，类型: {Type}", resultType.Name);

                // 使用反射调用泛型序列化方法
                var serializeMethod = typeof(ISerializer).GetMethod(nameof(ISerializer.Serialize));
                if (serializeMethod != null)
                {
                    var genericSerializeMethod = serializeMethod.MakeGenericMethod(resultType);
                    genericSerializeMethod.Invoke(serializer, new object[] { payloadWriter, result });
                    payloadBytes = payloadWriter.WrittenMemory.ToArray();
                }
                else
                {
                    _logger?.LogError("无法找到ISerializer.Serialize方法");
                    throw new InvalidOperationException("序列化方法不可用");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "序列化响应时发生错误，响应类型: {Type}", result.GetType().Name);
                throw;
            }

            // 组装完整消息：[HeaderLength(4)] + [Header] + [Payload]
            using var messageStream = new System.IO.MemoryStream();
            using var writer = new System.IO.BinaryWriter(messageStream);

            // 写入头部长度
            writer.Write(headerBytes.Length);

            // 写入头部
            writer.Write(headerBytes);

            // 写入载荷
            writer.Write(payloadBytes);

            var responseData = new ReadOnlyMemory<byte>(messageStream.ToArray());

            // 发送响应
            await channel.SendAsync(responseData);

            _logger?.LogDebug("已发送响应到通道 {ConnectionId}, 请求ID={RequestId}, 响应大小={Size} bytes",
                channel.ConnectionId, requestId, responseData.Length);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "发送响应失败");
        }
    }

    /// <summary>
    /// 发送结构化错误响应
    /// </summary>
    private async Task SendStructuredErrorResponse(ITransportChannel channel, string requestId, string errorType,
        string errorCode, string errorMessage, string? serviceName = null, string? methodName = null,
        Dictionary<string, object>? additionalInfo = null)
    {
        try
        {
            if (_serializerProvider == null)
            {
                _logger?.LogError("序列化器提供程序为null，无法发送结构化错误响应");
                return;
            }

            var serializer = _serializerProvider.Create(MethodType.Unary, null);

            // 创建错误响应头
            var header = new MessageHeader
            {
                Type = MessageType.Response,
                MessageId = string.IsNullOrEmpty(requestId) || requestId == "Unknown" ? Guid.NewGuid() : Guid.Parse(requestId),
                ServiceName = serviceName ?? string.Empty,
                MethodName = methodName ?? string.Empty
            };

            // 创建结构化错误响应体
            var structuredErrorResponse = new StructuredErrorResponse
            {
                ErrorType = errorType,
                ErrorCode = errorCode,
                ErrorMessage = errorMessage,
                RequestId = requestId ?? "Unknown",
                ServiceName = serviceName,
                MethodName = methodName
            };

            // 序列化头部
            var headerWriter = new System.Buffers.ArrayBufferWriter<byte>();
            serializer.Serialize(headerWriter, in header);
            var headerBytes = headerWriter.WrittenMemory.ToArray();

            // 序列化错误响应体
            var payloadWriter = new System.Buffers.ArrayBufferWriter<byte>();
            serializer.Serialize(payloadWriter, structuredErrorResponse);
            var payloadBytes = payloadWriter.WrittenMemory.ToArray();

            // 组装完整消息：[HeaderLength(4)] + [Header] + [Payload]
            using var messageStream = new System.IO.MemoryStream();
            using var writer = new System.IO.BinaryWriter(messageStream);

            // 写入头部长度
            writer.Write(headerBytes.Length);

            // 写入头部
            writer.Write(headerBytes);

            // 写入载荷
            writer.Write(payloadBytes);

            var responseData = new ReadOnlyMemory<byte>(messageStream.ToArray());

            await channel.SendAsync(responseData);

            _logger?.LogDebug("已发送结构化错误响应到通道 {ConnectionId} - 类型: {ErrorType}, 代码: {ErrorCode}, 请求ID: {RequestId}, 服务: {ServiceName}, 方法: {MethodName}",
                channel.ConnectionId, errorType, errorCode, requestId, serviceName ?? "未知", methodName ?? "未知");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "发送结构化错误响应失败");
        }
    }

    /// <summary>
    /// 发送错误响应（保留兼容性）
    /// </summary>
    private async Task SendErrorResponse(ITransportChannel channel, string requestId, string errorMessage)
    {
        await SendStructuredErrorResponse(channel, requestId, "InternalError", "INTERNAL_SERVER_ERROR",
            errorMessage);
    }

    /// <summary>
    /// 结构化错误响应
    /// </summary>
    [MemoryPackable]
    private partial class StructuredErrorResponse
    {
        /// <summary>
        /// 错误类型
        /// </summary>
        [MemoryPackOrder(0)]
        public required string ErrorType { get; set; }

        /// <summary>
        /// 错误代码
        /// </summary>
        [MemoryPackOrder(1)]
        public required string ErrorCode { get; set; }

        /// <summary>
        /// 错误消息
        /// </summary>
        [MemoryPackOrder(2)]
        public required string ErrorMessage { get; set; }

        /// <summary>
        /// 请求ID
        /// </summary>
        [MemoryPackOrder(3)]
        public required string RequestId { get; set; }

        /// <summary>
        /// 服务名称
        /// </summary>
        [MemoryPackOrder(4)]
        public string? ServiceName { get; set; }

        /// <summary>
        /// 方法名称
        /// </summary>
        [MemoryPackOrder(5)]
        public string? MethodName { get; set; }

        /// <summary>
        /// 时间戳
        /// </summary>
        [MemoryPackOrder(6)]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// RPC消息结构
    /// </summary>
    private class RpcMessage
    {
        public required string ServiceName { get; set; }
        public required string MethodName { get; set; }
        public required string RequestId { get; set; }
        public required byte[] RequestData { get; set; }
    }

    #endregion
}
