using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace PulseRPC.Generator;

[Generator(LanguageNames.CSharp)]
public partial class ServiceProxyGenerator : IIncrementalGenerator
{
    private const string PulseClientGenerationAttributeName = "PulseClientGenerationAttribute";
    private const string PulseServiceAttributeName = "PulseServiceAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 获取配置选项
        var configProvider = context.AnalyzerConfigOptionsProvider;

        // 查找带有 PulseClientGeneration 特性的类
        var classDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => s is ClassDeclarationSyntax { AttributeLists.Count: > 0 },
                transform: (ctx, _) => GetServiceTypesFromClass(ctx))
            .Where(m => m.Length > 0);

        // 分离服务类型和事件类型 - 基于接口实现而非名称
        var allServiceTypes = classDeclarations.Collect()
            .Select((classDeclarationArray, _) =>
            {
                var result = new List<ServiceTypeInfo>();

                foreach (var classDeclaration in classDeclarationArray)
                {
                    foreach (var serviceType in classDeclaration)
                    {
                        // 检查是否实现了IPulseHub接口（服务接口）
                        if (serviceType.Type != null && IsNetworkService(serviceType.Type))
                        {
                            result.Add(serviceType);
                        }
                    }
                }

                return result.ToImmutableArray();
            });

        var allEventTypes = classDeclarations.Collect()
            .Select((classDeclarationArray, _) =>
            {
                var result = new List<ServiceTypeInfo>();

                foreach (var classDeclaration in classDeclarationArray)
                {
                    foreach (var serviceType in classDeclaration)
                    {
                        // 检查是否实现了IPulseEventHandler接口（事件接口）
                        if (serviceType.Type != null && IsEventReceiver(serviceType.Type))
                        {
                            result.Add(serviceType);
                        }
                    }
                }

                return result.ToImmutableArray();
            });

        // 注册服务代理源代码输出
        context.RegisterSourceOutput(allServiceTypes.Combine(configProvider), (spc, combined) =>
        {
            var serviceTypes = combined.Left;

            // 生成服务代理
            foreach (var serviceTypeInfo in serviceTypes)
            {
                if (serviceTypeInfo.Type is INamedTypeSymbol namedType)
                {
                    var proxyCode = GenerateServiceProxy(namedType);
                    var fileName = $"{namedType.Name}Proxy.g.cs";
                    spc.AddSource(fileName, SourceText.From(proxyCode, Encoding.UTF8));
                }
                else
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        new DiagnosticDescriptor(
                            "PRPC101",
                            "无效的服务类型",
                            $"无法为类型 {serviceTypeInfo.Type} 生成代理，因为它不是 INamedTypeSymbol",
                            "PulseRPC",
                            DiagnosticSeverity.Error,
                            true),
                        Location.None));
                }
            }

            // 生成扩展方法
            var namedTypes = serviceTypes.Select(t => t.Type).OfType<INamedTypeSymbol>().ToImmutableArray();
            if (namedTypes.Length > 0)
            {
                var extensionsCode = GenerateChannelManagerExtensions(namedTypes, ImmutableArray<INamedTypeSymbol>.Empty);
                var fileName = "PulseRPC.Services.g.cs";
                spc.AddSource(fileName, SourceText.From(extensionsCode, Encoding.UTF8));
            }
        });

        // 注册事件处理器源代码输出
        context.RegisterSourceOutput(allEventTypes.Combine(configProvider), (spc, combined) =>
        {
            var eventTypes = combined.Left;

            // 生成事件处理器
            foreach (var eventTypeInfo in eventTypes)
            {
                if (eventTypeInfo.Type is INamedTypeSymbol namedType)
                {
                    var handlerCode = GenerateEventHandler(namedType);
                    var handlerFileName = $"{namedType.Name}Handler.g.cs";
                    spc.AddSource(handlerFileName, SourceText.From(handlerCode, Encoding.UTF8));

                    // 生成事件处理器接口
                    var handlerInterfaceCode = GenerateEventHandlerInterface(namedType);
                    var interfaceFileName = $"I{namedType.Name}Handler.g.cs";
                    spc.AddSource(interfaceFileName, SourceText.From(handlerInterfaceCode, Encoding.UTF8));
                }
                else
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        new DiagnosticDescriptor(
                            "PRPC102",
                            "无效的事件类型",
                            $"无法为类型 {eventTypeInfo.Type} 生成事件处理器，因为它不是 INamedTypeSymbol",
                            "PulseRPC",
                            DiagnosticSeverity.Error,
                            true),
                        Location.None));
                }
            }

            // 生成 EventListenerExtensions 的 partial 实现
            var namedTypes = eventTypes.Select(t => t.Type).OfType<INamedTypeSymbol>().ToImmutableArray();
            if (namedTypes.Length > 0)
            {
                var partialImplementationCode = GenerateEventListenerExtensionsPartial(namedTypes);
                var fileName = "PulseRPC.Events.g.cs";
                spc.AddSource(fileName, SourceText.From(partialImplementationCode, Encoding.UTF8));
            }
        });
    }

    private static ServiceTypeInfo[] GetServiceTypesFromClass(GeneratorSyntaxContext context)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
        var semanticModel = context.SemanticModel;

        // 获取类的语义模型
        var classSymbol = semanticModel.GetDeclaredSymbol(classDeclaration);
        if (classSymbol == null)
        {
            return Array.Empty<ServiceTypeInfo>();
        }

        var result = new List<ServiceTypeInfo>();

        // 查找 PulseClientGeneration 特性
        foreach (var attribute in classSymbol.GetAttributes())
        {
            if (attribute.AttributeClass?.Name != PulseClientGenerationAttributeName)
            {
                continue;
            }

            // 获取特性的参数
            if (attribute.ConstructorArguments.Length != 1)
            {
                continue;
            }

            if (attribute.ConstructorArguments[0].Value is not INamedTypeSymbol serviceType)
            {
                continue;
            }

            result.Add(new ServiceTypeInfo(serviceType));
        }

        return result.ToArray();
    }

    private static bool IsNetworkService(INamedTypeSymbol typeSymbol)
    {
        // 检查是否实现了 IPulseHub 接口
        return typeSymbol.AllInterfaces.Any(i => i.Name == "IPulseHub");
    }

    private static bool IsEventReceiver(INamedTypeSymbol typeSymbol)
    {
        // 检查是否实现了 IPulseEventHandler 接口
        return typeSymbol.AllInterfaces.Any(i => i.Name == "IPulseEventHandler");
    }

    private static string GenerateServiceProxy(INamedTypeSymbol interfaceSymbol)
    {
        var interfaceName = interfaceSymbol.Name;
        var namespaceName = interfaceSymbol.ContainingNamespace.ToDisplayString();

        // 获取通道特性
        var defaultChannelName = GetChannelAttributeValue(interfaceSymbol) ?? "default";

        var sb = new StringBuilder();

        // 生成文件头
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using PulseRPC;");
        sb.AppendLine("using PulseRPC.Transport;");
        sb.AppendLine("using PulseRPC.Messaging;");
        sb.AppendLine("using PulseRPC.Serialization;");
        sb.AppendLine("using PulseRPC.Client.Channels;");
        sb.AppendLine("using System.Runtime.CompilerServices;");
        sb.AppendLine();

        // 生成命名空间
        sb.AppendLine($"namespace {namespaceName}");
        sb.AppendLine("{");

        // 生成高性能代理类
        sb.AppendLine($"    /// <summary>");
        sb.AppendLine($"    /// 高性能 {interfaceName} 服务代理 - 零分配设计");
        sb.AppendLine($"    /// 使用编译时优化和类型缓存");
        sb.AppendLine($"    /// </summary>");
        sb.AppendLine($"    public sealed class {interfaceName}Proxy : {interfaceName}");
        sb.AppendLine("    {");
        sb.AppendLine("        private readonly IClientChannel _channel;");
        sb.AppendLine();
        sb.AppendLine($"        /// <summary>");
        sb.AppendLine($"        /// 初始化高性能 {interfaceName} 服务代理");
        sb.AppendLine($"        /// </summary>");
        sb.AppendLine($"        /// <param name=\"channelManager\">通道管理器</param>");
        sb.AppendLine($"        public {interfaceName}Proxy(IChannelManager channelManager)");
        sb.AppendLine("        {");
        sb.AppendLine($"            _channel = (channelManager ?? throw new ArgumentNullException(nameof(channelManager))).GetChannel(\"{defaultChannelName}\");");
        sb.AppendLine("        }");
        sb.AppendLine();

        // 生成高性能方法实现
        foreach (var member in interfaceSymbol.GetMembers())
        {
            if (member is not IMethodSymbol methodSymbol)
                continue;

            // 自动处理所有公共方法
            if (methodSymbol.DeclaredAccessibility != Accessibility.Public)
                continue;

            // 生成高性能方法实现
            GenerateHighPerformanceMethodImplementation(sb, methodSymbol, defaultChannelName, namespaceName, interfaceName);
        }

        // 结束类和命名空间
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static void GenerateHighPerformanceMethodImplementation(
        StringBuilder sb,
        IMethodSymbol methodSymbol,
        string defaultChannelName,
        string namespaceName,
        string interfaceName)
    {
        var methodName = methodSymbol.Name;
        var returnType = methodSymbol.ReturnType.ToDisplayString();

        // 获取方法级别的通道特性，如果没有则使用接口级别的默认通道
        var methodChannelName = GetChannelAttributeValue(methodSymbol) ?? defaultChannelName;

        sb.AppendLine($"        /// <inheritdoc/>");

        // 生成方法签名
        sb.Append($"        public async {returnType} {methodName}(");

        // 生成参数列表
        var parameters = methodSymbol.Parameters;
        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            if (i > 0)
                sb.Append(", ");

            sb.Append($"{parameter.Type.ToDisplayString()} {parameter.Name}");

            if (parameter.HasExplicitDefaultValue)
            {
                // 特殊处理CancellationToken类型的默认值
                if (parameter.Type.ToDisplayString() == "System.Threading.CancellationToken" ||
                    parameter.Type.ToDisplayString() == "CancellationToken")
                {
                    sb.Append(" = default");
                }
                else
                {
                    switch (parameter.ExplicitDefaultValue)
                    {
                        case null:
                            // 对于值类型，使用default；对于引用类型，使用null
                            if (parameter.Type.IsValueType)
                                sb.Append(" = default");
                            else
                                sb.Append(" = null");
                            break;
                        case string strValue:
                            sb.Append($" = \"{strValue}\"");
                            break;
                        default:
                            sb.Append($" = {parameter.ExplicitDefaultValue}");
                            break;
                    }
                }
            }
        }

        sb.AppendLine(")");
        sb.AppendLine("        {");

        // 添加性能优化注释
        sb.AppendLine($"            // 高性能路径: 直接使用预缓存的通道");

        // 确定取消令牌
        var cancelTokenParam = parameters.FirstOrDefault(p =>
            p.Type.ToDisplayString() == "System.Threading.CancellationToken" ||
            p.Type.ToDisplayString() == "CancellationToken");

        var tokenName = cancelTokenParam?.Name ?? "CancellationToken.None";

        // 查找请求参数 (通常是第一个非取消令牌参数)
        var requestParam = parameters.FirstOrDefault(p =>
            p.Type.ToDisplayString() != "System.Threading.CancellationToken" &&
            p.Type.ToDisplayString() != "CancellationToken");

        var requestName = requestParam?.Name ?? "new {}";
        var requestType = requestParam?.Type.ToDisplayString() ?? "object";

        // 确定返回类型
        var isVoid = returnType is "System.Threading.Tasks.Task" or "Task";
        var isValueTask = returnType.StartsWith("System.Threading.Tasks.ValueTask<") || returnType.StartsWith("ValueTask<");
        var isTask = returnType.StartsWith("System.Threading.Tasks.Task<") || returnType.StartsWith("Task<");
        var isValueTaskVoid = returnType is "System.Threading.Tasks.ValueTask" or "ValueTask";

        // 获取返回值类型
        var responseType = "PulseRPC.EmptyResponse";
        if (isValueTask || isTask)
        {
            responseType = ExtractGenericType(returnType);
        }

        // 生成高性能方法调用
        if (isVoid || isValueTaskVoid)
        {
            sb.AppendLine($"            await _channel.SendRequestAsync<{requestType}, {responseType}>(");
            sb.AppendLine($"                \"{namespaceName}.{interfaceName}\",");
            sb.AppendLine($"                \"{methodName}\",");
            sb.AppendLine($"                {requestName},");
            sb.AppendLine($"                {tokenName});");
        }
        else
        {
            sb.AppendLine($"            return await _channel.SendRequestAsync<{requestType}, {responseType}>(");
            sb.AppendLine($"                \"{namespaceName}.{interfaceName}\",");
            sb.AppendLine($"                \"{methodName}\",");
            sb.AppendLine($"                {requestName},");
            sb.AppendLine($"                {tokenName});");
        }

        sb.AppendLine("        }");
        sb.AppendLine();
    }

    private static string GenerateEventHandler(INamedTypeSymbol interfaceSymbol)
    {
        var interfaceName = interfaceSymbol.Name;
        var namespaceName = interfaceSymbol.ContainingNamespace.ToDisplayString();

        // 确保生成的接口名称不重复I前缀
        var handlerInterfaceName = "I" + (interfaceName.StartsWith("I")
            ? interfaceName.Substring(1) + "Handler"
            : interfaceName + "Handler");

        var handlerClassName = interfaceName.StartsWith("I")
            ? interfaceName.Substring(1) + "Handler"
            : interfaceName + "Handler";

        var sb = new StringBuilder();

        // 生成文件头
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using PulseRPC;");
        sb.AppendLine("using PulseRPC.Messaging;");
        sb.AppendLine();

        // 生成命名空间
        sb.AppendLine($"namespace {namespaceName}");
        sb.AppendLine("{");

        // 生成实现类
        sb.AppendLine($"    /// <summary>");
        sb.AppendLine($"    /// {interfaceName} 事件处理器实现");
        sb.AppendLine($"    /// </summary>");
        sb.AppendLine($"    public class {handlerClassName} : {handlerInterfaceName}");
        sb.AppendLine("    {");
        sb.AppendLine("        private readonly IClientChannel _channel;");
        sb.AppendLine($"        private readonly Dictionary<{interfaceName}, List<ISubscriptionToken>> _subscriptions = new Dictionary<{interfaceName}, List<ISubscriptionToken>>();");
        sb.AppendLine();
        sb.AppendLine($"        /// <summary>");
        sb.AppendLine($"        /// 初始化 {interfaceName} 事件处理器");
        sb.AppendLine($"        /// </summary>");
        sb.AppendLine($"        public {handlerClassName}(IClientChannel channel)");
        sb.AppendLine("        {");
        sb.AppendLine("            _channel = channel ?? throw new ArgumentNullException(nameof(channel));");
        sb.AppendLine("        }");
        sb.AppendLine();

        // 生成Subscribe方法
        sb.AppendLine($"        /// <inheritdoc/>");
        sb.AppendLine($"        public ISubscriptionToken Subscribe({interfaceName} subscriber)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (subscriber == null)");
        sb.AppendLine("                throw new ArgumentNullException(nameof(subscriber));");
        sb.AppendLine();
        sb.AppendLine("            var tokens = new List<ISubscriptionToken>();");
        sb.AppendLine();

        // 处理每个事件方法
        var hasEvents = false;

        // 用于跟踪已处理的方法名，防止重复
        var processedMethods = new HashSet<string>();

        foreach (var member in interfaceSymbol.GetMembers())
        {
            if (member is not IMethodSymbol methodSymbol)
                continue;

            // 自动处理所有公共方法，不再检查 Event 特性
            if (methodSymbol.DeclaredAccessibility != Accessibility.Public)
                continue;

            // 需要确保有一个参数
            if (methodSymbol.Parameters.Length != 1)
                continue;

            hasEvents = true;
            var eventMethod = methodSymbol.Name;
            var eventType = methodSymbol.Parameters[0].Type.ToDisplayString();

            // 如果方法名已经处理过，则修改变量名以避免冲突
            var tokenVarName = eventMethod + "Token";
            if (processedMethods.Contains(eventMethod))
            {
                tokenVarName = eventMethod + "_" + eventType.Replace(".", "_") + "Token";
            }
            else
            {
                processedMethods.Add(eventMethod);
            }

            sb.AppendLine($"            // 订阅 {eventMethod} 事件");
            sb.AppendLine($"            var {tokenVarName} = _channel.SubscribeToEvent<{eventType}>(\"{eventMethod}\",");
            sb.AppendLine($"                (System.EventHandler<{eventType}>)((sender, eventData) => subscriber.{eventMethod}(eventData)));");
            sb.AppendLine($"            tokens.Add({tokenVarName});");
            sb.AppendLine();
        }

        if (!hasEvents)
        {
            sb.AppendLine("            // 没有找到事件方法");
        }

        sb.AppendLine("            // 创建组合订阅令牌");
        sb.AppendLine("            var compositeToken = new CompositeSubscriptionToken(tokens);");
        sb.AppendLine("            _subscriptions[subscriber] = tokens;");
        sb.AppendLine("            return compositeToken;");
        sb.AppendLine("        }");
        sb.AppendLine();

        // 生成Unsubscribe方法
        sb.AppendLine($"        /// <inheritdoc/>");
        sb.AppendLine($"        public void Unsubscribe({interfaceName} subscriber)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (subscriber == null)");
        sb.AppendLine("                throw new ArgumentNullException(nameof(subscriber));");
        sb.AppendLine();
        sb.AppendLine("            if (_subscriptions.TryGetValue(subscriber, out var tokens))");
        sb.AppendLine("            {");
        sb.AppendLine("                foreach (var token in tokens)");
        sb.AppendLine("                {");
        sb.AppendLine("                    token.Unsubscribe();");
        sb.AppendLine("                }");
        sb.AppendLine();
        sb.AppendLine("                _subscriptions.Remove(subscriber);");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine();

        // 组合订阅令牌
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// 组合订阅令牌");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        private class CompositeSubscriptionToken : ISubscriptionToken");
        sb.AppendLine("        {");
        sb.AppendLine("            private readonly List<ISubscriptionToken> _tokens;");
        sb.AppendLine("            private bool _isDisposed;");
        sb.AppendLine();
        sb.AppendLine("            public Guid Id { get; } = Guid.NewGuid();");
        sb.AppendLine("            public bool IsActive => !_isDisposed;");
        sb.AppendLine("            public bool IsUnsubscribed => _isDisposed;");
        sb.AppendLine();
        sb.AppendLine("            public CompositeSubscriptionToken(List<ISubscriptionToken> tokens)");
        sb.AppendLine("            {");
        sb.AppendLine("                _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            public void Unsubscribe()");
        sb.AppendLine("            {");
        sb.AppendLine("                if (_isDisposed)");
        sb.AppendLine("                    return;");
        sb.AppendLine();
        sb.AppendLine("                foreach (var token in _tokens)");
        sb.AppendLine("                {");
        sb.AppendLine("                    token.Unsubscribe();");
        sb.AppendLine("                }");
        sb.AppendLine();
        sb.AppendLine("                _isDisposed = true;");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            public void Dispose()");
        sb.AppendLine("            {");
        sb.AppendLine("                Unsubscribe();");
        sb.AppendLine("                GC.SuppressFinalize(this);");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string GenerateEventListenerExtensionsPartial(ImmutableArray<INamedTypeSymbol> eventTypes)
    {
        var sb = new StringBuilder();

        // 生成文件头
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using PulseRPC.Transport;");
        sb.AppendLine("using PulseRPC;");
        sb.AppendLine("using PulseRPC.Client.Events;");
        sb.AppendLine();

        // 生成命名空间
        sb.AppendLine("namespace PulseRPC.Client");
        sb.AppendLine("{");

        // 生成完整的 EventListenerExtensions 类
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// 事件监听器扩展方法 - 统一命名，零反射实现");
        sb.AppendLine("    /// 此类完全由源代码生成器实现，提供高性能的事件监听器注册");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public static class EventListenerExtensions");
        sb.AppendLine("    {");

        // 添加静态构造函数来初始化委托
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// 静态构造函数，初始化事件监听器注册器");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        static EventListenerExtensions()");
        sb.AppendLine("        {");
        sb.AppendLine("            // 初始化 EventListenerRegistrar 的委托");
        sb.AppendLine("            EventListenerRegistrar.RegisterWithConfigurationDelegate = ");
        sb.AppendLine("                (client, listener, config) => RegisterEventListenerWithConfigurationGenerated(client, listener, config);");
        sb.AppendLine("        }");
        sb.AppendLine();

        // 生成智能分发方法
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// 源代码生成器生成的智能事件监听器注册分发器");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        private static ISubscriptionToken RegisterEventListenerGenerated<T>(IPulseRPCClient client, T listener) where T : class");
        sb.AppendLine("        {");
        sb.AppendLine("            var tokens = new List<ISubscriptionToken>();");
        sb.AppendLine("            var channelManager = client.GetChannelManager();");
        sb.AppendLine();

        // 为每个事件接口生成类型检查和注册逻辑
        foreach (var interfaceSymbol in eventTypes)
        {
            if (interfaceSymbol == null) continue;

            var fullTypeName = GetFullTypeName(interfaceSymbol);
            var channelName = GetChannelAttributeValue(interfaceSymbol) ?? "default";

            sb.AppendLine($"            // 检查是否实现了 {interfaceSymbol.Name}");
            sb.AppendLine($"            if (listener is {fullTypeName} {interfaceSymbol.Name.ToLower()}Listener)");
            sb.AppendLine("            {");
            sb.AppendLine($"                var channel = channelManager.GetChannel(\"{channelName}\");");

            // 为每个事件方法生成订阅代码
            foreach (var member in interfaceSymbol.GetMembers())
            {
                if (member is not IMethodSymbol methodSymbol)
                    continue;

                if (methodSymbol.DeclaredAccessibility != Accessibility.Public)
                    continue;

                if (methodSymbol.Parameters.Length != 1)
                    continue;

                var eventMethod = methodSymbol.Name;
                var eventType = methodSymbol.Parameters[0].Type.ToDisplayString();

                sb.AppendLine($"                tokens.Add(channel.SubscribeToEvent<{eventType}>(\"{eventMethod}\",");
                sb.AppendLine($"                    (System.EventHandler<{eventType}>)((sender, eventData) => {interfaceSymbol.Name.ToLower()}Listener.{eventMethod}(eventData))));");
            }

            sb.AppendLine("            }");
            sb.AppendLine();
        }

        sb.AppendLine("            if (tokens.Count == 0)");
        sb.AppendLine("            {");
        sb.AppendLine("                throw new ArgumentException($\"Type {typeof(T).Name} does not implement any recognized event listener interfaces. Make sure it implements interfaces registered with [PulseClientGeneration] attribute.\");");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            return new CompositeSubscriptionToken(tokens);");
        sb.AppendLine("        }");
        sb.AppendLine();

        // 生成配置版本
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// 源代码生成器生成的配置版事件监听器注册分发器");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        private static ISubscriptionToken RegisterEventListenerWithConfigurationGenerated<T>(IPulseRPCClient client, T listener, EventListenerConfiguration configuration) where T : class");
        sb.AppendLine("        {");
        sb.AppendLine("            var tokens = new List<ISubscriptionToken>();");
        sb.AppendLine("            var channelManager = client.GetChannelManager();");
        sb.AppendLine();

        // 为每个事件接口生成配置版的类型检查和注册逻辑
        foreach (var interfaceSymbol in eventTypes)
        {
            if (interfaceSymbol == null) continue;

            var fullTypeName = GetFullTypeName(interfaceSymbol);
            var channelName = GetChannelAttributeValue(interfaceSymbol) ?? "default";

            sb.AppendLine($"            // 检查是否实现了 {interfaceSymbol.Name}");
            sb.AppendLine($"            if (listener is {fullTypeName} {interfaceSymbol.Name.ToLower()}Listener)");
            sb.AppendLine("            {");
            sb.AppendLine($"                var targetChannelName = configuration.ChannelName ?? \"{channelName}\";");
            sb.AppendLine("                var channel = channelManager.GetChannel(targetChannelName);");

            // 为每个事件方法生成带配置的订阅代码
            foreach (var member in interfaceSymbol.GetMembers())
            {
                if (member is not IMethodSymbol methodSymbol)
                    continue;

                if (methodSymbol.DeclaredAccessibility != Accessibility.Public)
                    continue;

                if (methodSymbol.Parameters.Length != 1)
                    continue;

                var eventMethod = methodSymbol.Name;
                var eventType = methodSymbol.Parameters[0].Type.ToDisplayString();

                sb.AppendLine($"                tokens.Add(channel.SubscribeToEvent<{eventType}>(\"{eventMethod}\", (System.EventHandler<{eventType}>)(async (sender, eventData) =>");
                sb.AppendLine("                {");
                sb.AppendLine("                    try");
                sb.AppendLine("                    {");
                sb.AppendLine($"                        // 检查事件过滤器");
                sb.AppendLine($"                        if (configuration.EventFilter != null && !configuration.EventFilter(\"{eventMethod}\"))");
                sb.AppendLine($"                            return;");
                sb.AppendLine();
                sb.AppendLine($"                        // 检查数据过滤器");
                sb.AppendLine($"                        if (configuration.DataFilters?.TryGetValue(\"{eventMethod}\", out var dataFilter) == true)");
                sb.AppendLine($"                        {{");
                sb.AppendLine($"                            if (!dataFilter(eventData))");
                sb.AppendLine($"                                return;");
                sb.AppendLine($"                        }}");
                sb.AppendLine();
                sb.AppendLine($"                        // 调用事件处理方法");
                sb.AppendLine($"                        {interfaceSymbol.Name.ToLower()}Listener.{eventMethod}(eventData);");
                sb.AppendLine("                    }");
                sb.AppendLine("                    catch (Exception ex)");
                sb.AppendLine("                    {");
                sb.AppendLine("                        // 错误处理");
                sb.AppendLine("                        if (configuration.ErrorHandler != null)");
                sb.AppendLine($"                            await configuration.ErrorHandler(ex, \"{eventMethod}\", eventData, 1);");
                sb.AppendLine("                        else");
                sb.AppendLine($"                            Console.WriteLine($\"Event {eventMethod} failed: {{ex.Message}}\");");
                sb.AppendLine("                    }");
                sb.AppendLine("                })));");
            }

            sb.AppendLine("            }");
            sb.AppendLine();
        }

        sb.AppendLine("            if (tokens.Count == 0)");
        sb.AppendLine("            {");
        sb.AppendLine("                throw new ArgumentException($\"Type {typeof(T).Name} does not implement any recognized event listener interfaces. Make sure it implements interfaces registered with [PulseClientGeneration] attribute.\");");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            return new CompositeSubscriptionToken(tokens);");
        sb.AppendLine("        }");


        // 生成简单的组合订阅令牌类
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// 组合订阅令牌 - 管理多个事件订阅");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        private class CompositeSubscriptionToken : ISubscriptionToken");
        sb.AppendLine("        {");
        sb.AppendLine("            private readonly List<ISubscriptionToken> _tokens;");
        sb.AppendLine("            private bool _isDisposed;");
        sb.AppendLine();
        sb.AppendLine("            public Guid Id { get; } = Guid.NewGuid();");
        sb.AppendLine("            public bool IsActive => !_isDisposed;");
        sb.AppendLine("            public bool IsUnsubscribed => _isDisposed;");
        sb.AppendLine();
        sb.AppendLine("            public CompositeSubscriptionToken(List<ISubscriptionToken> tokens)");
        sb.AppendLine("            {");
        sb.AppendLine("                _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            public void Unsubscribe()");
        sb.AppendLine("            {");
        sb.AppendLine("                if (_isDisposed)");
        sb.AppendLine("                    return;");
        sb.AppendLine();
        sb.AppendLine("                foreach (var token in _tokens)");
        sb.AppendLine("                {");
        sb.AppendLine("                    try");
        sb.AppendLine("                    {");
        sb.AppendLine("                        token.Unsubscribe();");
        sb.AppendLine("                    }");
        sb.AppendLine("                    catch (Exception ex)");
        sb.AppendLine("                    {");
        sb.AppendLine("                        Console.WriteLine($\"Error unsubscribing token {token.Id}: {ex.Message}\");");
        sb.AppendLine("                    }");
        sb.AppendLine("                }");
        sb.AppendLine();
        sb.AppendLine("                _isDisposed = true;");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            public void Dispose()");
        sb.AppendLine("            {");
        sb.AppendLine("                Unsubscribe();");
        sb.AppendLine("                GC.SuppressFinalize(this);");
        sb.AppendLine("            }");
        sb.AppendLine("        }");

        // 生成公共API方法
        GeneratePublicApiMethods(sb, eventTypes);

        // 结束类和命名空间
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static void GeneratePublicApiMethods(StringBuilder sb, ImmutableArray<INamedTypeSymbol> eventTypes)
    {
        sb.AppendLine();

        // 生成主要的简洁API方法
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// 注册事件监听器 - 使用默认配置（简单场景）");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        /// <typeparam name=\"T\">事件监听器类型，必须实现IPulseEventHandler</typeparam>");
        sb.AppendLine("        /// <param name=\"client\">PulseRPC客户端</param>");
        sb.AppendLine("        /// <param name=\"listener\">事件监听器实例</param>");
        sb.AppendLine("        /// <returns>订阅令牌</returns>");
        sb.AppendLine("        public static ISubscriptionToken RegisterEventListener<T>(this IPulseRPCClient client, T listener) where T : class, IPulseEventHandler");
        sb.AppendLine("        {");
        sb.AppendLine("            if (client == null) throw new ArgumentNullException(nameof(client));");
        sb.AppendLine("            if (listener == null) throw new ArgumentNullException(nameof(listener));");
        sb.AppendLine("            return RegisterEventListenerGenerated(client, listener);");
        sb.AppendLine("        }");
        sb.AppendLine();

        // 生成配置方法
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// 配置事件监听器 - 提供高级配置选项（高级场景）");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        /// <typeparam name=\"T\">事件监听器类型，必须实现IPulseEventHandler</typeparam>");
        sb.AppendLine("        /// <param name=\"client\">PulseRPC客户端</param>");
        sb.AppendLine("        /// <param name=\"listener\">事件监听器实例</param>");
        sb.AppendLine("        /// <returns>事件监听器构建器</returns>");
        sb.AppendLine("        public static EventListenerBuilder<T> ConfigureEventListener<T>(this IPulseRPCClient client, T listener) where T : class, IPulseEventHandler");
        sb.AppendLine("        {");
        sb.AppendLine("            if (client == null) throw new ArgumentNullException(nameof(client));");
        sb.AppendLine("            if (listener == null) throw new ArgumentNullException(nameof(listener));");
        sb.AppendLine("            return new EventListenerBuilder<T>(client, listener);");
        sb.AppendLine("        }");
        sb.AppendLine();

        // 生成带配置的注册方法
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// 使用指定配置注册事件监听器");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        /// <typeparam name=\"T\">事件监听器类型</typeparam>");
        sb.AppendLine("        /// <param name=\"client\">PulseRPC客户端</param>");
        sb.AppendLine("        /// <param name=\"listener\">事件监听器实例</param>");
        sb.AppendLine("        /// <param name=\"configuration\">配置对象</param>");
        sb.AppendLine("        /// <returns>订阅令牌</returns>");
        sb.AppendLine("        public static ISubscriptionToken RegisterEventListener<T>(this IPulseRPCClient client, T listener, EventListenerConfiguration configuration) where T : class, IPulseEventHandler");
        sb.AppendLine("        {");
        sb.AppendLine("            if (client == null) throw new ArgumentNullException(nameof(client));");
        sb.AppendLine("            if (listener == null) throw new ArgumentNullException(nameof(listener));");
        sb.AppendLine("            if (configuration == null) throw new ArgumentNullException(nameof(configuration));");
        sb.AppendLine("            return RegisterEventListenerWithConfigurationGenerated(client, listener, configuration);");
        sb.AppendLine("        }");
        sb.AppendLine();

        // 生成内部方法，由 EventListenerBuilder 调用
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// 内部方法：使用配置注册事件监听器（由EventListenerBuilder调用）");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        internal static ISubscriptionToken RegisterEventListenerWithConfiguration<T>(IPulseRPCClient client, T listener, EventListenerConfiguration configuration) where T : class");
        sb.AppendLine("        {");
        sb.AppendLine("            return RegisterEventListenerWithConfigurationGenerated(client, listener, configuration);");
        sb.AppendLine("        }");
        sb.AppendLine();

        // 生成快捷方法
        GenerateShortcutMethods(sb);

        // 生成预设方法
        GeneratePresetMethods(sb);
    }

    private static void GenerateShortcutMethods(StringBuilder sb)
    {
        // 快捷方法：指定通道
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// 快捷方法：注册事件监听器并指定通道");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        public static ISubscriptionToken RegisterEventListenerOnChannel<T>(this IPulseRPCClient client, T listener, string channelName) where T : class, IPulseEventHandler");
        sb.AppendLine("        {");
        sb.AppendLine("            return client.ConfigureEventListener(listener).WithChannel(channelName).Register();");
        sb.AppendLine("        }");
        sb.AppendLine();

        // 快捷方法：错误处理
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// 快捷方法：注册事件监听器并设置错误处理");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        public static ISubscriptionToken RegisterEventListenerWithErrorHandler<T>(this IPulseRPCClient client, T listener, Action<Exception, string> errorHandler) where T : class, IPulseEventHandler");
        sb.AppendLine("        {");
        sb.AppendLine("            return client.ConfigureEventListener(listener).WithErrorHandler(errorHandler).Register();");
        sb.AppendLine("        }");
        sb.AppendLine();

        // 快捷方法：重试
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// 快捷方法：注册具有重试能力的事件监听器");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        public static ISubscriptionToken RegisterEventListenerWithRetry<T>(this IPulseRPCClient client, T listener, int maxRetries = 3) where T : class, IPulseEventHandler");
        sb.AppendLine("        {");
        sb.AppendLine("            return client.ConfigureEventListener(listener).WithRetry(maxRetries).Register();");
        sb.AppendLine("        }");
        sb.AppendLine();
    }

    private static void GeneratePresetMethods(StringBuilder sb)
    {
        // 游戏场景预设
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// 游戏场景预设：低延迟，快速重试，容错处理");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        public static ISubscriptionToken RegisterGameEventListener<T>(this IPulseRPCClient client, T listener) where T : class, IPulseEventHandler");
        sb.AppendLine("        {");
        sb.AppendLine("            return client.ConfigureEventListener(listener).WithGameSettings().Register();");
        sb.AppendLine("        }");
        sb.AppendLine();

        // 关键业务场景预设
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// 关键业务场景预设：高可靠性，持久重试，严格错误处理");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        public static ISubscriptionToken RegisterCriticalEventListener<T>(this IPulseRPCClient client, T listener) where T : class, IPulseEventHandler");
        sb.AppendLine("        {");
        sb.AppendLine("            return client.ConfigureEventListener(listener).WithCriticalSettings().Register();");
        sb.AppendLine("        }");
        sb.AppendLine();

        // 调试场景预设
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// 开发调试场景预设：详细日志，性能监控，错误详情");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        public static ISubscriptionToken RegisterDebugEventListener<T>(this IPulseRPCClient client, T listener) where T : class, IPulseEventHandler");
        sb.AppendLine("        {");
        sb.AppendLine("            return client.ConfigureEventListener(listener)");
        sb.AppendLine("                .WithPerformanceMonitoring()");
        sb.AppendLine("                .WithErrorHandler((ex, eventName) => Console.WriteLine($\"[DEBUG] Event {eventName} failed: {ex}\"))");
        sb.AppendLine("                .Register();");
        sb.AppendLine("        }");
        sb.AppendLine();
    }

    private static string GenerateChannelManagerExtensions(
        ImmutableArray<INamedTypeSymbol> serviceTypes,
        ImmutableArray<INamedTypeSymbol> eventTypes)
    {
        var sb = new StringBuilder();

        // 生成文件头
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using PulseRPC.Transport;");
        sb.AppendLine("using PulseRPC;");
        sb.AppendLine("using PulseRPC.Events;");
        sb.AppendLine("using PulseRPC.Client.Events;");
        sb.AppendLine("using PulseRPC.SmartConnection;");
        sb.AppendLine("using PulseRPC.Routing;");
        sb.AppendLine();

        // 生成命名空间
        sb.AppendLine("namespace PulseRPC.Client");
        sb.AppendLine("{");

        // 生成扩展类
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// 通道管理器扩展方法");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine($"    public static class ServiceManagerExtensions");
        sb.AppendLine("    {");

        // 生成通用的 GetService<T>() 方法
        if (serviceTypes.Length > 0)
        {
            // 为 IChannelManager 生成 GetService<T>() 方法
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// 获取指定类型的服务");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        /// <typeparam name=\"T\">服务接口类型</typeparam>");
            sb.AppendLine("        /// <param name=\"channelManager\">通道管理器</param>");
            sb.AppendLine("        /// <returns>服务实例</returns>");
            sb.AppendLine("        private static T GetService<T>(this IChannelManager channelManager) where T : IPulseHub");
            sb.AppendLine("        {");
            sb.AppendLine("            if (channelManager == null)");
            sb.AppendLine("                throw new ArgumentNullException(nameof(channelManager));");
            sb.AppendLine();

            // 为每个服务类型生成 if-else 分支
            for (int i = 0; i < serviceTypes.Length; i++)
            {
                var interfaceSymbol = serviceTypes[i];
                if (interfaceSymbol == null) continue;

                var fullTypeName = GetFullTypeName(interfaceSymbol);

                if (i == 0)
                {
                    sb.AppendLine($"            if (typeof(T) == typeof({fullTypeName}))");
                }
                else
                {
                    sb.AppendLine($"            else if (typeof(T) == typeof({fullTypeName}))");
                }
                sb.AppendLine($"                return (T)(object)new {fullTypeName}Proxy(channelManager);");
            }

            sb.AppendLine();
            sb.AppendLine("            throw new ArgumentException($\"未找到服务代理方法: {{typeof(T).Name}}\", nameof(T));");
            sb.AppendLine("        }");
            sb.AppendLine();

            // 为 IPulseRPCClient 生成 GetService<T>() 方法
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// 获取指定类型的服务");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        /// <typeparam name=\"T\">服务接口类型</typeparam>");
            sb.AppendLine("        /// <param name=\"client\">PulseRPC 客户端</param>");
            sb.AppendLine("        /// <returns>服务实例</returns>");
            sb.AppendLine("        public static T GetService<T>(this IPulseRPCClient client) where T : IPulseHub");
            sb.AppendLine("        {");
            sb.AppendLine("            if (client == null)");
            sb.AppendLine("                throw new ArgumentNullException(nameof(client));");
            sb.AppendLine();
            sb.AppendLine("            return client.GetChannelManager().GetService<T>();");
            sb.AppendLine("        }");
            sb.AppendLine();

            // 生成 GetServiceInternal<T>() 方法供 PulseRPCClient 内部使用
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// 内部方法：获取服务代理 - 由 PulseRPCClient 调用");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        internal static T GetServiceInternal<T>(this IPulseRPCClient client) where T : class, IPulseHub");
            sb.AppendLine("        {");
            sb.AppendLine("            var channelManager = client.GetChannelManager();");
            sb.AppendLine("            return channelManager.GetService<T>();");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        // 为 IPulseRPCClient 生成 GetServiceAsync<T>() 方法
        if (serviceTypes.Length > 0)
        {
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// 获取服务代理 - 智能连接管理");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        /// <typeparam name=\"T\">服务接口类型</typeparam>");
            sb.AppendLine("        /// <param name=\"client\">智能 PulseRPC 客户端</param>");
            sb.AppendLine("        /// <param name=\"serviceName\">服务名称</param>");
            sb.AppendLine("        /// <param name=\"options\">连接选项</param>");
            sb.AppendLine("        /// <returns>服务代理</returns>");
            sb.AppendLine("        public static Task<T> GetServiceAsync<T>(this IPulseRPCClient client, string serviceName = \"\", SmartConnectionOptions? options = null) where T : class, IPulseHub");
            sb.AppendLine("        {");
            sb.AppendLine("            if (client == null)");
            sb.AppendLine("                throw new ArgumentNullException(nameof(client));");
            sb.AppendLine();

            // 为每个服务类型生成 if-else 分支
            for (int i = 0; i < serviceTypes.Length; i++)
            {
                var interfaceSymbol = serviceTypes[i];
                if (interfaceSymbol == null) continue;

                var fullTypeName = GetFullTypeName(interfaceSymbol);
                var serviceNameHint = interfaceSymbol.Name.TrimStart('I');

                if (i == 0)
                {
                    sb.AppendLine($"            if (typeof(T) == typeof({fullTypeName}))");
                }
                else
                {
                    sb.AppendLine($"            else if (typeof(T) == typeof({fullTypeName}))");
                }
                sb.AppendLine("            {");
                sb.AppendLine($"                var effectiveServiceName = string.IsNullOrEmpty(serviceName) ? \"{serviceNameHint}\" : serviceName;");
                sb.AppendLine($"                return client.GetServiceAsync<T>(effectiveServiceName, options);");
                sb.AppendLine("            }");
            }

            sb.AppendLine();
            sb.AppendLine("            throw new ArgumentException($\"未找到智能服务代理方法: {{typeof(T).Name}}\", nameof(T));");
            sb.AppendLine("        }");
            sb.AppendLine();

            // 生成多实例服务管理器方法
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// 获取多实例服务管理器");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        /// <typeparam name=\"T\">服务接口类型</typeparam>");
            sb.AppendLine("        /// <param name=\"client\">智能 PulseRPC 客户端</param>");
            sb.AppendLine("        /// <param name=\"serviceName\">服务名称</param>");
            sb.AppendLine("        /// <param name=\"options\">连接选项</param>");
            sb.AppendLine("        /// <returns>多实例服务管理器</returns>");
            sb.AppendLine("        public static Task<IMultiInstanceServiceManager<T>> GetMultiInstanceServiceAsync<T>(this IPulseRPCClient client, string serviceName = \"\", SmartConnectionOptions? options = null) where T : class, IPulseHub");
            sb.AppendLine("        {");
            sb.AppendLine("            if (client == null)");
            sb.AppendLine("                throw new ArgumentNullException(nameof(client));");
            sb.AppendLine();

            for (int i = 0; i < serviceTypes.Length; i++)
            {
                var interfaceSymbol = serviceTypes[i];
                if (interfaceSymbol == null) continue;

                var fullTypeName = GetFullTypeName(interfaceSymbol);
                var serviceNameHint = interfaceSymbol.Name.TrimStart('I');

                if (i == 0)
                {
                    sb.AppendLine($"            if (typeof(T) == typeof({fullTypeName}))");
                }
                else
                {
                    sb.AppendLine($"            else if (typeof(T) == typeof({fullTypeName}))");
                }
                sb.AppendLine("            {");
                sb.AppendLine($"                var effectiveServiceName = string.IsNullOrEmpty(serviceName) ? \"{serviceNameHint}\" : serviceName;");
                sb.AppendLine($"                return client.GetMultiInstanceServiceAsync<T>(effectiveServiceName, options);");
                sb.AppendLine("            }");
            }

            sb.AppendLine();
            sb.AppendLine("            throw new ArgumentException($\"未找到多实例服务管理器: {{typeof(T).Name}}\", nameof(T));");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        // 生成新的 RegisterEventListener<T>() 方法 - 统一命名
        if (eventTypes.Length > 0)
        {
            // 为 IPulseRPCClient 生成 RegisterEventListener<T>() 方法
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// 注册事件监听器 - 使用默认配置");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        /// <typeparam name=\"T\">事件接口类型</typeparam>");
            sb.AppendLine("        /// <param name=\"client\">PulseRPC 客户端</param>");
            sb.AppendLine("        /// <param name=\"listener\">事件监听器实例</param>");
            sb.AppendLine("        /// <returns>订阅令牌</returns>");
            sb.AppendLine("        public static ISubscriptionToken RegisterEventListenerInternal<T>(this IPulseRPCClient client, T listener) where T : class");
            sb.AppendLine("        {");
            sb.AppendLine("            if (client == null)");
            sb.AppendLine("                throw new ArgumentNullException(nameof(client));");
            sb.AppendLine("            if (listener == null)");
            sb.AppendLine("                throw new ArgumentNullException(nameof(listener));");
            sb.AppendLine();

            // 为每个事件类型生成 if-else 分支
            for (int i = 0; i < eventTypes.Length; i++)
            {
                var interfaceSymbol = eventTypes[i];
                if (interfaceSymbol == null) continue;

                var fullTypeName = GetFullTypeName(interfaceSymbol);

                if (i == 0)
                {
                    sb.AppendLine($"            if (typeof(T) == typeof({fullTypeName}))");
                }
                else
                {
                    sb.AppendLine($"            else if (typeof(T) == typeof({fullTypeName}))");
                }
                sb.AppendLine("            {");
                sb.AppendLine($"                var channelManager = client.GetChannelManager();");
                sb.AppendLine($"                return RegisterEventListenerFor{GetSafeTypeName(interfaceSymbol)}(channelManager, listener as {fullTypeName}, (PulseRPC.EventListenerConfiguration?)null);");
                sb.AppendLine("            }");
            }

            sb.AppendLine();
            sb.AppendLine("            throw new ArgumentException($\"未找到事件监听器注册方法: {{typeof(T).Name}}\", nameof(T));");
            sb.AppendLine("        }");
            sb.AppendLine();

            // 生成配置版本的方法
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// 注册事件监听器 - 使用指定配置");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        /// <typeparam name=\"T\">事件接口类型</typeparam>");
            sb.AppendLine("        /// <param name=\"client\">PulseRPC 客户端</param>");
            sb.AppendLine("        /// <param name=\"listener\">事件监听器实例</param>");
            sb.AppendLine("        /// <param name=\"configuration\">配置对象</param>");
            sb.AppendLine("        /// <returns>订阅令牌</returns>");
            sb.AppendLine("        public static ISubscriptionToken RegisterEventListenerWithConfiguration<T>(this IPulseRPCClient client, T listener, PulseRPC.EventListenerConfiguration configuration) where T : class");
            sb.AppendLine("        {");
            sb.AppendLine("            if (client == null)");
            sb.AppendLine("                throw new ArgumentNullException(nameof(client));");
            sb.AppendLine("            if (listener == null)");
            sb.AppendLine("                throw new ArgumentNullException(nameof(listener));");
            sb.AppendLine("            if (configuration == null)");
            sb.AppendLine("                throw new ArgumentNullException(nameof(configuration));");
            sb.AppendLine();

            // 为每个事件类型生成配置版本的 if-else 分支
            for (int i = 0; i < eventTypes.Length; i++)
            {
                var interfaceSymbol = eventTypes[i];
                if (interfaceSymbol == null) continue;

                var fullTypeName = GetFullTypeName(interfaceSymbol);

                if (i == 0)
                {
                    sb.AppendLine($"            if (typeof(T) == typeof({fullTypeName}))");
                }
                else
                {
                    sb.AppendLine($"            else if (typeof(T) == typeof({fullTypeName}))");
                }
                sb.AppendLine("            {");
                sb.AppendLine($"                var channelManager = client.GetChannelManager();");
                sb.AppendLine($"                return RegisterEventListenerFor{GetSafeTypeName(interfaceSymbol)}(channelManager, listener as {fullTypeName}, configuration);");
                sb.AppendLine("            }");
            }

            sb.AppendLine();
            sb.AppendLine("            throw new ArgumentException($\"未找到事件监听器注册方法: {{typeof(T).Name}}\", nameof(T));");
            sb.AppendLine("        }");
            sb.AppendLine();

            // 为 IChannelManager 生成扩展方法
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// 为 IChannelManager 注册事件监听器 - 源代码生成器实现，零反射");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        /// <typeparam name=\"T\">事件监听器类型</typeparam>");
            sb.AppendLine("        /// <param name=\"channelManager\">通道管理器</param>");
            sb.AppendLine("        /// <param name=\"listener\">事件监听器实例</param>");
            sb.AppendLine("        /// <returns>订阅令牌</returns>");
            sb.AppendLine("        public static ISubscriptionToken RegisterEventListener<T>(this IChannelManager channelManager, T listener) where T : class, IPulseEventHandler");
            sb.AppendLine("        {");
            sb.AppendLine("            if (channelManager == null)");
            sb.AppendLine("                throw new ArgumentNullException(nameof(channelManager));");
            sb.AppendLine("            if (listener == null)");
            sb.AppendLine("                throw new ArgumentNullException(nameof(listener));");
            sb.AppendLine();

            // 为每个事件类型生成 if-else 分支 - IChannelManager 版本
            for (int i = 0; i < eventTypes.Length; i++)
            {
                var interfaceSymbol = eventTypes[i];
                if (interfaceSymbol == null) continue;

                var fullTypeName = GetFullTypeName(interfaceSymbol);

                if (i == 0)
                {
                    sb.AppendLine($"            if (typeof(T) == typeof({fullTypeName}))");
                }
                else
                {
                    sb.AppendLine($"            else if (typeof(T) == typeof({fullTypeName}))");
                }
                sb.AppendLine("            {");
                sb.AppendLine($"                return RegisterEventListenerFor{GetSafeTypeName(interfaceSymbol)}(channelManager, listener as {fullTypeName}, (PulseRPC.EventListenerConfiguration?)null);");
                sb.AppendLine("            }");
            }

            sb.AppendLine();
            sb.AppendLine("            throw new ArgumentException($\"未找到事件监听器注册方法: {{typeof(T).Name}}\", nameof(T));");
            sb.AppendLine("        }");
            sb.AppendLine();

            // 为每个事件接口生成专用的注册方法
            foreach (var interfaceSymbol in eventTypes)
            {
                if (interfaceSymbol == null) continue;

                var fullTypeName = GetFullTypeName(interfaceSymbol);
                var safeTypeName = GetSafeTypeName(interfaceSymbol);
                var channelName = GetChannelAttributeValue(interfaceSymbol) ?? "default";

                sb.AppendLine($"        /// <summary>");
                sb.AppendLine($"        /// 注册 {interfaceSymbol.Name} 事件监听器");
                sb.AppendLine($"        /// </summary>");
                sb.AppendLine($"        private static ISubscriptionToken RegisterEventListenerFor{safeTypeName}(IChannelManager channelManager, {fullTypeName}? listener, PulseRPC.EventListenerConfiguration? configuration)");
                sb.AppendLine("        {");
                sb.AppendLine("            if (listener == null)");
                sb.AppendLine("                throw new ArgumentNullException(nameof(listener));");
                sb.AppendLine();
                sb.AppendLine("            // 使用配置中的通道，或者默认通道");
                sb.AppendLine($"            var targetChannelName = configuration?.ChannelName ?? \"{channelName}\";");
                sb.AppendLine("            var channel = channelManager.GetChannel(targetChannelName);");
                sb.AppendLine("            var tokens = new System.Collections.Generic.List<ISubscriptionToken>();");
                sb.AppendLine();
                sb.AppendLine("            // 创建错误处理管理器和性能包装器");
                sb.AppendLine("            var errorManager = new PulseRPC.Client.EventErrorManager();");
                sb.AppendLine("            var performanceWrapper = new PulseRPC.Client.EventPerformanceWrapper();");
                sb.AppendLine("            var timeoutWrapper = new PulseRPC.Client.EventTimeoutWrapper();");
                sb.AppendLine();

                // 为每个事件方法生成订阅代码
                var processedMethods = new HashSet<string>();
                foreach (var member in interfaceSymbol.GetMembers())
                {
                    if (member is not IMethodSymbol methodSymbol)
                        continue;

                    if (methodSymbol.DeclaredAccessibility != Accessibility.Public)
                        continue;

                    if (methodSymbol.Parameters.Length != 1)
                        continue;

                    var eventMethod = methodSymbol.Name;
                    var eventType = methodSymbol.Parameters[0].Type.ToDisplayString();

                    // 防止重复处理同名方法
                    var methodKey = $"{eventMethod}_{eventType}";
                    if (processedMethods.Contains(methodKey))
                        continue;

                    processedMethods.Add(methodKey);

                    sb.AppendLine($"            // 注册 {eventMethod} 事件");
                    sb.AppendLine($"            {{");
                    sb.AppendLine($"                var eventName = \"{eventMethod}\";");
                    sb.AppendLine($"                var token = channel.SubscribeToEvent<{eventType}>(eventName, (System.EventHandler<{eventType}>)(async (sender, eventData) =>");
                    sb.AppendLine($"                {{");
                    sb.AppendLine($"                    try");
                    sb.AppendLine($"                    {{");
                    sb.AppendLine($"                        // 检查事件过滤器");
                    sb.AppendLine($"                        if (configuration?.EventFilter != null && !configuration.EventFilter(eventName))");
                    sb.AppendLine($"                            return;");
                    sb.AppendLine();
                    sb.AppendLine($"                        // 检查数据过滤器");
                    sb.AppendLine($"                        if (configuration?.DataFilters != null && configuration.DataFilters.TryGetValue(eventName, out var dataFilter))");
                    sb.AppendLine($"                        {{");
                    sb.AppendLine($"                            if (!dataFilter(eventData))");
                    sb.AppendLine($"                                return;");
                    sb.AppendLine($"                        }}");
                    sb.AppendLine();
                    sb.AppendLine($"                        // 创建事件处理器包装");
                    sb.AppendLine($"                        System.Func<object?, System.Threading.Tasks.Task> eventHandler = (System.Func<object?, System.Threading.Tasks.Task>)(async (data) =>");
                    sb.AppendLine($"                        {{");
                    sb.AppendLine($"                            await timeoutWrapper.WrapWithTimeout(eventName,");
                    sb.AppendLine($"                                async (ct) => await System.Threading.Tasks.Task.Run(() => listener.{eventMethod}(({eventType})data!), ct),");
                    sb.AppendLine($"                                configuration?.Timeout);");
                    sb.AppendLine($"                        }});");
                    sb.AppendLine();
                    sb.AppendLine($"                        // 应用性能监控包装");
                    sb.AppendLine($"                        await performanceWrapper.WrapWithPerformanceMonitoring(eventName,");
                    sb.AppendLine($"                            () => eventHandler(eventData),");
                    sb.AppendLine($"                            configuration?.EnablePerformanceMonitoring ?? false);");
                    sb.AppendLine($"                    }}");
                    sb.AppendLine($"                    catch (System.Exception ex)");
                    sb.AppendLine($"                    {{");
                    sb.AppendLine($"                        // 使用错误管理器处理异常");
                    sb.AppendLine($"                        if (configuration != null)");
                    sb.AppendLine($"                        {{");
                    sb.AppendLine($"                            System.Func<object?, System.Threading.Tasks.Task> eventHandler = (System.Func<object?, System.Threading.Tasks.Task>)(async (object? data) =>");
                    sb.AppendLine($"                            {{");
                    sb.AppendLine($"                                await System.Threading.Tasks.Task.Run(() => listener.{eventMethod}(({eventType})data!));");
                    sb.AppendLine($"                            }});");
                    sb.AppendLine($"                            await errorManager.HandleEventErrorAsync(ex, eventName, eventData, configuration, eventHandler);");
                    sb.AppendLine($"                        }}");
                    sb.AppendLine($"                        else");
                    sb.AppendLine($"                        {{");
                    sb.AppendLine($"                            // 默认错误处理：记录日志并继续");
                    sb.AppendLine($"                            System.Console.WriteLine($\"Event {{eventName}} failed: {{ex.Message}}\");");
                    sb.AppendLine($"                        }}");
                    sb.AppendLine($"                    }}");
                    sb.AppendLine($"                }}));");
                    sb.AppendLine($"                tokens.Add(token);");
                    sb.AppendLine($"            }}");
                    sb.AppendLine();
                }

                sb.AppendLine("            // 返回增强的组合订阅令牌");
                sb.AppendLine("            return new EnhancedCompositeSubscriptionToken(tokens, configuration);");
                sb.AppendLine("        }");
                sb.AppendLine();
            }

            // 生成增强的组合订阅令牌类
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// 增强的组合订阅令牌 - 支持配置和统计");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        private class EnhancedCompositeSubscriptionToken : ISubscriptionToken");
            sb.AppendLine("        {");
            sb.AppendLine("            private readonly System.Collections.Generic.List<ISubscriptionToken> _tokens;");
            sb.AppendLine("            private readonly PulseRPC.EventListenerConfiguration? _configuration;");
            sb.AppendLine("            private readonly System.DateTime _createdAt;");
            sb.AppendLine("            private bool _isDisposed;");
            sb.AppendLine("            private int _eventCount;");
            sb.AppendLine("            private int _errorCount;");
            sb.AppendLine();
            sb.AppendLine("            public System.Guid Id { get; } = System.Guid.NewGuid();");
            sb.AppendLine("            public bool IsActive => !_isDisposed;");
            sb.AppendLine("            public bool IsUnsubscribed => _isDisposed;");
            sb.AppendLine("            public int EventCount => _eventCount;");
            sb.AppendLine("            public int ErrorCount => _errorCount;");
            sb.AppendLine("            public System.TimeSpan Age => System.DateTime.UtcNow - _createdAt;");
            sb.AppendLine();
            sb.AppendLine("            public EnhancedCompositeSubscriptionToken(System.Collections.Generic.List<ISubscriptionToken> tokens, PulseRPC.EventListenerConfiguration? configuration = null)");
            sb.AppendLine("            {");
            sb.AppendLine("                _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));");
            sb.AppendLine("                _configuration = configuration;");
            sb.AppendLine("                _createdAt = System.DateTime.UtcNow;");
            sb.AppendLine("            }");
            sb.AppendLine();
            sb.AppendLine("            public void Unsubscribe()");
            sb.AppendLine("            {");
            sb.AppendLine("                if (_isDisposed)");
            sb.AppendLine("                    return;");
            sb.AppendLine();
            sb.AppendLine("                try");
            sb.AppendLine("                {");
            sb.AppendLine("                    foreach (var token in _tokens)");
            sb.AppendLine("                    {");
            sb.AppendLine("                        try");
            sb.AppendLine("                        {");
            sb.AppendLine("                            token.Unsubscribe();");
            sb.AppendLine("                        }");
            sb.AppendLine("                        catch (System.Exception ex)");
            sb.AppendLine("                        {");
            sb.AppendLine("                            // 记录取消订阅错误，但不阻止其他令牌的取消");
            sb.AppendLine("                            System.Console.WriteLine($\"Error unsubscribing token {token.Id}: {ex.Message}\");");
            sb.AppendLine("                        }");
            sb.AppendLine("                    }");
            sb.AppendLine("                }");
            sb.AppendLine("                finally");
            sb.AppendLine("                {");
            sb.AppendLine("                    _isDisposed = true;");
            sb.AppendLine();
            sb.AppendLine("                    // 输出统计信息");
            sb.AppendLine("                    if (_configuration?.EnablePerformanceMonitoring == true)");
            sb.AppendLine("                    {");
            sb.AppendLine("                        System.Console.WriteLine($\"EventListener Statistics - Events: {_eventCount}, Errors: {_errorCount}, Age: {Age.TotalSeconds:F1}s\");");
            sb.AppendLine("                    }");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
            sb.AppendLine();
            sb.AppendLine("            public void Dispose()");
            sb.AppendLine("            {");
            sb.AppendLine("                Unsubscribe();");
            sb.AppendLine("                System.GC.SuppressFinalize(this);");
            sb.AppendLine("            }");
            sb.AppendLine();
            sb.AppendLine("            /// <summary>");
            sb.AppendLine("            /// 内部方法：增加事件计数");
            sb.AppendLine("            /// </summary>");
            sb.AppendLine("            internal void IncrementEventCount()");
            sb.AppendLine("            {");
            sb.AppendLine("                System.Threading.Interlocked.Increment(ref _eventCount);");
            sb.AppendLine("            }");
            sb.AppendLine();
            sb.AppendLine("            /// <summary>");
            sb.AppendLine("            /// 内部方法：增加错误计数");
            sb.AppendLine("            /// </summary>");
            sb.AppendLine("            internal void IncrementErrorCount()");
            sb.AppendLine("            {");
            sb.AppendLine("                System.Threading.Interlocked.Increment(ref _errorCount);");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        // 为每个服务接口生成扩展方法
        // foreach (var interfaceSymbol in serviceTypes)
        // {
        //     if (interfaceSymbol == null) continue;
        //
        //     var interfaceName = interfaceSymbol.Name;
        //     var fullTypeName = GetFullTypeName(interfaceSymbol);
        //
        //     // 去掉I前缀
        //     var serviceName = interfaceName.StartsWith("I") ? interfaceName.Substring(1) : interfaceName;
        //
        //     sb.AppendLine($"        /// <summary>");
        //     sb.AppendLine($"        /// 获取 {interfaceName} 服务");
        //     sb.AppendLine($"        /// </summary>");
        //     sb.AppendLine($"        public static {fullTypeName} Get{serviceName}(this IChannelManager channelManager)");
        //     sb.AppendLine("        {");
        //     sb.AppendLine($"            if (channelManager == null)");
        //     sb.AppendLine($"                throw new ArgumentNullException(nameof(channelManager));");
        //     sb.AppendLine();
        //     sb.AppendLine($"            return new {fullTypeName}Proxy(channelManager);");
        //     sb.AppendLine("        }");
        //     sb.AppendLine();
        // }

        // 为每个事件接口生成扩展方法
        foreach (var interfaceSymbol in eventTypes)
        {
            if (interfaceSymbol == null) continue;

            var interfaceName = interfaceSymbol.Name;
            var fullTypeName = GetFullTypeName(interfaceSymbol);

            // 去掉I前缀获取服务名
            var serviceName = interfaceName.StartsWith("I") ? interfaceName.Substring(1) : interfaceName;

            // 确保生成的接口名称不重复I前缀
            var handlerInterfaceName = "I" + (interfaceName.StartsWith("I")
                ? interfaceName.Substring(1) + "Handler"
                : interfaceName + "Handler");

            var handlerClassName = interfaceName.StartsWith("I")
                ? interfaceName.Substring(1) + "Handler"
                : interfaceName + "Handler";

            // 获取处理器的完整类型名称
            var handlerInterfaceFullName = interfaceSymbol.ContainingNamespace.IsGlobalNamespace
                ? handlerInterfaceName
                : $"{interfaceSymbol.ContainingNamespace.ToDisplayString()}.{handlerInterfaceName}";

            var handlerClassFullName = interfaceSymbol.ContainingNamespace.IsGlobalNamespace
                ? handlerClassName
                : $"{interfaceSymbol.ContainingNamespace.ToDisplayString()}.{handlerClassName}";

            sb.AppendLine($"        /// <summary>");
            sb.AppendLine($"        /// 获取 {interfaceName} 事件处理器");
            sb.AppendLine($"        /// </summary>");
            sb.AppendLine($"        public static {handlerInterfaceFullName} Get{serviceName}Handler(this IChannelManager channelManager)");
            sb.AppendLine("        {");
            sb.AppendLine($"            if (channelManager == null)");
            sb.AppendLine($"                throw new ArgumentNullException(nameof(channelManager));");
            sb.AppendLine();
            sb.AppendLine($"            var channel = channelManager.GetChannel(\"{GetChannelAttributeValue(interfaceSymbol) ?? "default"}\");");
            sb.AppendLine($"            return new {handlerClassFullName}(channel);");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        // 为 IPulseRPCClient 生成 RegisterEventListenerAsync<T>() 方法
        if (eventTypes.Length > 0)
        {
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// 注册事件监听器 - 智能连接管理");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        /// <typeparam name=\"T\">事件接口类型</typeparam>");
            sb.AppendLine("        /// <param name=\"client\">智能 PulseRPC 客户端</param>");
            sb.AppendLine("        /// <param name=\"listener\">事件监听器实例</param>");
            sb.AppendLine("        /// <param name=\"serviceName\">服务名称</param>");
            sb.AppendLine("        /// <param name=\"options\">连接选项</param>");
            sb.AppendLine("        /// <returns>订阅令牌</returns>");
            sb.AppendLine("        public static async Task<ISubscriptionToken> RegisterEventListenerAsync<T>(this IPulseRPCClient client, T listener, string serviceName = \"\", SmartConnectionOptions? options = null) where T : class, IPulseEventHandler");
            sb.AppendLine("        {");
            sb.AppendLine("            if (client == null)");
            sb.AppendLine("                throw new ArgumentNullException(nameof(client));");
            sb.AppendLine("            if (listener == null)");
            sb.AppendLine("                throw new ArgumentNullException(nameof(listener));");
            sb.AppendLine();

            // 为每个事件类型生成 if-else 分支
            for (int i = 0; i < eventTypes.Length; i++)
            {
                var interfaceSymbol = eventTypes[i];
                if (interfaceSymbol == null) continue;

                var fullTypeName = GetFullTypeName(interfaceSymbol);
                var serviceNameHint = interfaceSymbol.Name.TrimStart('I').Replace("Events", "").Replace("Listener", "");

                if (i == 0)
                {
                    sb.AppendLine($"            if (typeof(T) == typeof({fullTypeName}))");
                }
                else
                {
                    sb.AppendLine($"            else if (typeof(T) == typeof({fullTypeName}))");
                }
                sb.AppendLine("            {");
                sb.AppendLine($"                var effectiveServiceName = string.IsNullOrEmpty(serviceName) ? \"{serviceNameHint}\" : serviceName;");
                sb.AppendLine($"                return await client.RegisterEventListenerAsync<T>(listener, effectiveServiceName, options);");
                sb.AppendLine("            }");
            }

            sb.AppendLine();
            sb.AppendLine("            throw new ArgumentException($\"未找到智能事件监听器注册方法: {{typeof(T).Name}}\", nameof(T));");
            sb.AppendLine("        }");
            sb.AppendLine();

            // 生成路由版本的事件监听器方法
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// 注册事件监听器 - 支持路由上下文");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        /// <typeparam name=\"T\">事件接口类型</typeparam>");
            sb.AppendLine("        /// <param name=\"client\">智能 PulseRPC 客户端</param>");
            sb.AppendLine("        /// <param name=\"listener\">事件监听器实例</param>");
            sb.AppendLine("        /// <param name=\"serviceName\">服务名称</param>");
            sb.AppendLine("        /// <param name=\"routingContext\">路由上下文</param>");
            sb.AppendLine("        /// <param name=\"options\">连接选项</param>");
            sb.AppendLine("        /// <returns>订阅令牌</returns>");
            sb.AppendLine("        public static async Task<ISubscriptionToken> RegisterEventListenerAsync<T>(this IPulseRPCClient client, T listener, string serviceName, IRoutingContext routingContext, SmartConnectionOptions? options = null) where T : class, IPulseEventHandler");
            sb.AppendLine("        {");
            sb.AppendLine("            if (client == null)");
            sb.AppendLine("                throw new ArgumentNullException(nameof(client));");
            sb.AppendLine("            if (listener == null)");
            sb.AppendLine("                throw new ArgumentNullException(nameof(listener));");
            sb.AppendLine("            if (routingContext == null)");
            sb.AppendLine("                throw new ArgumentNullException(nameof(routingContext));");
            sb.AppendLine();

            for (int i = 0; i < eventTypes.Length; i++)
            {
                var interfaceSymbol = eventTypes[i];
                if (interfaceSymbol == null) continue;

                var fullTypeName = GetFullTypeName(interfaceSymbol);

                if (i == 0)
                {
                    sb.AppendLine($"            if (typeof(T) == typeof({fullTypeName}))");
                }
                else
                {
                    sb.AppendLine($"            else if (typeof(T) == typeof({fullTypeName}))");
                }
                sb.AppendLine("            {");
                sb.AppendLine($"                return await client.RegisterEventListenerAsync<T>(listener, serviceName, routingContext, options);");
                sb.AppendLine("            }");
            }

            sb.AppendLine();
            sb.AppendLine("            throw new ArgumentException($\"未找到智能路由事件监听器注册方法: {{typeof(T).Name}}\", nameof(T));");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        // 结束类和命名空间
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string? GetChannelAttributeValue(ISymbol symbol)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass?.Name is not ("ChannelAttribute" or "Channel"))
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length > 0 &&
                attribute.ConstructorArguments[0].Value is string channelName)
            {
                return channelName;
            }
        }

        // 返回默认通道名称而不是空字符串
        return null;
    }

    private static string ExtractGenericType(string genericTypeName)
    {
        var startIndex = genericTypeName.IndexOf('<');
        var endIndex = genericTypeName.LastIndexOf('>');

        if (startIndex >= 0 && endIndex > startIndex)
        {
            return genericTypeName.Substring(startIndex + 1, endIndex - startIndex - 1);
        }

        return "object";
    }

    private static string GenerateEventHandlerInterface(INamedTypeSymbol interfaceSymbol)
    {
        var interfaceName = interfaceSymbol.Name;
        var namespaceName = interfaceSymbol.ContainingNamespace.ToDisplayString();

        // 确保生成的接口名称不重复I前缀
        var handlerInterfaceName = "I" + (interfaceName.StartsWith("I")
            ? interfaceName.Substring(1) + "Handler"
            : interfaceName + "Handler");

        var sb = new StringBuilder();

        // 生成文件头
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine("using PulseRPC;");
        sb.AppendLine();

        // 生成命名空间
        sb.AppendLine($"namespace {namespaceName}");
        sb.AppendLine("{");

        // 生成处理器接口
        sb.AppendLine($"    /// <summary>");
        sb.AppendLine($"    /// {interfaceName} 事件处理器接口");
        sb.AppendLine($"    /// </summary>");
        sb.AppendLine($"    public interface {handlerInterfaceName}");
        sb.AppendLine("    {");
        sb.AppendLine($"        /// <summary>");
        sb.AppendLine($"        /// 订阅事件");
        sb.AppendLine($"        /// </summary>");
        sb.AppendLine($"        ISubscriptionToken Subscribe({interfaceName} subscriber);");
        sb.AppendLine();
        sb.AppendLine($"        /// <summary>");
        sb.AppendLine($"        /// 取消订阅事件");
        sb.AppendLine($"        /// </summary>");
        sb.AppendLine($"        void Unsubscribe({interfaceName} subscriber);");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private class ServiceTypeInfo
    {
        public INamedTypeSymbol? Type { get; }

        public ServiceTypeInfo(INamedTypeSymbol? type)
        {
            Type = type;
        }
    }

    /// <summary>
    /// 获取类型的完整名称，正确处理全局命名空间
    /// </summary>
    private static string GetFullTypeName(INamedTypeSymbol typeSymbol)
    {
        if (typeSymbol.ContainingNamespace.IsGlobalNamespace)
        {
            return typeSymbol.Name;
        }
        else
        {
            return $"{typeSymbol.ContainingNamespace.ToDisplayString()}.{typeSymbol.Name}";
        }
    }

    /// <summary>
    /// 获取类型的安全名称，用于生成方法名
    /// </summary>
    private static string GetSafeTypeName(INamedTypeSymbol typeSymbol)
    {
        var name = typeSymbol.Name;
        if (name.StartsWith("I"))
        {
            name = name.Substring(1);
        }
        return name;
    }
}
