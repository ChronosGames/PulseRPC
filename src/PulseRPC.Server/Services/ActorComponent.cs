using System;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Authentication;

// 类型别名 - 服务间认证上下文
using AuthenticationContext = PulseRPC.Server.ServiceAuthenticationContext;
using AuthenticationContextProvider = PulseRPC.Server.ServiceAuthenticationContextProvider;

namespace PulseRPC.Server;

// ========================
// 1. 通用组件框架 - 核心抽象
// ========================

/// <summary>
/// Actor组件基类 - 完全通用
/// </summary>
/// <typeparam name="TActor">宿主Actor类型</typeparam>
public abstract class ActorComponent<TActor> where TActor : IComponentHost
{
    /// <summary>宿主Actor</summary>
    protected TActor Host { get; private set; } = default!;

    /// <summary>日志</summary>
    protected ILogger Logger { get; private set; } = null!;

    /// <summary>服务提供者</summary>
    protected IServiceProvider ServiceProvider { get; private set; } = null!;

    /// <summary>
    /// 初始化组件
    /// </summary>
    internal void Initialize(TActor host, ILogger logger, IServiceProvider serviceProvider)
    {
        Host = host;
        Logger = logger;
        ServiceProvider = serviceProvider;
        OnInitialize();
    }

    /// <summary>
    /// 组件初始化钩子
    /// </summary>
    protected virtual void OnInitialize() { }

    /// <summary>
    /// Actor启动时调用
    /// </summary>
    public virtual Task OnActorStartAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <summary>
    /// Actor停止时调用
    /// </summary>
    public virtual Task OnActorStopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <summary>
    /// 获取当前调用者
    /// </summary>
    protected AuthenticationContext GetCurrentCaller()
        => AuthenticationContextProvider.RequireCurrent();

    /// <summary>
    /// 获取其他组件
    /// </summary>
    protected TComponent GetComponent<TComponent>() where TComponent : class
        => Host.GetComponent<TComponent>();

    /// <summary>
    /// 记录操作日志
    /// </summary>
    protected void LogOperation(string operation, object details)
    {
        Logger.LogInformation(
            "ComponentOperation - Component: {Component}, Operation: {Operation}, Details: {Details}",
            GetType().Name, operation, System.Text.Json.JsonSerializer.Serialize(details));
    }
}

/// <summary>
/// 组件宿主接口 - 任何支持组件的Actor都需要实现
/// </summary>
public interface IComponentHost
{
    /// <summary>获取组件</summary>
    TComponent GetComponent<TComponent>() where TComponent : class;

    /// <summary>Actor的PID</summary>
    PID ServicePID { get; }

    /// <summary>服务提供者</summary>
    IServiceProvider ServiceProvider { get; }
}

/// <summary>
/// 组件注册特性 - 声明组件实现的接口
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class ComponentAttribute : Attribute
{
    public Type[] InterfaceTypes { get; }

    public ComponentAttribute(params Type[] interfaceTypes)
    {
        InterfaceTypes = interfaceTypes;
    }
}

/// <summary>
/// 组件管理器 - 负责组件的注册、查找和生命周期管理
/// </summary>
public class ComponentManager<TActor> where TActor : IComponentHost
{
    private readonly TActor _host;
    private readonly ILogger _logger;
    private readonly IServiceProvider _serviceProvider;

    private readonly Dictionary<Type, object> _components = new();
    private readonly Dictionary<Type, Type> _interfaceToComponent = new();
    private readonly List<Type> _registeredComponentTypes = new();

