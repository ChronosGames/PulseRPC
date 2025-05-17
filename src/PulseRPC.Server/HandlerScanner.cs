using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace PulseRPC.Server;

public class HandlerScanner(
    IServiceProvider serviceProvider,
    HandlerRegistry registry,
    ILogger<HandlerScanner> logger)
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;

    // 扫描指定程序集
    public void ScanAssembly(Assembly assembly)
    {
        logger.LogInformation("扫描程序集 {Assembly} 中的消息处理器", assembly.FullName);

        // 找到所有带有PacketHandlerAttribute的类型
        var handlerTypes = assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<HandlerAttribute>() != null);

        foreach (var handlerType in handlerTypes)
        {
            RegisterHandler(handlerType);
        }
    }

    // 扫描所有已加载的程序集
    public void ScanAllAssemblies()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            // 跳过系统和第三方程序集，只扫描应用程序相关程序集
            if (!ShouldScanAssembly(assembly))
            {
                continue;
            }

            ScanAssembly(assembly);
        }
    }

    private bool ShouldScanAssembly(Assembly assembly)
    {
        var name = assembly.GetName().Name!;

        // 跳过系统和第三方程序集
        return !name.StartsWith("System.", StringComparison.Ordinal) &&
               !name.StartsWith("Microsoft.", StringComparison.Ordinal) &&
               !name.StartsWith("JetBrains.", StringComparison.Ordinal) &&
               !name.StartsWith("PulseRPC.", StringComparison.Ordinal) &&
               !name.StartsWith("MemoryPack.", StringComparison.Ordinal) &&
               name != "MemoryPack" &&
               name != "netstandard" &&
               name != "mscorlib";
    }

    // 注册单个处理器
    private void RegisterHandler(Type handlerType)
    {
        var attribute = handlerType.GetCustomAttribute<HandlerAttribute>();
        if (attribute == null)
        {
            return;
        }

        var policy = attribute.ThreadingPolicy;
        var priority = attribute.Priority;

        // 检查各种处理器接口
        foreach (var interfaceType in handlerType.GetInterfaces())
        {
            if (!interfaceType.IsGenericType)
            {
                continue;
            }

            var genericTypeDef = interfaceType.GetGenericTypeDefinition();

            // 注册标准命令处理器
            if (genericTypeDef == typeof(ICommandHandler<>))
            {
                var commandType = interfaceType.GetGenericArguments()[0];

                // 使用反射创建RegisterCommandHandler<TCommand>方法
                var method = typeof(HandlerRegistry)
                    .GetMethod(nameof(HandlerRegistry.RegisterCommandHandler))!
                    .MakeGenericMethod(commandType);

                method.Invoke(registry, [handlerType, policy, priority]);

                logger.LogInformation("已注册命令处理器: {Handler} 用于指令 {Name}", handlerType.Name, commandType.Name);
            }
            // 注册上下文命令处理器
            else if (genericTypeDef == typeof(IContextualCommandHandler<,>))
            {
                var genericArgs = interfaceType.GetGenericArguments();
                var commandType = genericArgs[0];
                var contextType = genericArgs[1];

                // 使用反射创建RegisterContextualCommandHandler<TCommand, TContext>方法
                var method = typeof(HandlerRegistry)
                    .GetMethod(nameof(HandlerRegistry.RegisterContextualCommandHandler))!
                    .MakeGenericMethod(commandType, contextType);

                method.Invoke(registry, [handlerType, policy, priority]);

                logger.LogInformation("已注册上下文命令处理器: {Handler} 用于指令 {CommandName}, 上下文 {ContextName}",
                    handlerType.Name, commandType.Name, contextType.Name);
            }
            // 注册标准请求处理器
            else if (genericTypeDef == typeof(IRequestHandler<,>))
            {
                var genericArgs = interfaceType.GetGenericArguments();
                var requestType = genericArgs[0];
                var responseType = genericArgs[1];

                // 使用反射创建RegisterRequestHandler<TRequest, TResponse>方法
                var method = typeof(HandlerRegistry)
                    .GetMethod(nameof(HandlerRegistry.RegisterRequestHandler))!
                    .MakeGenericMethod(requestType, responseType);

                method.Invoke(registry, [handlerType, policy, priority]);

                logger.LogInformation("已注册请求处理器: {Handler} 用于请求 {RequestName}, 响应 {ResponseName}",
                    handlerType.Name, requestType.Name, responseType.Name);
            }
            // 注册上下文请求处理器
            else if (genericTypeDef == typeof(IContextualRequestHandler<,,>))
            {
                var genericArgs = interfaceType.GetGenericArguments();
                var requestType = genericArgs[0];
                var responseType = genericArgs[1];
                var contextType = genericArgs[2];

                // 使用反射创建RegisterContextualRequestHandler<TRequest, TResponse, TContext>方法
                var method = typeof(HandlerRegistry)
                    .GetMethod(nameof(HandlerRegistry.RegisterContextualRequestHandler))!
                    .MakeGenericMethod(requestType, responseType, contextType);

                method.Invoke(registry, [handlerType, policy, priority]);

                logger.LogInformation("已注册上下文请求处理器: {Handler} 用于请求 {RequestName}, 响应 {ResponseName}, 上下文 {ContextName}",
                    handlerType.Name, requestType.Name, responseType.Name, contextType.Name);
            }
            // 注册扩展请求处理器
            else if (genericTypeDef == typeof(IExtendedRequestHandler<,,,>))
            {
                var genericArgs = interfaceType.GetGenericArguments();
                var requestType = genericArgs[0];
                var responseType = genericArgs[1];
                var optionsType = genericArgs[2];
                var resultType = genericArgs[3];

                // 使用反射创建RegisterExtendedRequestHandler<TRequest, TResponse, TOptions, TResult>方法
                var method = typeof(HandlerRegistry)
                    .GetMethod(nameof(HandlerRegistry.RegisterExtendedRequestHandler))!
                    .MakeGenericMethod(requestType, responseType, optionsType, resultType);

                method.Invoke(registry, [handlerType, policy, priority]);

                logger.LogInformation("已注册扩展请求处理器: {Handler} 用于请求 {RequestName}, 响应 {ResponseName}, 选项 {OptionsName}, 结果 {ResultName}",
                    handlerType.Name, requestType.Name, responseType.Name, optionsType.Name, resultType.Name);
            }
        }
    }
}
