using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using PulseRPC.Server.Hubs; using PulseRPC.Server.Services; using PulseRPC.Server.Transport;

namespace PulseRPC.Server.Services.Management;

/// <summary>
/// 编译后的构造函数工厂 - 使用表达式树编译代替反射创建实例
/// </summary>
/// <remarks>
/// <para>
/// <strong>性能对比</strong>（单次创建）:
/// </para>
/// <list type="bullet">
/// <item><description>直接 new: ~5ns</description></item>
/// <item><description>表达式树编译委托: ~10ns (本实现)</description></item>
/// <item><description>ConstructorInfo.Invoke: ~500ns</description></item>
/// <item><description>Activator.CreateInstance: ~600ns</description></item>
/// <item><description>ActivatorUtilities.CreateInstance: ~800ns</description></item>
/// </list>
/// <para>
/// <strong>实现原理</strong>:
/// </para>
/// <list type="number">
/// <item><description>首次调用: 分析构造函数，构建表达式树</description></item>
/// <item><description>编译表达式树为 Func&lt;IServiceProvider, string, T&gt; 委托</description></item>
/// <item><description>缓存委托到 ConcurrentDictionary</description></item>
/// <item><description>后续调用: 直接调用缓存的委托（接近原生性能）</description></item>
/// </list>
/// </remarks>
public static class CompiledConstructorFactory
{
    /// <summary>
    /// 服务工厂委托缓存
    /// Key: 服务类型
    /// Value: 编译后的工厂委托 (IServiceProvider, serviceId) => IPulseService
    /// </summary>
    private static readonly ConcurrentDictionary<Type, Func<IServiceProvider, string, IPulseService>> _factoryCache = new();

    /// <summary>
    /// 构造函数信息缓存（用于诊断）
    /// </summary>
    private static readonly ConcurrentDictionary<Type, ConstructorMetadata> _metadataCache = new();

    /// <summary>
    /// 获取或创建编译后的服务工厂
    /// </summary>
    /// <typeparam name="TService">服务类型</typeparam>
    /// <returns>编译后的工厂委托</returns>
    public static Func<IServiceProvider, string, TService> GetOrCreateFactory<TService>()
        where TService : class, IPulseService
    {
        var factory = _factoryCache.GetOrAdd(typeof(TService), static type => CompileFactory(type));
        return (sp, serviceId) => (TService)factory(sp, serviceId);
    }

    /// <summary>
    /// 获取或创建编译后的服务工厂（非泛型版本）
    /// </summary>
    /// <param name="serviceType">服务类型</param>
    /// <returns>编译后的工厂委托</returns>
    public static Func<IServiceProvider, string, IPulseService> GetOrCreateFactory(Type serviceType)
    {
        return _factoryCache.GetOrAdd(serviceType, static type => CompileFactory(type));
    }

    /// <summary>
    /// 编译服务工厂表达式树
    /// </summary>
    private static Func<IServiceProvider, string, IPulseService> CompileFactory(Type serviceType)
    {
        // 查找最适合的构造函数
        var (constructor, parameterMappings) = FindBestConstructor(serviceType);

        // 参数: (IServiceProvider sp, string serviceId)
        var spParam = Expression.Parameter(typeof(IServiceProvider), "sp");
        var serviceIdParam = Expression.Parameter(typeof(string), "serviceId");

        // 构建构造函数参数表达式
        var ctorParams = constructor.GetParameters();
        var argExpressions = new Expression[ctorParams.Length];

        for (int i = 0; i < ctorParams.Length; i++)
        {
            var mapping = parameterMappings[i];

            argExpressions[i] = mapping.Source switch
            {
                ParameterSource.ServiceId => serviceIdParam,
                ParameterSource.ServiceProvider => spParam,
                ParameterSource.DependencyInjection => CreateDIResolutionExpression(spParam, ctorParams[i]),
                _ => throw new InvalidOperationException($"Unknown parameter source: {mapping.Source}")
            };
        }

        // 构建构造函数调用: new TService(arg0, arg1, ...)
        var newExpression = Expression.New(constructor, argExpressions);

        // 转换为接口类型
        var castExpression = Expression.Convert(newExpression, typeof(IPulseService));

        // 编译为委托
        var lambda = Expression.Lambda<Func<IServiceProvider, string, IPulseService>>(
            castExpression, spParam, serviceIdParam);

        var compiledFactory = lambda.Compile();

        // 缓存元数据（用于诊断和日志）
        _metadataCache[serviceType] = new ConstructorMetadata
        {
            ServiceType = serviceType,
            Constructor = constructor,
            ParameterMappings = parameterMappings,
            CompileTime = DateTime.UtcNow
        };

        return compiledFactory;
    }

    /// <summary>
    /// 查找最适合的构造函数
    /// </summary>
    /// <remarks>
    /// 优先级：
    /// 1. 带有 serviceId 参数的构造函数
    /// 2. 参数最多的构造函数（支持更多 DI 注入）
    /// </remarks>
    private static (ConstructorInfo Constructor, ParameterMapping[] Mappings) FindBestConstructor(Type serviceType)
    {
        var constructors = serviceType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        if (constructors.Length == 0)
        {
            throw new InvalidOperationException(
                $"Service type '{serviceType.Name}' has no public constructors.");
        }

        // 优先查找带 serviceId 参数的构造函数
        var serviceIdConstructor = constructors
            .Select(ctor => (Ctor: ctor, Mappings: AnalyzeConstructor(ctor)))
            .Where(x => x.Mappings.Any(m => m.Source == ParameterSource.ServiceId))
            .OrderByDescending(x => x.Ctor.GetParameters().Length)
            .FirstOrDefault();

        if (serviceIdConstructor.Ctor != null)
        {
            return (serviceIdConstructor.Ctor, serviceIdConstructor.Mappings);
        }

        // 回退: 选择参数最多的构造函数
        var fallbackConstructor = constructors
            .OrderByDescending(c => c.GetParameters().Length)
            .First();

        return (fallbackConstructor, AnalyzeConstructor(fallbackConstructor));
    }