    public ComponentManager(TActor host, ILogger logger, IServiceProvider serviceProvider)
    {
        _host = host;
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// 自动注册组件类型
    /// </summary>
    public void RegisterComponentTypes(params Type[] componentTypes)
    {
        foreach (var componentType in componentTypes)
        {
            if (!IsValidComponentType(componentType))
            {
                throw new InvalidOperationException(
                    $"Type {componentType.Name} is not a valid component type. " +
                    $"It must inherit from ActorComponent<{typeof(TActor).Name}>");
            }

            _registeredComponentTypes.Add(componentType);

            _logger.LogDebug("Component type registered - Type: {Type}", componentType.Name);
        }
    }

    /// <summary>
    /// 自动扫描并注册程序集中的所有组件
    /// </summary>
    public void AutoRegisterComponents(Assembly assembly)
    {
        var componentTypes = assembly.GetTypes()
            .Where(t => !t.IsAbstract && IsValidComponentType(t))
            .ToArray();

        RegisterComponentTypes(componentTypes);

        _logger.LogInformation(
            "Auto-registered components - Count: {Count}, Assembly: {Assembly}",
            componentTypes.Length, assembly.GetName().Name);
    }

    /// <summary>
    /// 初始化所有组件
    /// </summary>
    public void InitializeComponents()
    {
        foreach (var componentType in _registeredComponentTypes)
        {
            var component = CreateComponent(componentType);
            _components[componentType] = component;

            // 注册组件实现的接口
            var attr = componentType.GetCustomAttribute<ComponentAttribute>();
            if (attr != null)
            {
                foreach (var interfaceType in attr.InterfaceTypes)
                {
                    _interfaceToComponent[interfaceType] = componentType;

                    _logger.LogTrace(
                        "Interface mapped - Interface: {Interface}, Component: {Component}",
                        interfaceType.Name, componentType.Name);
                }
            }
        }

        _logger.LogInformation(
            "Components initialized - Host: {Host}, Count: {Count}",
            typeof(TActor).Name, _components.Count);
    }

    /// <summary>
    /// 启动所有组件
    /// </summary>
    public async Task StartComponentsAsync(CancellationToken cancellationToken)
    {
        foreach (var component in _components.Values)
        {
            if (component is IActorComponent actorComponent)
            {
                await actorComponent.OnActorStartAsync(cancellationToken);
            }
        }
    }

    /// <summary>
    /// 停止所有组件
    /// </summary>
    public async Task StopComponentsAsync(CancellationToken cancellationToken)
    {
        foreach (var component in _components.Values)
        {
            if (component is IActorComponent actorComponent)
            {
                await actorComponent.OnActorStopAsync(cancellationToken);
            }
        }
    }

    /// <summary>
    /// 获取组件
    /// </summary>
    public TComponent GetComponent<TComponent>() where TComponent : class
    {
        var componentType = typeof(TComponent);

        // 直接类型查找
        if (_components.TryGetValue(componentType, out var component))
        {
            return (TComponent)component;
        }

        // 接口查找
        if (_interfaceToComponent.TryGetValue(componentType, out var mappedType))
        {
            return (TComponent)_components[mappedType];
        }

        throw new InvalidOperationException(
            $"Component {componentType.Name} not found on {typeof(TActor).Name}");
    }

    /// <summary>
    /// 尝试获取组件
    /// </summary>
    public bool TryGetComponent<TComponent>(out TComponent? component) where TComponent : class
    {
        try
        {
            component = GetComponent<TComponent>();
            return true;
        }
        catch
        {
            component = null;
            return false;
        }
    }

    /// <summary>
    /// 查找方法所在的组件
    /// </summary>
    public object? FindComponentByMethod(PulseRPC.Protocol.ProtocolId protocolId)
    {
        // TODO: 协议号到组件的映射将由 SourceGenerator 生成
        throw new NotImplementedException(
            $"Protocol ID to component mapping not yet implemented. " +
            $"SourceGenerator will provide this mapping. ProtocolId: {protocolId}");
    }

    /// <summary>
    /// 获取所有组件
    /// </summary>
    public IEnumerable<object> GetAllComponents() => _components.Values;

    private object CreateComponent(Type componentType)
    {
        var component = Activator.CreateInstance(componentType)!;

        // 调用Initialize方法
        var initMethod = componentType
            .GetMethod("Initialize", BindingFlags.NonPublic | BindingFlags.Instance);

        if (initMethod != null)
        {
            initMethod.Invoke(component, new object[] { _host, _logger, _serviceProvider });
        }

        return component;
    }

    private bool IsValidComponentType(Type type)
    {
        var baseType = type.BaseType;
        while (baseType != null)
        {
            if (baseType.IsGenericType &&
                baseType.GetGenericTypeDefinition() == typeof(ActorComponent<>))
            {
                // 检查泛型参数是否匹配
                var genericArg = baseType.GetGenericArguments()[0];
                return genericArg == typeof(TActor) || typeof(TActor).IsAssignableFrom(genericArg);
            }
            baseType = baseType.BaseType;
        }
        return false;
    }
}

/// <summary>
/// 组件生命周期接口
/// </summary>
internal interface IActorComponent
{
    Task OnActorStartAsync(CancellationToken cancellationToken);
    Task OnActorStopAsync(CancellationToken cancellationToken);
}

// ========================
// 2. 增强的BaseService - 支持组件
// ========================

/// <summary>
/// 支持组件的Actor服务基类
/// </summary>
public abstract class ComponentBasedService : BaseService, IComponentHost
{
    private ComponentManager<ComponentBasedService>? _componentManager;

