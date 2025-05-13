using System.Reflection;
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
            .Where(t => t.GetCustomAttribute<PacketHandlerAttribute>() != null);

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
        var attribute = handlerType.GetCustomAttribute<PacketHandlerAttribute>();
        if (attribute == null)
        {
            return;
        }

        var messageId = attribute.PacketId;
        var policy = attribute.ThreadingPolicy;
        var priority = attribute.Priority;

        // 检查命令处理器接口
        foreach (var interfaceType in handlerType.GetInterfaces())
        {
            if (!interfaceType.IsGenericType)
            {
                continue;
            }

            var genericTypeDef = interfaceType.GetGenericTypeDefinition();

            // 注册命令处理器
            if (genericTypeDef == typeof(ICommandHandler<>))
            {
                var commandType = interfaceType.GetGenericArguments()[0];

                // 使用反射创建RegisterCommandHandler<TCommand>方法
                var method = typeof(HandlerRegistry)
                    .GetMethod(nameof(HandlerRegistry.RegisterCommandHandler))!
                    .MakeGenericMethod(commandType);

                method.Invoke(registry, [handlerType, messageId, policy, priority]);

                logger.LogInformation("已注册命令处理器: {Handler} 用于消息 {MessageId}", handlerType.Name, messageId);
            }
            // 注册请求处理器
            else if (genericTypeDef == typeof(IRequestHandler<,>))
            {
                var genericArgs = interfaceType.GetGenericArguments();
                var requestType = genericArgs[0];
                var responseType = genericArgs[1];

                // 使用反射创建RegisterRequestHandler<TRequest, TResponse>方法
                var method = typeof(HandlerRegistry)
                    .GetMethod(nameof(HandlerRegistry.RegisterRequestHandler))!
                    .MakeGenericMethod(requestType, responseType);

                method.Invoke(registry, [handlerType, messageId, policy, priority]);

                logger.LogInformation("已注册请求处理器: {Handler} 用于消息 {MessageId}", handlerType.Name, messageId);
            }
        }
    }
}