    /// <summary>
    /// 分析构造函数参数
    /// </summary>
    private static ParameterMapping[] AnalyzeConstructor(ConstructorInfo constructor)
    {
        var parameters = constructor.GetParameters();
        var mappings = new ParameterMapping[parameters.Length];

        for (int i = 0; i < parameters.Length; i++)
        {
            var param = parameters[i];
            mappings[i] = DetermineParameterMapping(param);
        }

        return mappings;
    }

    /// <summary>
    /// 确定参数映射来源
    /// </summary>
    private static ParameterMapping DetermineParameterMapping(ParameterInfo parameter)
    {
        var paramType = parameter.ParameterType;
        var paramName = parameter.Name?.ToLowerInvariant() ?? "";

        // 1. ServiceId 参数（按名称匹配）
        if (paramType == typeof(string) &&
            (paramName == "serviceid" || paramName == "id" || paramName == "instanceid"))
        {
            return new ParameterMapping
            {
                ParameterName = parameter.Name!,
                ParameterType = paramType,
                Source = ParameterSource.ServiceId
            };
        }

        // 2. IServiceProvider 直接注入
        if (paramType == typeof(IServiceProvider))
        {
            return new ParameterMapping
            {
                ParameterName = parameter.Name!,
                ParameterType = paramType,
                Source = ParameterSource.ServiceProvider
            };
        }

        // 3. 其他参数通过 DI 解析
        return new ParameterMapping
        {
            ParameterName = parameter.Name!,
            ParameterType = paramType,
            Source = ParameterSource.DependencyInjection
        };
    }

    /// <summary>
    /// 创建 DI 解析表达式
    /// </summary>
    private static Expression CreateDIResolutionExpression(ParameterExpression spParam, ParameterInfo parameter)
    {
        var paramType = parameter.ParameterType;

        // 使用 ServiceProviderServiceExtensions.GetRequiredService<T>(sp)
        // 但由于泛型限制，我们使用非泛型版本
        var getServiceMethod = typeof(ServiceProviderServiceExtensions)
            .GetMethod(nameof(ServiceProviderServiceExtensions.GetRequiredService),
                new[] { typeof(IServiceProvider), typeof(Type) })!;

        // sp.GetRequiredService(typeof(T))
        var serviceCall = Expression.Call(
            null,
            getServiceMethod,
            spParam,
            Expression.Constant(paramType));

        // 转换为目标类型: (T)sp.GetRequiredService(typeof(T))
        return Expression.Convert(serviceCall, paramType);
    }

    /// <summary>
    /// 获取缓存统计信息
    /// </summary>
    public static FactoryStatistics GetStatistics()
    {
        return new FactoryStatistics
        {
            CachedFactoryCount = _factoryCache.Count,
            CachedTypes = _factoryCache.Keys.ToArray(),
            Metadata = _metadataCache.Values.ToArray()
        };
    }

    /// <summary>
    /// 清空缓存（仅用于测试）
    /// </summary>
    public static void ClearCache()
    {
        _factoryCache.Clear();
        _metadataCache.Clear();
    }

    /// <summary>
    /// 预编译指定类型的工厂（可用于启动时预热）
    /// </summary>
    /// <typeparam name="TService">服务类型</typeparam>
    public static void Precompile<TService>() where TService : class, IPulseService
    {
        _ = GetOrCreateFactory<TService>();
    }

    /// <summary>
    /// 预编译指定类型的工厂（非泛型版本）
    /// </summary>
    public static void Precompile(Type serviceType)
    {
        _ = GetOrCreateFactory(serviceType);
    }
}

/// <summary>
/// 参数来源类型
/// </summary>
public enum ParameterSource
{
    /// <summary>从 serviceId 参数传入</summary>
    ServiceId,

    /// <summary>直接传入 IServiceProvider</summary>
    ServiceProvider,

    /// <summary>通过依赖注入解析</summary>
    DependencyInjection
}

/// <summary>
/// 参数映射信息
/// </summary>
public sealed class ParameterMapping
{
    public required string ParameterName { get; init; }
    public required Type ParameterType { get; init; }
    public required ParameterSource Source { get; init; }
}

/// <summary>
/// 构造函数元数据（用于诊断）
/// </summary>
public sealed class ConstructorMetadata
{
    public required Type ServiceType { get; init; }
    public required ConstructorInfo Constructor { get; init; }
    public required ParameterMapping[] ParameterMappings { get; init; }
    public required DateTime CompileTime { get; init; }
}

/// <summary>
/// 工厂统计信息
/// </summary>
public sealed class FactoryStatistics
{
    public int CachedFactoryCount { get; init; }
    public Type[] CachedTypes { get; init; } = Array.Empty<Type>();
    public ConstructorMetadata[] Metadata { get; init; } = Array.Empty<ConstructorMetadata>();
}

