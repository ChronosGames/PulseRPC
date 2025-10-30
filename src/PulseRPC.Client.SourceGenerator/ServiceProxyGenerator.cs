using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using PulseRPC.Generator.Generators;

namespace PulseRPC.Generator;

[Generator(LanguageNames.CSharp)]
public class ServiceProxyGenerator : IIncrementalGenerator
{
    private const string PulseClientGenerationAttributeName = "PulseClientGenerationAttribute";

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
                        // 检查是否实现了IPulseReceiver接口（事件接口）
                        if (serviceType.Type != null && IsEventReceiver(serviceType.Type))
                        {
                            result.Add(serviceType);
                        }
                    }
                }

                return result.ToImmutableArray();
            });

        // 注册服务代理源代码输出
        context.RegisterSourceOutput(allServiceTypes.Combine(allEventTypes).Combine(configProvider), (spc, combined) =>
        {
            var serviceTypes = combined.Left.Left;
            var eventTypes = combined.Left.Right;

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
            var serviceNamedTypes = serviceTypes.Select(t => t.Type).OfType<INamedTypeSymbol>().ToImmutableArray();
            var eventNamedTypes = eventTypes.Select(t => t.Type).OfType<INamedTypeSymbol>().ToImmutableArray();

            if (serviceNamedTypes.Length > 0)
            {
                var extensionsCode = GenerateChannelManagerExtensions(serviceNamedTypes, eventNamedTypes);
                var fileName = "PulseRPC.Services.g.cs";
                spc.AddSource(fileName, SourceText.From(extensionsCode, Encoding.UTF8));
            }

            // 生成 PulseClient 工厂扩展方法
            if (serviceNamedTypes.Length > 0 || eventNamedTypes.Length > 0)
            {
                var factoryExtensionsCode = PulseClientExtensionsGenerator.GeneratePulseClientExtensions(serviceNamedTypes, eventNamedTypes);
                var factoryFileName = "PulseClientFactoryExtensions.g.cs";
                spc.AddSource(factoryFileName, SourceText.From(factoryExtensionsCode, Encoding.UTF8));
            }
        });

        // 注册事件处理器源代码输出
        context.RegisterSourceOutput(allEventTypes.Combine(configProvider), (spc, combined) =>
        {
            var eventTypes = combined.Left;

            // 生成支持类型（只需要生成一次）
            if (eventTypes.Length > 0)
            {
                var supportTypesCode = EventHandlerSupportTypes.GenerateSupportTypes();
                spc.AddSource("EventHandlerSupportTypes.g.cs", SourceText.From(supportTypesCode, Encoding.UTF8));

                var asyncHelpersCode = AsyncMethodHelpers.GenerateAsyncHelpers();
                spc.AddSource("AsyncMethodHelpers.g.cs", SourceText.From(asyncHelpersCode, Encoding.UTF8));
            }

            // 生成事件处理器
            foreach (var eventTypeInfo in eventTypes)
            {
                if (eventTypeInfo.Type is INamedTypeSymbol namedType)
                {
                    // 生成传统事件处理器（保持向后兼容）
                    var handlerCode = GenerateEventHandler(namedType);
                    var handlerFileName = $"{namedType.Name}Handler.g.cs";
                    spc.AddSource(handlerFileName, SourceText.From(handlerCode, Encoding.UTF8));

                    // 智能事件处理器
                    var smartHandlerCode = SmartEventHandlerGenerator.GenerateSmartEventHandler(namedType);
                    var smartHandlerBaseName = namedType.Name.StartsWith("I") ? namedType.Name.Substring(1) : namedType.Name;
                    var smartHandlerFileName = $"Smart{smartHandlerBaseName}Handler.g.cs";
                    spc.AddSource(smartHandlerFileName, SourceText.From(smartHandlerCode, Encoding.UTF8));

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
                // var partialImplementationCode = GenerateEventListenerExtensionsPartial(namedTypes);
                // var fileName = "PulseRPC.Events.g.cs";
                // spc.AddSource(fileName, SourceText.From(partialImplementationCode, Encoding.UTF8));

                // 生成增强的事件监听器扩展 - 重新设计以匹配新的智能处理器架构
                var enhancedExtensionsCode = EnhancedEventListenerExtensions.GenerateEnhancedExtensions(namedTypes);
                var enhancedFileName = "EnhancedEventListenerExtensions.g.cs";
                spc.AddSource(enhancedFileName, SourceText.From(enhancedExtensionsCode, Encoding.UTF8));
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
        // 检查是否实现了 IPulseReceiver 接口
        return typeSymbol.AllInterfaces.Any(i => i.Name == "IPulseReceiver");
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
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using PulseRPC;");
        sb.AppendLine("using PulseRPC.Client;");
        sb.AppendLine("using PulseRPC.Transport;");

        // 添加当前类型的命名空间
        if (!interfaceSymbol.ContainingNamespace.IsGlobalNamespace)
        {
            sb.AppendLine($"using {interfaceSymbol.ContainingNamespace.ToDisplayString()};");
        }

        sb.AppendLine();

        // 生成命名空间
        sb.AppendLine($"namespace {namespaceName}");
        sb.AppendLine("{");

        // 生成基于 IClientChannel 的代理类
        sb.AppendLine($"    /// <summary>");
        sb.AppendLine($"    /// 基于连接的 {interfaceName} 服务代理");
        sb.AppendLine($"    /// </summary>");
        sb.AppendLine($"    public sealed class {interfaceName}Proxy : {interfaceName}");
        sb.AppendLine("    {");
        sb.AppendLine("        private readonly IClientChannel _connection;");
        sb.AppendLine();
        sb.AppendLine($"        /// <summary>");
        sb.AppendLine($"        /// 初始化 {interfaceName} 连接代理");
        sb.AppendLine($"        /// </summary>");
        sb.AppendLine($"        /// <param name=\"connection\">连接</param>");
        sb.AppendLine($"        public {interfaceName}Proxy(IClientChannel connection)");
        sb.AppendLine("        {");
        sb.AppendLine($"            _connection = connection ?? throw new ArgumentNullException(nameof(connection));");
        sb.AppendLine("        }");
        sb.AppendLine();

        // 生成基于连接上下文的方法实现
        foreach (var member in interfaceSymbol.GetMembers())
        {
            if (member is not IMethodSymbol methodSymbol2)
                continue;

            // 自动处理所有公共方法
            if (methodSymbol2.DeclaredAccessibility != Accessibility.Public)
                continue;

            // 生成基于连接上下文的方法实现
            GenerateConnectionContextMethodImplementation(sb, methodSymbol2, defaultChannelName, namespaceName, interfaceName);
        }

        // 结束连接代理类和命名空间
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

        // 简化的方法调用实现，委托给客户端的实际实现
        sb.AppendLine($"            // 通过服务发现或直接连接获取服务");
        sb.AppendLine($"            var service = await _connection.GetServiceAsync<{interfaceName}>(null, {tokenName});");
        if (isVoid || isValueTaskVoid)
        {
            var parameterList = string.Join(", ", parameters.Where(p =>
                p.Type.ToDisplayString() != "System.Threading.CancellationToken" &&
                p.Type.ToDisplayString() != "CancellationToken").Select(p => p.Name));
            sb.AppendLine($"            await service.{methodName}({parameterList});");
        }
        else
        {
            var parameterList = string.Join(", ", parameters.Where(p =>
                p.Type.ToDisplayString() != "System.Threading.CancellationToken" &&
                p.Type.ToDisplayString() != "CancellationToken").Select(p => p.Name));
            sb.AppendLine($"            return await service.{methodName}({parameterList});");
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
        sb.AppendLine("using PulseRPC.Client;");
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
        sb.AppendLine("        private readonly IPulseClient _client;");
        sb.AppendLine($"        private readonly Dictionary<{interfaceName}, List<ISubscriptionToken>> _subscriptions = new Dictionary<{interfaceName}, List<ISubscriptionToken>>();");
        sb.AppendLine();
        sb.AppendLine($"        /// <summary>");
        sb.AppendLine($"        /// 初始化 {interfaceName} 事件处理器");
        sb.AppendLine($"        /// </summary>");
        sb.AppendLine($"        public {handlerClassName}(IPulseClient client)");
        sb.AppendLine("        {");
        sb.AppendLine("            _client = client ?? throw new ArgumentNullException(nameof(client));");
        sb.AppendLine("        }");
        sb.AppendLine();

        // 生成Subscribe方法
        sb.AppendLine($"        /// <inheritdoc/>");
        sb.AppendLine($"        public async Task<ISubscriptionToken> Subscribe({interfaceName} subscriber)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (subscriber == null)");
        sb.AppendLine("                throw new ArgumentNullException(nameof(subscriber));");
        sb.AppendLine();
        sb.AppendLine("            var tokens = new List<ISubscriptionToken>();");
        sb.AppendLine();

        // 简化事件订阅 - 只需要注册一次整个监听器
        sb.AppendLine("            // 注册整个事件监听器");
        sb.AppendLine("            var token = await _client.RegisterEventListenerAsync(subscriber);");
        sb.AppendLine("            tokens.Add(token);");

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
        sb.AppendLine("using PulseRPC.Client;");
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
        sb.AppendLine("        private static async Task<ISubscriptionToken> RegisterEventListenerGenerated<T>(IPulseClient client, T listener) where T : class");
        sb.AppendLine("        {");
        sb.AppendLine("            var tokens = new List<ISubscriptionToken>();");
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
            sb.AppendLine($"                var token = await client.RegisterEventListenerAsync({interfaceSymbol.Name.ToLower()}Listener);");
            sb.AppendLine($"                tokens.Add(token);");
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
        sb.AppendLine("        private static async Task<ISubscriptionToken> RegisterEventListenerWithConfigurationGenerated<T>(IPulseClient client, T listener, EventListenerConfiguration configuration) where T : class");
        sb.AppendLine("        {");
        sb.AppendLine("            var tokens = new List<ISubscriptionToken>();");
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
            sb.AppendLine($"                // 简化实现：直接使用客户端注册事件监听器");
            sb.AppendLine($"                var token = await client.RegisterEventListenerAsync({interfaceSymbol.Name.ToLower()}Listener);");
            sb.AppendLine($"                tokens.Add(token);");
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
        sb.AppendLine("        /// <typeparam name=\"T\">事件监听器类型，必须实现IPulseReceiver</typeparam>");
        sb.AppendLine("        /// <param name=\"client\">PulseRPC客户端</param>");
        sb.AppendLine("        /// <param name=\"listener\">事件监听器实例</param>");
        sb.AppendLine("        /// <returns>订阅令牌</returns>");
        sb.AppendLine("        public static Task<ISubscriptionToken> RegisterEventListenerAsync<T>(this IPulseClient client, T listener) where T : class, IPulseReceiver");
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
        sb.AppendLine("        /// <typeparam name=\"T\">事件监听器类型，必须实现IPulseReceiver</typeparam>");
        sb.AppendLine("        /// <param name=\"client\">PulseRPC客户端</param>");
        sb.AppendLine("        /// <param name=\"listener\">事件监听器实例</param>");
        sb.AppendLine("        /// <returns>事件监听器构建器</returns>");
        sb.AppendLine("        public static PulseReceiverBuilder<T> ConfigureEventListener<T>(this IPulseClient client, T listener) where T : class, IPulseReceiver");
        sb.AppendLine("        {");
        sb.AppendLine("            if (client == null) throw new ArgumentNullException(nameof(client));");
        sb.AppendLine("            if (listener == null) throw new ArgumentNullException(nameof(listener));");
        sb.AppendLine("            return new PulseReceiverBuilder<T>(client, listener);");
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
        sb.AppendLine("        public static Task<ISubscriptionToken> RegisterEventListenerAsync<T>(this IPulseClient client, T listener, EventListenerConfiguration configuration) where T : class, IPulseReceiver");
        sb.AppendLine("        {");
        sb.AppendLine("            if (client == null) throw new ArgumentNullException(nameof(client));");
        sb.AppendLine("            if (listener == null) throw new ArgumentNullException(nameof(listener));");
        sb.AppendLine("            if (configuration == null) throw new ArgumentNullException(nameof(configuration));");
        sb.AppendLine("            return RegisterEventListenerWithConfigurationGenerated(client, listener, configuration);");
        sb.AppendLine("        }");
        sb.AppendLine();

        // 生成内部方法，由 PulseReceiverBuilder 调用
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// 内部方法：使用配置注册事件监听器（由PulseReceiverBuilder调用）");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        internal static Task<ISubscriptionToken> RegisterEventListenerWithConfiguration<T>(IPulseClient client, T listener, EventListenerConfiguration configuration) where T : class");
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
        sb.AppendLine("        public static Task<ISubscriptionToken> RegisterEventListenerOnChannel<T>(this IPulseClient client, T listener, string channelName) where T : class, IPulseReceiver");
        sb.AppendLine("        {");
        sb.AppendLine("            return client.ConfigureEventListener(listener).WithChannel(channelName).RegisterAsync();");
        sb.AppendLine("        }");
        sb.AppendLine();

        // 快捷方法：错误处理
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// 快捷方法：注册事件监听器并设置错误处理");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        public static Task<ISubscriptionToken> RegisterEventListenerWithErrorHandler<T>(this IPulseClient client, T listener, Action<Exception, string> errorHandler) where T : class, IPulseReceiver");
        sb.AppendLine("        {");
        sb.AppendLine("            return client.ConfigureEventListener(listener).WithErrorHandler(errorHandler).RegisterAsync();");
        sb.AppendLine("        }");
        sb.AppendLine();

        // 快捷方法：重试
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// 快捷方法：注册具有重试能力的事件监听器");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        public static Task<ISubscriptionToken> RegisterEventListenerWithRetry<T>(this IPulseClient client, T listener, int maxRetries = 3) where T : class, IPulseReceiver");
        sb.AppendLine("        {");
        sb.AppendLine("            return client.ConfigureEventListener(listener).WithRetry(maxRetries).RegisterAsync();");
        sb.AppendLine("        }");
        sb.AppendLine();
    }

    private static void GeneratePresetMethods(StringBuilder sb)
    {
        // 游戏场景预设
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// 游戏场景预设：低延迟，快速重试，容错处理");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        public static Task<ISubscriptionToken> RegisterGameEventListener<T>(this IPulseClient client, T listener) where T : class, IPulseReceiver");
        sb.AppendLine("        {");
        sb.AppendLine("            return client.ConfigureEventListener(listener).WithGameSettings().RegisterAsync();");
        sb.AppendLine("        }");
        sb.AppendLine();

        // 关键业务场景预设
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// 关键业务场景预设：高可靠性，持久重试，严格错误处理");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        public static Task<ISubscriptionToken> RegisterCriticalEventListener<T>(this IPulseClient client, T listener) where T : class, IPulseReceiver");
        sb.AppendLine("        {");
        sb.AppendLine("            return client.ConfigureEventListener(listener).WithCriticalSettings().RegisterAsync();");
        sb.AppendLine("        }");
        sb.AppendLine();

        // 调试场景预设
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// 开发调试场景预设：详细日志，性能监控，错误详情");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        public static Task<ISubscriptionToken> RegisterDebugEventListenerAsync<T>(this IPulseClient client, T listener) where T : class, IPulseReceiver");
        sb.AppendLine("        {");
        sb.AppendLine("            return client.ConfigureEventListener(listener)");
        sb.AppendLine("                .WithPerformanceMonitoring()");
        sb.AppendLine("                .WithErrorHandler((ex, eventName) => Console.WriteLine($\"[DEBUG] Event {eventName} failed: {ex}\"))");
        sb.AppendLine("                .RegisterAsync();");
        sb.AppendLine("        }");
        sb.AppendLine();
    }

    private static string GenerateChannelManagerExtensions(
        ImmutableArray<INamedTypeSymbol> serviceTypes,
        ImmutableArray<INamedTypeSymbol> eventTypes)
    {
        var sb = new StringBuilder();

        // 生成文件头和动态 using 语句
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using PulseRPC;");
        sb.AppendLine("using PulseRPC.Client;");
        sb.AppendLine("using PulseRPC.Transport;");

        // 动态添加需要的命名空间
        var namespacesToAdd = new HashSet<string>();
        foreach (var serviceType in serviceTypes)
        {
            if (serviceType != null && !serviceType.ContainingNamespace.IsGlobalNamespace)
            {
                namespacesToAdd.Add(serviceType.ContainingNamespace.ToDisplayString());
            }
        }
        foreach (var eventType in eventTypes)
        {
            if (eventType != null && !eventType.ContainingNamespace.IsGlobalNamespace)
            {
                namespacesToAdd.Add(eventType.ContainingNamespace.ToDisplayString());
            }
        }

        foreach (var ns in namespacesToAdd.OrderBy(x => x))
        {
            sb.AppendLine($"using {ns};");
        }

        sb.AppendLine();

        // 生成命名空间
        sb.AppendLine("namespace PulseRPC.Client");
        sb.AppendLine("{");

        // 生成扩展类
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// 服务扩展方法");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public static class ServiceExtensions");
        sb.AppendLine("    {");

        // 生成简化的服务代理工厂方法
        if (serviceTypes.Length > 0)
        {
            // 为每个服务类型生成工厂方法
            foreach (var interfaceSymbol in serviceTypes)
            {
                if (interfaceSymbol == null) continue;

                var interfaceName = interfaceSymbol.Name;
                var fullTypeName = GetFullTypeName(interfaceSymbol);
                var serviceName = interfaceName.StartsWith("I") ? interfaceName.Substring(1) : interfaceName;

                sb.AppendLine($"        /// <summary>");
                sb.AppendLine($"        /// 获取 {interfaceName} 服务代理（通过服务名）");
                sb.AppendLine($"        /// </summary>");
                sb.AppendLine($"        public static async Task<{interfaceName}> Get{serviceName}Async(this IPulseClient client, string serviceName, ServiceProxyOptions? options = null, CancellationToken cancellationToken = default)");
                sb.AppendLine("        {");
                sb.AppendLine("            var connection = await client.ConnectToServiceAsync(serviceName);");
                sb.AppendLine("            return await client.GetServiceAsync<" + fullTypeName + ">(connection.Id, options, cancellationToken);");
                sb.AppendLine("        }");
                sb.AppendLine();

                sb.AppendLine($"        /// <summary>");
                sb.AppendLine($"        /// 获取指定连接的 {interfaceName} 服务代理");
                sb.AppendLine($"        /// </summary>");
                sb.AppendLine($"        public static Task<{fullTypeName}Proxy> Get{serviceName}ProxyAsync(this IPulseClient self, string connectionId, ServiceProxyOptions? options = null, CancellationToken cancellationToken = default)");
                sb.AppendLine("        {");
                sb.AppendLine("            var connection = self.Registry.GetConnection(connectionId);");
                sb.AppendLine("            if (connection == null)");
                sb.AppendLine("            {");
                sb.AppendLine("                throw new ArgumentException($\"连接不存在: {connectionId}\", nameof(connectionId));");
                sb.AppendLine("            }");
                sb.AppendLine();
                sb.AppendLine($"            return Task.FromResult(new {fullTypeName}Proxy(connection));");
                sb.AppendLine("        }");
                sb.AppendLine();
            }
        }

        // 生成事件处理器工厂方法
        if (eventTypes.Length > 0)
        {
            foreach (var interfaceSymbol in eventTypes)
            {
                if (interfaceSymbol == null) continue;

                var interfaceName = interfaceSymbol.Name;
                var fullTypeName = GetFullTypeName(interfaceSymbol);
                var serviceName = interfaceName.StartsWith("I") ? interfaceName.Substring(1) : interfaceName;
                var handlerClassName = interfaceName.StartsWith("I")
                    ? interfaceName.Substring(1) + "Handler"
                    : interfaceName + "Handler";

                sb.AppendLine($"        /// <summary>");
                sb.AppendLine($"        /// 创建 {interfaceName} 事件处理器");
                sb.AppendLine($"        /// </summary>");
                sb.AppendLine($"        public static {fullTypeName.Replace(interfaceName, handlerClassName)} Create{serviceName}Handler(this IPulseClient client)");
                sb.AppendLine("        {");
                sb.AppendLine("            if (client == null)");
                sb.AppendLine("                throw new ArgumentNullException(nameof(client));");
                sb.AppendLine();
                sb.AppendLine($"            return new {fullTypeName.Replace(interfaceName, handlerClassName)}(client);");
                sb.AppendLine("        }");
                sb.AppendLine();

                // 生成 IClientChannel 的事件监听器扩展方法
                sb.AppendLine($"        /// <summary>");
                sb.AppendLine($"        /// 在指定连接上注册 {interfaceName} 事件监听器");
                sb.AppendLine($"        /// </summary>");
                sb.AppendLine($"        public static Task<ISubscriptionToken> RegisterEventListener(this IClientChannel channel, {fullTypeName} listener)");
                sb.AppendLine("        {");
                sb.AppendLine("            if (channel == null)");
                sb.AppendLine("                throw new ArgumentNullException(nameof(channel));");
                sb.AppendLine("            if (listener == null)");
                sb.AppendLine("                throw new ArgumentNullException(nameof(listener));");
                sb.AppendLine();
                sb.AppendLine($"            // TODO: 完整的智能事件处理器集成");
                sb.AppendLine($"            // 目前返回一个基本的订阅令牌，允许代码编译通过");
                sb.AppendLine($"            return Task.FromResult<ISubscriptionToken>(");
                sb.AppendLine($"                new SubscriptionToken(Guid.NewGuid(), \"{interfaceName}\", typeof({fullTypeName}), () => {{ }}));");
                sb.AppendLine("        }");
                sb.AppendLine();
            }
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
        sb.AppendLine("using PulseRPC.Client;");
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
        sb.AppendLine($"        Task<ISubscriptionToken> Subscribe({interfaceName} subscriber);");
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

    /// <summary>
    /// 生成方法参数名称列表
    /// </summary>
    private static string GetParameterNames(ImmutableArray<IParameterSymbol> parameters, string? excludeTokenParam = null)
    {
        var paramNames = new List<string>();

        foreach (var param in parameters)
        {
            // 跳过CancellationToken参数，因为它将单独处理
            if (excludeTokenParam != null && param.Name == excludeTokenParam.Replace("CancellationToken.None", "").Trim())
                continue;

            if (param.Type.ToDisplayString() == "System.Threading.CancellationToken" ||
                param.Type.ToDisplayString() == "CancellationToken")
                continue;

            paramNames.Add(param.Name);
        }

        // 如果有CancellationToken参数，添加到最后
        if (excludeTokenParam != null && excludeTokenParam != "CancellationToken.None")
        {
            paramNames.Add(excludeTokenParam);
        }

        return string.Join(", ", paramNames);
    }

    private static void GenerateConnectionContextMethodImplementation(
        StringBuilder sb,
        IMethodSymbol methodSymbol,
        string defaultChannelName,
        string namespaceName,
        string interfaceName)
    {
        var methodName = methodSymbol.Name;
        var returnType = methodSymbol.ReturnType.ToDisplayString();

        // 获取方法级别的通道特性，如果没有则使用接口级别的默认通道
        var channelName = GetChannelAttributeValue(methodSymbol) ?? defaultChannelName;

        sb.AppendLine($"        /// <summary>");
        sb.AppendLine($"        /// 调用 {methodName} 方法 (零拷贝优化)");
        sb.AppendLine($"        /// </summary>");

        // 生成方法签名
        var parameters = methodSymbol.Parameters;
        var paramList = string.Join(", ", parameters.Select(p =>
            $"{p.Type.ToDisplayString()} {p.Name}"));

        // 检查是否是异步方法
        var isAsync = returnType.StartsWith("System.Threading.Tasks.Task") || returnType.StartsWith("System.Threading.Tasks.ValueTask");

        if (isAsync)
        {
            sb.AppendLine($"        public async {returnType} {methodName}({paramList})");
        }
        else
        {
            sb.AppendLine($"        public {returnType} {methodName}({paramList})");
        }

        sb.AppendLine("        {");

        if (isAsync)
        {
            // 提取 CancellationToken 参数
            var cancelTokenParam = parameters.FirstOrDefault(p =>
                p.Type.ToDisplayString() == "System.Threading.CancellationToken" ||
                p.Type.ToDisplayString() == "CancellationToken");
            var tokenName = cancelTokenParam?.Name ?? "default";

            // 提取非 CancellationToken 的参数
            var dataParameters = parameters.Where(p =>
                p.Type.ToDisplayString() != "System.Threading.CancellationToken" &&
                p.Type.ToDisplayString() != "CancellationToken").ToList();

            // 根据返回类型生成不同的调用
            if (returnType == "System.Threading.Tasks.Task" || returnType == "System.Threading.Tasks.ValueTask")
            {
                // OneWay/Command 方法（无返回值）- 使用零拷贝路径
                GenerateCommandMethodBody(sb, interfaceName, methodName, dataParameters, tokenName);
            }
            else
            {
                // Request/Response 方法（有返回值）- 使用零拷贝路径
                var taskType = returnType
                    .Replace("System.Threading.Tasks.Task<", string.Empty)
                    .Replace("System.Threading.Tasks.ValueTask<", string.Empty)
                    .TrimEnd('>');

                GenerateRequestMethodBody(sb, interfaceName, methodName, dataParameters, taskType, tokenName);
            }
        }
        else
        {
            // 生成同步实现（通过异步包装）
            sb.AppendLine($"            // 同步方法通过异步实现");
            var asyncCall = $"{methodName}Async({string.Join(", ", parameters.Select(p => p.Name))})";
            if (returnType == "void")
            {
                sb.AppendLine($"            {asyncCall}.GetAwaiter().GetResult();");
            }
            else
            {
                sb.AppendLine($"            return {asyncCall}.GetAwaiter().GetResult();");
            }
        }

        sb.AppendLine("        }");
        sb.AppendLine();
    }

    /// <summary>
    /// 生成 Request/Response 方法体（零拷贝优化）
    /// </summary>
    private static void GenerateRequestMethodBody(
        StringBuilder sb,
        string interfaceName,
        string methodName,
        List<IParameterSymbol> dataParameters,
        string responseType,
        string tokenName)
    {
        sb.AppendLine($"            // ========== 零拷贝优化路径：Request/Response ==========");
        sb.AppendLine($"            // Step 1: 租借序列化缓冲区");
        sb.AppendLine($"            var __buffer__ = _connection.RentSerializationBuffer(256);");
        sb.AppendLine($"            try");
        sb.AppendLine($"            {{");

        // Step 2: 显式 MemoryPack 序列化
        if (dataParameters.Count == 0)
        {
            // 无参数
            sb.AppendLine($"                // 无参数，序列化空对象");
            sb.AppendLine($"                MemoryPack.MemoryPackSerializer.Serialize(__buffer__, PulseRPC.EmptyResponse.Instance);");
        }
        else if (dataParameters.Count == 1)
        {
            // 单参数
            var param = dataParameters[0];
            sb.AppendLine($"                // 序列化单个参数");
            sb.AppendLine($"                MemoryPack.MemoryPackSerializer.Serialize(__buffer__, {param.Name});");
        }
        else
        {
            // 多参数：创建元组
            var tupleType = "(" + string.Join(", ", dataParameters.Select(p => p.Type.ToDisplayString())) + ")";
            var tupleValues = "(" + string.Join(", ", dataParameters.Select(p => p.Name)) + ")";
            sb.AppendLine($"                // 序列化多个参数为元组");
            sb.AppendLine($"                var __request__ = {tupleValues};");
            sb.AppendLine($"                MemoryPack.MemoryPackSerializer.Serialize(__buffer__, __request__);");
        }

        sb.AppendLine($"");
        sb.AppendLine($"                // Step 3: 获取已序列化的字节");
        sb.AppendLine($"                var __serializedRequest__ = __buffer__ is System.Buffers.ArrayBufferWriter<byte> __abw__ ");
        sb.AppendLine($"                    ? __abw__.WrittenMemory ");
        sb.AppendLine($"                    : System.ReadOnlyMemory<byte>.Empty;");
        sb.AppendLine($"");
        sb.AppendLine($"                // Step 4: 零拷贝发送并等待响应");
        sb.AppendLine($"                var __responseBytes__ = await _connection.InvokeRawAsync(");
        sb.AppendLine($"                    serviceName: \"{interfaceName}\",");
        sb.AppendLine($"                    methodName: \"{methodName}\",");
        sb.AppendLine($"                    serializedRequest: __serializedRequest__,");
        sb.AppendLine($"                    cancellationToken: {tokenName}");
        sb.AppendLine($"                );");
        sb.AppendLine($"");

        // Step 5: 反序列化响应
        if (responseType == "PulseRPC.EmptyResponse" || responseType == "EmptyResponse")
        {
            sb.AppendLine($"                // 空响应，直接返回");
            sb.AppendLine($"                return;");
        }
        else
        {
            sb.AppendLine($"                // Step 5: 显式反序列化响应");
            sb.AppendLine($"                return MemoryPack.MemoryPackSerializer.Deserialize<{responseType}>(__responseBytes__.Span)!;");
        }

        sb.AppendLine($"            }}");
        sb.AppendLine($"            finally");
        sb.AppendLine($"            {{");
        sb.AppendLine($"                // Step 6: 归还缓冲区");
        sb.AppendLine($"                _connection.ReturnSerializationBuffer(__buffer__);");
        sb.AppendLine($"            }}");
    }

    /// <summary>
    /// 生成 Command/OneWay 方法体（零拷贝优化）
    /// </summary>
    private static void GenerateCommandMethodBody(
        StringBuilder sb,
        string interfaceName,
        string methodName,
        List<IParameterSymbol> dataParameters,
        string tokenName)
    {
        sb.AppendLine($"            // ========== 零拷贝优化路径：Command/OneWay ==========");
        sb.AppendLine($"            // Step 1: 租借序列化缓冲区");
        sb.AppendLine($"            var __buffer__ = _connection.RentSerializationBuffer(256);");
        sb.AppendLine($"            try");
        sb.AppendLine($"            {{");

        // Step 2: 显式 MemoryPack 序列化
        if (dataParameters.Count == 0)
        {
            sb.AppendLine($"                // 无参数，序列化空对象");
            sb.AppendLine($"                MemoryPack.MemoryPackSerializer.Serialize(__buffer__, PulseRPC.EmptyResponse.Instance);");
        }
        else if (dataParameters.Count == 1)
        {
            var param = dataParameters[0];
            sb.AppendLine($"                // 序列化单个参数");
            sb.AppendLine($"                MemoryPack.MemoryPackSerializer.Serialize(__buffer__, {param.Name});");
        }
        else
        {
            var tupleValues = "(" + string.Join(", ", dataParameters.Select(p => p.Name)) + ")";
            sb.AppendLine($"                // 序列化多个参数为元组");
            sb.AppendLine($"                var __command__ = {tupleValues};");
            sb.AppendLine($"                MemoryPack.MemoryPackSerializer.Serialize(__buffer__, __command__);");
        }

        sb.AppendLine($"");
        sb.AppendLine($"                // Step 3: 获取已序列化的字节");
        sb.AppendLine($"                var __serializedCommand__ = __buffer__ is System.Buffers.ArrayBufferWriter<byte> __abw__");
        sb.AppendLine($"                    ? __abw__.WrittenMemory");
        sb.AppendLine($"                    : System.ReadOnlyMemory<byte>.Empty;");
        sb.AppendLine($"");
        sb.AppendLine($"                // Step 4: 零拷贝发送（无需等待响应）");
        sb.AppendLine($"                await _connection.SendCommandAsync(");
        sb.AppendLine($"                    serviceName: \"{interfaceName}\",");
        sb.AppendLine($"                    methodName: \"{methodName}\",");
        sb.AppendLine($"                    serializedCommand: __serializedCommand__,");
        sb.AppendLine($"                    cancellationToken: {tokenName}");
        sb.AppendLine($"                );");
        sb.AppendLine($"            }}");
        sb.AppendLine($"            finally");
        sb.AppendLine($"            {{");
        sb.AppendLine($"                // Step 5: 归还缓冲区");
        sb.AppendLine($"                _connection.ReturnSerializationBuffer(__buffer__);");
        sb.AppendLine($"            }}");
    }

    private static string GetEventDataType(IMethodSymbol method)
    {
        // 获取方法的第一个参数类型作为事件数据类型
        var parameters = method.Parameters;
        if (parameters.Length > 0)
        {
            return parameters[0].Type.ToDisplayString();
        }
        return "object";
    }
}
