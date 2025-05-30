using System.Collections.Immutable;
using System.IO;
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
        context.RegisterSourceOutput(allServiceTypes.Combine(configProvider), (spc, combined) =>
        {
            var serviceTypes = combined.Left;
            var config = combined.Right;

            // 检查配置选项
            var writeFilesToDisk = config.GlobalOptions.TryGetValue("build_property.PulseRPC_WriteFilesToDisk", out var writeFiles) &&
                                   (writeFiles.Equals("true", StringComparison.OrdinalIgnoreCase));
            var outputFolder = config.GlobalOptions.TryGetValue("build_property.PulseRPC_OutputFolder", out var folder) ? folder : "Generated";

            // 生成服务代理
            foreach (var serviceTypeInfo in serviceTypes)
            {
                if (serviceTypeInfo.Type is INamedTypeSymbol namedType)
                {
                    var proxyCode = GenerateServiceProxy(namedType);
                    var fileName = $"{namedType.Name}Proxy.g.cs";
                    spc.AddSource(fileName, SourceText.From(proxyCode, Encoding.UTF8));

                    // 如果配置为写入磁盘，则尝试写入文件
                    if (writeFilesToDisk)
                    {
                        TryWriteFileToDisk(fileName, proxyCode, outputFolder);
                    }
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
                var fileName = "ServiceChannelManagerExtensions.g.cs";
                spc.AddSource(fileName, SourceText.From(extensionsCode, Encoding.UTF8));

                // 如果配置为写入磁盘，则尝试写入文件
                if (writeFilesToDisk)
                {
                    TryWriteFileToDisk(fileName, extensionsCode, outputFolder);
                }
            }
        });

        // 注册事件处理器源代码输出
        context.RegisterSourceOutput(allEventTypes.Combine(configProvider), (spc, combined) =>
        {
            var eventTypes = combined.Left;
            var config = combined.Right;

            // 检查配置选项
            var writeFilesToDisk = config.GlobalOptions.TryGetValue("build_property.PulseRPC_WriteFilesToDisk", out var writeFiles) &&
                                   (writeFiles.Equals("true", StringComparison.OrdinalIgnoreCase));
            var outputFolder = config.GlobalOptions.TryGetValue("build_property.PulseRPC_OutputFolder", out var folder) ? folder : "Generated";

            // 生成事件处理器
            foreach (var eventTypeInfo in eventTypes)
            {
                if (eventTypeInfo.Type is INamedTypeSymbol namedType)
                {
                    var handlerCode = GenerateEventHandler(namedType);
                    var handlerFileName = $"{namedType.Name}Handler.g.cs";
                    spc.AddSource(handlerFileName, SourceText.From(handlerCode, Encoding.UTF8));

                    if (writeFilesToDisk)
                    {
                        TryWriteFileToDisk(handlerFileName, handlerCode, outputFolder);
                    }

                    // 生成事件处理器接口
                    var handlerInterfaceCode = GenerateEventHandlerInterface(namedType);
                    var interfaceFileName = $"I{namedType.Name}Handler.g.cs";
                    spc.AddSource(interfaceFileName, SourceText.From(handlerInterfaceCode, Encoding.UTF8));

                    if (writeFilesToDisk)
                    {
                        TryWriteFileToDisk(interfaceFileName, handlerInterfaceCode, outputFolder);
                    }
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

            // 生成事件扩展方法
            var namedTypes = eventTypes.Select(t => t.Type).OfType<INamedTypeSymbol>().ToImmutableArray();
            if (namedTypes.Length > 0)
            {
                var eventExtensionsCode = GenerateChannelManagerExtensions(
                    ImmutableArray<INamedTypeSymbol>.Empty,
                    namedTypes);
                var fileName = "EventChannelManagerExtensions.g.cs";
                spc.AddSource(fileName, SourceText.From(eventExtensionsCode, Encoding.UTF8));

                if (writeFilesToDisk)
                {
                    TryWriteFileToDisk(fileName, eventExtensionsCode, outputFolder);
                }
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
            return [];
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
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using PulseRPC;");
        sb.AppendLine("using PulseRPC.Transport;");
        sb.AppendLine("using PulseRPC.Messaging;");
        sb.AppendLine();

        // 生成命名空间
        sb.AppendLine($"namespace {namespaceName}");
        sb.AppendLine("{");

        // 生成代理类
        sb.AppendLine($"    /// <summary>");
        sb.AppendLine($"    /// 自动生成的 {interfaceName} 服务代理");
        sb.AppendLine($"    /// </summary>");
        sb.AppendLine($"    public class {interfaceName}Proxy : {interfaceName}");
        sb.AppendLine("    {");
        sb.AppendLine("        private readonly IChannelManager _channelManager;");
        sb.AppendLine();
        sb.AppendLine($"        /// <summary>");
        sb.AppendLine($"        /// 初始化 {interfaceName} 服务代理");
        sb.AppendLine($"        /// </summary>");
        sb.AppendLine($"        /// <param name=\"channelManager\">通道管理器</param>");
        sb.AppendLine($"        public {interfaceName}Proxy(IChannelManager channelManager)");
        sb.AppendLine("        {");
        sb.AppendLine("            _channelManager = channelManager ?? throw new ArgumentNullException(nameof(channelManager));");
        sb.AppendLine("        }");
        sb.AppendLine();

        // 生成方法实现
        foreach (var member in interfaceSymbol.GetMembers())
        {
            if (member is not IMethodSymbol methodSymbol)
                continue;

            // 自动处理所有公共方法，不再检查 Operation 特性
            if (methodSymbol.DeclaredAccessibility != Accessibility.Public)
                continue;

            // 生成方法实现
            GenerateMethodImplementation(sb, methodSymbol, defaultChannelName, namespaceName, interfaceName);
        }

        // 结束类和命名空间
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static void GenerateMethodImplementation(
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

        // 获取通道
        sb.AppendLine($"            var channel = _channelManager.GetChannel(\"{methodChannelName}\");");

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

        // 生成方法调用
        if (isVoid || isValueTaskVoid)
        {
            sb.AppendLine($"            await channel.SendRequestAsync<{requestType}, {responseType}>(");
            sb.AppendLine($"                \"{namespaceName}.{interfaceName}\", ");
            sb.AppendLine($"                \"{methodName}\", ");
            sb.AppendLine($"                {requestName}, ");
            sb.AppendLine($"                {tokenName});");
        }
        else
        {
            sb.AppendLine($"            var response = await channel.SendRequestAsync<{requestType}, {responseType}>(");
            sb.AppendLine($"                \"{namespaceName}.{interfaceName}\", ");
            sb.AppendLine($"                \"{methodName}\", ");
            sb.AppendLine($"                {requestName}, ");
            sb.AppendLine($"                {tokenName});");
            sb.AppendLine("            return response;");
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
            sb.AppendLine($"                (sender, eventData) => subscriber.{eventMethod}(eventData));");
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
        sb.AppendLine("using PulseRPC.Transport;");
        sb.AppendLine("using PulseRPC;");
        sb.AppendLine();

        // 生成命名空间
        sb.AppendLine("namespace PulseRPC.Client");
        sb.AppendLine("{");

        // 为每种类型生成不同的扩展类，避免冲突
        var extensionClassName = "ChannelManagerExtensions";
        if (serviceTypes.Length > 0 && eventTypes.Length == 0)
        {
            extensionClassName = "ServiceChannelManagerExtensions";
        }
        else if (serviceTypes.Length == 0 && eventTypes.Length > 0)
        {
            extensionClassName = "EventChannelManagerExtensions";
        }

        // 生成扩展类
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// 通道管理器扩展方法");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine($"    public static class {extensionClassName}");
        sb.AppendLine("    {");

        // 为每个服务接口生成扩展方法
        foreach (var interfaceSymbol in serviceTypes)
        {
            if (interfaceSymbol == null) continue;

            var interfaceName = interfaceSymbol.Name;
            var fullTypeName = GetFullTypeName(interfaceSymbol);

            // 去掉I前缀
            var serviceName = interfaceName.StartsWith("I") ? interfaceName.Substring(1) : interfaceName;

            sb.AppendLine($"        /// <summary>");
            sb.AppendLine($"        /// 获取 {interfaceName} 服务");
            sb.AppendLine($"        /// </summary>");
            sb.AppendLine($"        public static {fullTypeName} Get{serviceName}(this IChannelManager channelManager)");
            sb.AppendLine("        {");
            sb.AppendLine($"            if (channelManager == null)");
            sb.AppendLine($"                throw new ArgumentNullException(nameof(channelManager));");
            sb.AppendLine();
            sb.AppendLine($"            return new {fullTypeName}Proxy(channelManager);");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

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
    /// 尝试将生成的代码写入到磁盘文件
    /// </summary>
    private static void TryWriteFileToDisk(string fileName, string content, string outputFolder)
    {
        try
        {
            // 确保输出目录存在
            if (!Directory.Exists(outputFolder))
            {
                Directory.CreateDirectory(outputFolder);
            }

            var fullPath = Path.Combine(outputFolder, fileName);
            File.WriteAllText(fullPath, content, Encoding.UTF8);
        }
        catch
        {
            // 忽略文件写入错误，因为 SourceGenerator 应该能够在没有文件输出的情况下工作
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
}