    public IServiceProvider ServiceProvider { get; private set; } = null!;

    protected ComponentBasedService(
        ILogger logger,
        IAuthenticationService authService,
        PermissionValidator validator,
        IServiceProvider serviceProvider)
        : base(logger, authService, validator)
    {
        ServiceProvider = serviceProvider;
    }

    /// <summary>
    /// 初始化组件管理器 - 子类在构造函数中调用
    /// </summary>
    protected void InitializeComponents(params Type[] componentTypes)
    {
        _componentManager = new ComponentManager<ComponentBasedService>(
            this,
            Logger,
            ServiceProvider);

        if (componentTypes.Length > 0)
        {
            _componentManager.RegisterComponentTypes(componentTypes);
        }
        else
        {
            // 自动扫描当前程序集
            _componentManager.AutoRegisterComponents(GetType().Assembly);
        }

        _componentManager.InitializeComponents();
    }

    /// <summary>
    /// 获取组件
    /// </summary>
    public TComponent GetComponent<TComponent>() where TComponent : class
    {
        if (_componentManager == null)
        {
            throw new InvalidOperationException(
                "Components not initialized. Call InitializeComponents in constructor.");
        }

        return _componentManager.GetComponent<TComponent>();
    }

    /// <summary>
    /// 尝试获取组件
    /// </summary>
    protected bool TryGetComponent<TComponent>(out TComponent? component) where TComponent : class
    {
        if (_componentManager == null)
        {
            component = null;
            return false;
        }

        return _componentManager.TryGetComponent(out component);
    }

    protected override async Task OnStartAsync(CancellationToken cancellationToken)
    {
        await base.OnStartAsync(cancellationToken);

        if (_componentManager != null)
        {
            await _componentManager.StartComponentsAsync(cancellationToken);
        }
    }

    protected override async Task OnStopAsync(CancellationToken cancellationToken)
    {
        if (_componentManager != null)
        {
            await _componentManager.StopComponentsAsync(cancellationToken);
        }

        await base.OnStopAsync(cancellationToken);
    }

    /// <summary>
    /// 重写方法调用 - 自动路由到组件
    /// </summary>
    protected override async Task ProcessMethodInvocationAsync(MethodInvocationMessage message)
    {
        // TODO: 协议号到方法/组件的映射将由 SourceGenerator 生成
        // 临时实现: 直接调用基类方法，会抛出 NotImplementedException
        try
        {
            await base.ProcessMethodInvocationAsync(message);
        }
        catch (NotImplementedException)
        {
            // 尝试在组件中查找
            if (_componentManager != null)
            {
                var component = _componentManager.FindComponentByMethod(message.ProtocolId);
                if (component != null)
                {
                    // 这里也需要通过 SourceGenerator 生成的映射来获取 MethodInfo
                    throw new NotImplementedException(
                        $"Component-based protocol routing not yet implemented. " +
                        $"SourceGenerator will provide this mapping. ProtocolId: {message.ProtocolId}");
                }
            }

            // 方法未找到
            message.CompletionSource.TrySetException(
                new InvalidOperationException($"Method with ProtocolId '{message.ProtocolId}' not found"));
        }
    }

    private async Task InvokeMethodOnTargetAsync(
        object target,
        MethodInfo methodInfo,
        MethodInvocationMessage message)
    {
        try
        {
            // ✅ 使用表达式树编译调用（性能提升 ~50 倍）
            var result = await CompiledAsyncMethodInvoker.InvokeAsync(target, methodInfo, message.Arguments);
            message.CompletionSource.TrySetResult(result);
        }
        catch (Exception ex)
        {
            var actualException = ex is TargetInvocationException tie
                ? tie.InnerException ?? ex
                : ex;
            message.CompletionSource.TrySetException(actualException);
        }
    }
}
