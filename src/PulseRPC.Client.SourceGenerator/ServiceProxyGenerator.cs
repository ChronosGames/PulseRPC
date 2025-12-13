using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using PulseRPC.Generator.Generators;

namespace PulseRPC.Generator;

[Generator(LanguageNames.CSharp)]
public class ServiceProxyGenerator : IIncrementalGenerator
{
    private const string PulseClientGenerationAttributeName = "PulseClientGenerationAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
// #if DEBUG
//         if (!System.Diagnostics.Debugger.IsAttached)
//         {
//             System.Diagnostics.Debugger.Launch();
//         }
// #endif

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
            var configOptions = combined.Right;

            // 读取 MSBuild 配置：PulseRPC_ClientChannels
            var channelNames = ReadChannelNamesFromConfig(configOptions);

            // 去重服务类型（同一个接口可能被多个类的 PulseClientGeneration 特性引用）
            var uniqueServiceTypes = serviceTypes
                .Where(st => st.Type is INamedTypeSymbol)
                .GroupBy(st => ((INamedTypeSymbol)st.Type!).ToDisplayString())
                .Select(g => g.First())
                .ToList();

            // 生成服务代理
            foreach (var serviceTypeInfo in uniqueServiceTypes)
            {
                if (serviceTypeInfo.Type is INamedTypeSymbol namedType)
                {
                    var proxyCode = GenerateServiceProxy(namedType, spc);
                    // 使用完整类型名称（包含命名空间）确保文件名唯一
                    var fileName = $"{GetSafeFileName(namedType)}Proxy.g.cs";
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

            // 生成扩展方法（使用去重后的类型）
            var serviceNamedTypes = uniqueServiceTypes.Select(t => t.Type).OfType<INamedTypeSymbol>().ToImmutableArray();

            // 去重事件类型用于扩展方法生成
            var uniqueEventTypesForExtensions = eventTypes
                .Where(et => et.Type is INamedTypeSymbol)
                .GroupBy(et => ((INamedTypeSymbol)et.Type!).ToDisplayString())
                .Select(g => g.First())
                .ToList();
            var eventNamedTypes = uniqueEventTypesForExtensions.Select(t => t.Type).OfType<INamedTypeSymbol>().ToImmutableArray();

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

            // 去重事件类型（同一个接口可能被多个类的 PulseClientGeneration 特性引用）
            var uniqueEventTypes = eventTypes
                .Where(et => et.Type is INamedTypeSymbol)
                .GroupBy(et => ((INamedTypeSymbol)et.Type!).ToDisplayString())
                .Select(g => g.First())
                .ToList();

            // 生成支持类型（只需要生成一次）
            if (uniqueEventTypes.Count > 0)
            {
                var supportTypesCode = EventHandlerSupportTypes.GenerateSupportTypes();
                spc.AddSource("PulseRPC.Client.SupportTypes.g.cs", SourceText.From(supportTypesCode, Encoding.UTF8));
            }

            // 生成智能事件处理器和接收器调度器
            foreach (var eventTypeInfo in uniqueEventTypes)
            {
                if (eventTypeInfo.Type is INamedTypeSymbol namedType)
                {
                    // 生成智能事件处理器（保持向后兼容）
                    var smartHandlerCode = SmartEventHandlerGenerator.GenerateSmartEventHandler(namedType, spc);
                    var smartHandlerBaseName = namedType.Name.StartsWith("I") ? namedType.Name.Substring(1) : namedType.Name;
                    // 使用完整类型名称（包含命名空间）确保文件名唯一
                    var smartHandlerFileName = $"{GetSafeFileName(namedType).Replace(namedType.Name, smartHandlerBaseName)}SmartHandler.g.cs";
                    spc.AddSource(smartHandlerFileName, SourceText.From(smartHandlerCode, Encoding.UTF8));

                    // 生成接收器调度器（新增：简化的 IPulseReceiver 注册）
                    var dispatcherCode = ReceiverDispatcherGenerator.GenerateReceiverDispatcher(namedType, spc);
                    var dispatcherBaseName = namedType.Name.StartsWith("I") ? namedType.Name.Substring(1) : namedType.Name;
                    var dispatcherFileName = $"{GetSafeFileName(namedType).Replace(namedType.Name, dispatcherBaseName)}Dispatcher.g.cs";
                    spc.AddSource(dispatcherFileName, SourceText.From(dispatcherCode, Encoding.UTF8));
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

            // 生成统一的客户端扩展方法（使用去重后的类型）
            // var namedTypes = uniqueEventTypes.Select(t => t.Type).OfType<INamedTypeSymbol>().ToImmutableArray();
            // if (namedTypes.Length > 0)
            // {
            //     var enhancedExtensionsCode = EnhancedEventListenerExtensions.GenerateEnhancedExtensions(namedTypes);
            //     var enhancedFileName = "PulseRPC.Client.Extensions.g.cs";
            //     spc.AddSource(enhancedFileName, SourceText.From(enhancedExtensionsCode, Encoding.UTF8));
            //
            //     // 生成统一接收器注册扩展方法（RegisterAllReceivers<T>）
            //     var unifiedRegistrationCode = UnifiedReceiverRegistrationGenerator.Generate(namedTypes);
            //     if (!string.IsNullOrEmpty(unifiedRegistrationCode))
            //     {
            //         spc.AddSource("PulseRPC.Client.UnifiedReceiverRegistration.g.cs",
            //             SourceText.From(unifiedRegistrationCode, Encoding.UTF8));
            //     }
            // }
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
        // 排除 IPulseReceiver 接口本身
        if (typeSymbol.Name == "IPulseReceiver")
            return false;

        // 方式 1（主要方式）：检查是否实现了 IPulseReceiver 接口
        if (typeSymbol.AllInterfaces.Any(i => i.Name == "IPulseReceiver"))
        {
            return true;
        }

        // 方式 2（向后兼容）：检查是否标记了 [Channel("CLIENT")] 特性
        var channelAttr = typeSymbol.GetAttributes()
            .FirstOrDefault(attr => attr.AttributeClass?.Name is "ChannelAttribute" or "Channel");

        if (channelAttr?.ConstructorArguments.Length > 0)
        {
            var channelName = channelAttr.ConstructorArguments[0].Value?.ToString();
            // 约定：Channel 为 "CLIENT" 的接口视为客户端实现的事件接收器
            if (string.Equals(channelName, "CLIENT", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string GenerateServiceProxy(INamedTypeSymbol interfaceSymbol, SourceProductionContext context)
    {
        var interfaceName = interfaceSymbol.Name;
        var namespaceName = interfaceSymbol.ContainingNamespace.ToDisplayString();

        // 获取通道特性
        var defaultChannelName = GetChannelAttributeValue(interfaceSymbol) ?? "default";

        // 为所有方法生成协议号
        var protocolIds = ProtocolIdGenerator.AssignProtocolIds(interfaceSymbol, context);

        var sb = new StringBuilder();

        // 生成文件头
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine($"// Generated by PulseRPC.Client.SourceGenerator");
        sb.AppendLine($"// Protocol IDs generated using FNV-1a hash for {interfaceSymbol.ToDisplayString()}");
        sb.AppendLine("// </auto-generated>");
        sb.AppendLine();
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using PulseRPC;");
        sb.AppendLine("using PulseRPC.Client;");
        sb.AppendLine("using PulseRPC.Transport;");

        // 收集方法参数和返回值类型的命名空间
        var additionalNamespaces = CollectNamespacesFromMethods(interfaceSymbol);
        foreach (var ns in additionalNamespaces.OrderBy(x => x))
        {
            // 跳过已经添加的系统命名空间和当前接口的命名空间
            if (ns.StartsWith("System") || ns == namespaceName)
                continue;
            sb.AppendLine($"using {ns};");
        }

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
        sb.AppendLine($"    /// 自动生成的 {interfaceName} 客户端代理");
        sb.AppendLine($"    /// 使用协议号进行高性能方法路由");
        sb.AppendLine($"    /// </summary>");
        sb.AppendLine($"    public sealed class {interfaceName}Proxy : {interfaceName}");
        sb.AppendLine("    {");
        sb.AppendLine("        private readonly IClientChannel _connection;");
        sb.AppendLine();

        // 生成协议号常量
        GenerateProtocolIdConstants(sb, interfaceSymbol, protocolIds);

        sb.AppendLine($"        /// <summary>");
        sb.AppendLine($"        /// 初始化 {interfaceName} 连接代理");
        sb.AppendLine($"        /// </summary>");
        sb.AppendLine($"        /// <param name=\"connection\">连接</param>");
        sb.AppendLine($"        public {interfaceName}Proxy(IClientChannel connection)");
        sb.AppendLine("        {");
        sb.AppendLine($"            _connection = connection ?? throw new ArgumentNullException(nameof(connection));");
        sb.AppendLine("        }");
        sb.AppendLine();

        // 生成基于连接上下文的方法实现（包括继承的接口方法）
        foreach (var methodSymbol2 in GetAllInterfaceMethods(interfaceSymbol))
        {
            // 自动处理所有公共方法
            if (methodSymbol2.DeclaredAccessibility != Accessibility.Public)
                continue;

            // 获取方法的协议号（使用方法的声明接口来计算）
            var declaringInterface = methodSymbol2.ContainingType;
            var methodKey = $"{declaringInterface.ToDisplayString()}.{methodSymbol2.Name}";
            ushort protocolId = protocolIds.TryGetValue(methodKey, out var id) ? id : (ushort)0;

            // 生成基于连接上下文的方法实现
            GenerateConnectionContextMethodImplementation(sb, methodSymbol2, protocolId, defaultChannelName, namespaceName, interfaceName);
        }

        // 结束连接代理类和命名空间
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>
    /// 生成协议号常量（包括继承的接口方法）
    /// </summary>
    private static void GenerateProtocolIdConstants(StringBuilder sb, INamedTypeSymbol interfaceSymbol, Dictionary<string, ushort> protocolIds)
    {
        sb.AppendLine("        // ==================== 协议号常量 ====================");
        sb.AppendLine("        // 使用 FNV-1a 哈希算法生成，确保客户端和服务端一致");
        sb.AppendLine();

        foreach (var method in GetAllInterfaceMethods(interfaceSymbol))
        {
            if (method.DeclaredAccessibility != Accessibility.Public)
                continue;

            // 使用方法的声明接口来构建键
            var declaringInterface = method.ContainingType;
            var methodKey = $"{declaringInterface.ToDisplayString()}.{method.Name}";

            if (protocolIds.TryGetValue(methodKey, out var protocolId))
            {
                var methodSignature = ProtocolIdGenerator.BuildMethodSignature(method);
                var constName = ProtocolIdGenerator.GetProtocolIdConstantName(method);

                sb.AppendLine($"        /// <summary>");
                sb.AppendLine($"        /// 协议号: {method.Name}");
                sb.AppendLine($"        /// 方法签名: {methodSignature}");
                sb.AppendLine($"        /// 声明接口: {declaringInterface.ToDisplayString()}");
                sb.AppendLine($"        /// </summary>");
                sb.AppendLine($"        private const ushort {constName} = 0x{protocolId:X4}; // {protocolId}");
                sb.AppendLine();
            }
        }
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
                var smartHandlerClassName = interfaceName.StartsWith("I")
                    ? interfaceName.Substring(1) + "SmartHandler"
                    : interfaceName + "SmartHandler";

                sb.AppendLine($"        /// <summary>");
                sb.AppendLine($"        /// 创建 {interfaceName} 智能事件处理器");
                sb.AppendLine($"        /// </summary>");
                sb.AppendLine($"        public static {fullTypeName.Replace(interfaceName, smartHandlerClassName)} CreateSmart{serviceName}Handler(this IPulseClient client)");
                sb.AppendLine("        {");
                sb.AppendLine("            if (client == null)");
                sb.AppendLine("                throw new ArgumentNullException(nameof(client));");
                sb.AppendLine();
                sb.AppendLine($"            return new {fullTypeName.Replace(interfaceName, smartHandlerClassName)}();");
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


    private class ServiceTypeInfo
    {
        public INamedTypeSymbol? Type { get; }

        public ServiceTypeInfo(INamedTypeSymbol? type)
        {
            Type = type;
        }
    }

    /// <summary>
    /// 获取接口的所有方法（包括继承的接口方法）
    /// </summary>
    /// <param name="interfaceSymbol">接口符号</param>
    /// <returns>所有方法符号（不含重复）</returns>
    private static IEnumerable<IMethodSymbol> GetAllInterfaceMethods(INamedTypeSymbol interfaceSymbol)
    {
        var processedMethods = new HashSet<string>();

        // 首先处理当前接口定义的方法
        foreach (var member in interfaceSymbol.GetMembers())
        {
            if (member is IMethodSymbol method && method.DeclaredAccessibility == Accessibility.Public)
            {
                var methodKey = GetMethodKey(method);
                if (processedMethods.Add(methodKey))
                {
                    yield return method;
                }
            }
        }

        // 然后处理继承的接口方法（排除 IPulseHub 等基础接口）
        foreach (var baseInterface in interfaceSymbol.AllInterfaces)
        {
            // 跳过 PulseRPC 框架的基础接口
            if (baseInterface.Name is "IPulseHub" or "IPulseReceiver")
                continue;

            foreach (var member in baseInterface.GetMembers())
            {
                if (member is IMethodSymbol method && method.DeclaredAccessibility == Accessibility.Public)
                {
                    var methodKey = GetMethodKey(method);
                    if (processedMethods.Add(methodKey))
                    {
                        yield return method;
                    }
                }
            }
        }
    }

    /// <summary>
    /// 获取方法的唯一键（用于去重）
    /// </summary>
    private static string GetMethodKey(IMethodSymbol method)
    {
        var paramTypes = string.Join(",", method.Parameters
            .Where(p => p.Type.ToDisplayString() != "System.Threading.CancellationToken" &&
                       p.Type.ToDisplayString() != "CancellationToken")
            .Select(p => p.Type.ToDisplayString()));
        return $"{method.Name}({paramTypes})";
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
    /// 获取安全的文件名（将命名空间中的点替换为下划线，确保文件名唯一）
    /// </summary>
    private static string GetSafeFileName(INamedTypeSymbol typeSymbol)
    {
        if (typeSymbol.ContainingNamespace.IsGlobalNamespace)
        {
            return typeSymbol.Name;
        }
        else
        {
            // 使用完整的类型名（包含命名空间），将点替换为下划线以生成有效的文件名
            var fullTypeName = $"{typeSymbol.ContainingNamespace.ToDisplayString()}.{typeSymbol.Name}";
            return fullTypeName.Replace('.', '_');
        }
    }

    private static void GenerateConnectionContextMethodImplementation(
        StringBuilder sb,
        IMethodSymbol methodSymbol,
        ushort protocolId,
        string defaultChannelName,
        string namespaceName,
        string interfaceName)
    {
        var methodName = methodSymbol.Name;
        var returnType = methodSymbol.ReturnType.ToDisplayString();

        // 获取方法级别的通道特性，如果没有则使用接口级别的默认通道
        var channelName = GetChannelAttributeValue(methodSymbol) ?? defaultChannelName;

        var constName = ProtocolIdGenerator.GetProtocolIdConstantName(methodSymbol);
        var methodSignature = ProtocolIdGenerator.BuildMethodSignature(methodSymbol);

        sb.AppendLine($"        /// <summary>");
        sb.AppendLine($"        /// 调用 {methodName} 方法");
        sb.AppendLine($"        /// Protocol ID: 0x{protocolId:X4} ({protocolId})");
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
                GenerateCommandMethodBody(sb, methodSymbol, interfaceName, methodName, dataParameters, tokenName, protocolId);
            }
            else
            {
                // Request/Response 方法（有返回值）- 使用零拷贝路径
                var taskType = returnType
                    .Replace("System.Threading.Tasks.Task<", string.Empty)
                    .Replace("System.Threading.Tasks.ValueTask<", string.Empty)
                    .TrimEnd('>');

                GenerateRequestMethodBody(sb, methodSymbol, interfaceName, methodName, dataParameters, taskType, tokenName, protocolId);
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
        IMethodSymbol methodSymbol,
        string interfaceName,
        string methodName,
        List<IParameterSymbol> dataParameters,
        string responseType,
        string tokenName,
        ushort protocolId)
    {
        var constName = ProtocolIdGenerator.GetProtocolIdConstantName(methodSymbol);

        sb.AppendLine($"            // ========== 零拷贝优化路径：Request/Response ==========");
        sb.AppendLine($"            // 使用协议号: {constName} = 0x{protocolId:X4} ({protocolId})");
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
        sb.AppendLine($"                // Step 4: 使用协议号发送并等待响应（零拷贝）");
        sb.AppendLine($"                var __responseBytes__ = await _connection.InvokeRawAsync(");
        sb.AppendLine($"                    protocolId: {constName},");
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
        IMethodSymbol methodSymbol,
        string interfaceName,
        string methodName,
        List<IParameterSymbol> dataParameters,
        string tokenName,
        ushort protocolId)
    {
        var constName = ProtocolIdGenerator.GetProtocolIdConstantName(methodSymbol);

        sb.AppendLine($"            // ========== 零拷贝优化路径：Command/OneWay ==========");
        sb.AppendLine($"            // 使用协议号: {constName} = 0x{protocolId:X4} ({protocolId})");
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
        sb.AppendLine($"                // Step 4: 使用协议号发送（零拷贝，无需等待响应）");
        sb.AppendLine($"                await _connection.SendCommandAsync(");
        sb.AppendLine($"                    protocolId: {constName},");
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

    /// <summary>
    /// 从接口方法中收集所有需要的命名空间（包括参数和返回值类型）
    /// 使用 Roslyn 的语义模型正确处理泛型类型
    /// </summary>
    private static HashSet<string> CollectNamespacesFromMethods(INamedTypeSymbol interfaceSymbol)
    {
        var namespaces = new HashSet<string>();

        foreach (var member in interfaceSymbol.GetMembers())
        {
            if (member is not IMethodSymbol method)
                continue;

            if (method.DeclaredAccessibility != Accessibility.Public)
                continue;

            // 收集返回值类型的命名空间
            if (method.ReturnType != null)
            {
                CollectNamespacesFromType(method.ReturnType, namespaces);
            }

            // 收集参数类型的命名空间
            foreach (var parameter in method.Parameters)
            {
                if (parameter.Type != null)
                {
                    CollectNamespacesFromType(parameter.Type, namespaces);
                }
            }
        }

        return namespaces;
    }

    /// <summary>
    /// 从类型符号中递归收集命名空间（使用 Roslyn 语义模型，正确处理泛型）
    /// </summary>
    private static void CollectNamespacesFromType(ITypeSymbol? typeSymbol, HashSet<string> namespaces)
    {
        // 跳过特殊类型
        if (typeSymbol == null || typeSymbol.SpecialType != SpecialType.None)
            return;

        // 添加类型本身的命名空间
        if (typeSymbol.ContainingNamespace != null && !typeSymbol.ContainingNamespace.IsGlobalNamespace)
        {
            var ns = typeSymbol.ContainingNamespace.ToDisplayString();
            if (!string.IsNullOrWhiteSpace(ns))
            {
                namespaces.Add(ns);
            }
        }

        // 如果是泛型类型，递归处理泛型参数
        if (typeSymbol is INamedTypeSymbol namedType && namedType.IsGenericType)
        {
            foreach (var typeArg in namedType.TypeArguments)
            {
                CollectNamespacesFromType(typeArg, namespaces);
            }
        }

        // 如果是数组类型，处理元素类型
        if (typeSymbol is IArrayTypeSymbol arrayType)
        {
            CollectNamespacesFromType(arrayType.ElementType, namespaces);
        }
    }

    /// <summary>
    /// 从 MSBuild 配置中读取 Channel 名称列表
    /// </summary>
    /// <param name="configOptions">配置选项提供器</param>
    /// <returns>Channel 名称数组，如果未配置则返回空数组</returns>
    private static string[] ReadChannelNamesFromConfig(AnalyzerConfigOptionsProvider configOptions)
    {
        // 尝试读取全局配置
        if (configOptions.GlobalOptions.TryGetValue("build_property.PulseRPC_ClientChannels", out var channelsValue))
        {
            if (!string.IsNullOrWhiteSpace(channelsValue))
            {
                // 按分号或逗号分割
                return channelsValue
                    .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToArray();
            }
        }

        // 返回空数组
        return Array.Empty<string>();
    }
}
