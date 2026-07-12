using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using PulseRPC.Generator.Generators;
using PulseRPC.Generator.Helpers;

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

        // 将 Roslyn 配置提供器投影为值对象。直接把 AnalyzerConfigOptionsProvider
        // 组合进输出会导致相同编译的第二次运行仍被判定为 Modified。
        var generatorOptions = context.AnalyzerConfigOptionsProvider
            .Select(static (provider, _) => ReadGeneratorOptions(provider));

        // 查找带有 PulseClientGeneration 特性的类
        var classDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => s is ClassDeclarationSyntax { AttributeLists.Count: > 0 },
                transform: (ctx, _) => GetServiceTypesFromClass(ctx))
            .Where(m => m.Items.Length > 0)
            .WithTrackingName("AttributedClasses");

        // 分离服务类型和事件类型 - 基于接口实现而非名称
        var allServiceTypes = classDeclarations.Collect()
            .Select((classDeclarationArray, _) =>
            {
                var result = new List<ServiceTypeInfo>();

                foreach (var classDeclaration in classDeclarationArray)
                {
                    foreach (var serviceType in classDeclaration.Items)
                    {
                        // 检查是否实现了IPulseHub接口（服务接口）
                        if (serviceType.Type != null && IsNetworkService(serviceType.Type))
                        {
                            result.Add(serviceType);
                        }
                    }
                }

                return result.ToImmutableArray();
            })
            .WithTrackingName("ServiceTypes");

        var allEventTypes = classDeclarations.Collect()
            .Select((classDeclarationArray, _) =>
            {
                var result = new List<ServiceTypeInfo>();

                foreach (var classDeclaration in classDeclarationArray)
                {
                    foreach (var serviceType in classDeclaration.Items)
                    {
                        // 检查是否实现了 [Channel("CLIENT")] : IPulseHub 推送接收器接口（事件接口）
                        if (serviceType.Type != null && IsEventReceiver(serviceType.Type))
                        {
                            result.Add(serviceType);
                        }
                    }
                }

                return result.ToImmutableArray();
            })
            .WithTrackingName("EventTypes");

        // 注册服务代理源代码输出
        context.RegisterSourceOutput(allServiceTypes.Combine(allEventTypes).Combine(generatorOptions), (spc, combined) =>
        {
            var serviceTypes = combined.Left.Left;
            var eventTypes = combined.Left.Right;
            var options = combined.Right;

            // 读取 MSBuild 配置：PulseRPC_ClientChannels
            var channelNames = ReadChannelNamesFromConfig(options.ChannelNames);

            // 去重服务类型（同一个接口可能被多个类的 PulseClientGeneration 特性引用）
            var uniqueServiceTypes = serviceTypes
                .Where(st => st.Type is INamedTypeSymbol)
                .GroupBy(st => ((INamedTypeSymbol)st.Type!).ToDisplayString())
                .Select(g => g.First())
                .ToList();

            // 一次性为编译单元内所有 IPulseHub 接口聚合分配协议号（冲突检测范围与服务端
            // AssignProtocolIdsForIncremental 保持一致，避免跨接口哈希碰撞在客户端被漏检）
            var serviceProtocolIds = ProtocolIdGenerator.AssignProtocolIds(
                uniqueServiceTypes.Select(st => st.Type).OfType<INamedTypeSymbol>(),
                spc);

            // 生成服务代理
            foreach (var serviceTypeInfo in uniqueServiceTypes)
            {
                if (serviceTypeInfo.Type is INamedTypeSymbol namedType)
                {
                    var stubCode = GenerateServiceStub(namedType, spc, serviceProtocolIds);
                    // 使用完整类型名称（包含命名空间）确保文件名唯一
                    // 格式: {Namespace}_{TypeNameWithoutI}.Stub.g.cs
                    var fileName = $"{GetSafeFileName(namedType)}.Stub.g.cs";
                    spc.AddSource(fileName, SourceText.From(stubCode, Encoding.UTF8));
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

            // 生成服务扩展方法
            if (serviceNamedTypes.Length > 0)
            {
                var extensionsCode = GenerateChannelManagerExtensions(serviceNamedTypes, eventNamedTypes);
                spc.AddSource("PulseRPC.Client.Generated.ServiceExtensions.g.cs", SourceText.From(extensionsCode, Encoding.UTF8));
            }

            // 生成 PulseClient 工厂扩展方法
            if (serviceNamedTypes.Length > 0 || eventNamedTypes.Length > 0)
            {
                var factoryExtensionsCode = PulseClientExtensionsGenerator.GeneratePulseClientExtensions(serviceNamedTypes, eventNamedTypes);
                spc.AddSource("PulseRPC.Client.Generated.FactoryExtensions.g.cs", SourceText.From(factoryExtensionsCode, Encoding.UTF8));
            }

            // 生成 IClientChannel 泛型扩展方法（GetHub<T>, RegisterReceiver<T>）
            if (serviceNamedTypes.Length > 0 || eventNamedTypes.Length > 0)
            {
                var channelExtensionsCode = ClientChannelGenericExtensionsGenerator.Generate(serviceNamedTypes, eventNamedTypes);
                spc.AddSource("PulseRPC.Client.Generated.ChannelExtensions.g.cs", SourceText.From(channelExtensionsCode, Encoding.UTF8));
            }
        });

        // 注册事件处理器源代码输出
        context.RegisterSourceOutput(allEventTypes.Combine(generatorOptions), (spc, combined) =>
        {
            var eventTypes = combined.Left;
            var options = combined.Right;

            // 读取 EventHandler 生成配置
            var generateEventHandlers = options.GenerateEventHandlers;

            // 去重事件类型（同一个接口可能被多个类的 PulseClientGeneration 特性引用）
            var uniqueEventTypes = eventTypes
                .Where(et => et.Type is INamedTypeSymbol)
                .GroupBy(et => ((INamedTypeSymbol)et.Type!).ToDisplayString())
                .Select(g => g.First())
                .ToList();

            var validEventTypes = uniqueEventTypes;

            // 一次性为编译单元内所有 [Channel("CLIENT")] : IPulseHub 推送接收器接口聚合分配协议号（独立于 Hub 协议号空间，
            // 冲突检测范围与服务端 AssignReceiverProtocolIds 保持一致）
            var receiverProtocolIds = ProtocolIdGenerator.AssignProtocolIds(
                validEventTypes.Select(et => et.Type).OfType<INamedTypeSymbol>(),
                spc);

            // 注意：支持类型已移至 PulseRPC.Abstractions 库中（PulseRPC.Client.Events 命名空间）
            // 不再需要在此处生成 EventHandlerSupportTypes

            // 生成事件处理器
            foreach (var eventTypeInfo in validEventTypes)
            {
                if (eventTypeInfo.Type is INamedTypeSymbol namedType)
                {
                    // 检查接口是否有 [GenerateEventHandler] 特性
                    var hasEventHandlerAttribute = HasGenerateEventHandlerAttribute(namedType);

                    // 根据接口特性或全局配置决定是否生成事件处理器
                    // 优先级：接口特性 > 全局配置
                    // 默认不生成 EventHandler（改变原有行为）
                    // 格式: {Namespace}_{TypeNameWithoutI}.EventHandler.g.cs
                    if (hasEventHandlerAttribute || generateEventHandlers)
                    {
                        var eventHandlerCode = EventHandlerGenerator.GenerateEventHandler(namedType, spc, receiverProtocolIds);
                        var eventHandlerFileName = $"{GetSafeFileName(namedType)}.EventHandler.g.cs";
                        spc.AddSource(eventHandlerFileName, SourceText.From(eventHandlerCode, Encoding.UTF8));
                    }

                    // 始终生成接收器调度器（轻量级，推荐使用）
                    // 格式: {Namespace}_{TypeNameWithoutI}.Dispatcher.g.cs
                    var dispatcherCode = ReceiverDispatcherGenerator.GenerateReceiverDispatcher(namedType, spc, receiverProtocolIds);
                    var dispatcherFileName = $"{GetSafeFileName(namedType)}.Dispatcher.g.cs";
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

        });
    }

    private static ServiceTypeCollection GetServiceTypesFromClass(GeneratorSyntaxContext context)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
        var semanticModel = context.SemanticModel;

        // 获取类的语义模型
        var classSymbol = semanticModel.GetDeclaredSymbol(classDeclaration);
        if (classSymbol == null)
        {
            return ServiceTypeCollection.Empty;
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

        return new ServiceTypeCollection(result.ToArray());
    }

    private static bool IsNetworkService(INamedTypeSymbol typeSymbol)
    {
        // 继承 IPulseHub，且非客户端实现的推送接收器（[Channel("CLIENT")]）→ 客户端调用服务端的 Stub
        if (!typeSymbol.AllInterfaces.Any(i => i.Name == "IPulseHub") || HasClientChannel(typeSymbol))
            return false;

        // §5.2-C 显式覆盖：[PulseHub(Consume=false)] 表示本侧（客户端）不生成调用方 Stub
        if (PulseHubOverrideHelper.TryGetOverride(typeSymbol, out _, out var consume) && !consume)
            return false;

        return true;
    }

    private static bool IsEventReceiver(INamedTypeSymbol typeSymbol)
    {
        // 统一标记模型：所有远程契约都继承 IPulseHub；由 [Channel("CLIENT")] 判定为
        // 客户端实现的推送接收器（服务端推送、客户端接收），为其生成 Dispatcher。
        if (!HasClientChannel(typeSymbol))
            return false;

        // §5.2-C 显式覆盖：[PulseHub(Provide=false)] 表示本侧（客户端）不生成被调方 Dispatcher
        if (PulseHubOverrideHelper.TryGetOverride(typeSymbol, out var provide, out _) && !provide)
            return false;

        return true;
    }

    /// <summary>
    /// 判断接口是否标注 <c>[Channel("CLIENT")]</c>（大小写不敏感）。
    /// </summary>
    private static bool HasClientChannel(INamedTypeSymbol typeSymbol)
    {
        var channelAttr = typeSymbol.GetAttributes()
            .FirstOrDefault(attr => attr.AttributeClass?.Name is "ChannelAttribute" or "Channel");

        if (channelAttr?.ConstructorArguments.Length > 0)
        {
            var channelName = channelAttr.ConstructorArguments[0].Value?.ToString();
            return string.Equals(channelName, ClientChannelConstants.ClientChannelName, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static string GenerateServiceStub(INamedTypeSymbol interfaceSymbol, SourceProductionContext context, Dictionary<string, ushort> protocolIds)
    {
        var interfaceName = interfaceSymbol.Name;
        var isGlobalNamespace = interfaceSymbol.ContainingNamespace.IsGlobalNamespace;
        var namespaceName = isGlobalNamespace ? null : interfaceSymbol.ContainingNamespace.ToDisplayString();

        // 获取通道特性
        var defaultChannelName = GetChannelAttributeValue(interfaceSymbol) ?? "default";

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
        sb.AppendLine("using PulseRPC.Shared;");

        // 收集方法参数和返回值类型的命名空间
        var additionalNamespaces = CollectNamespacesFromMethods(interfaceSymbol);
        foreach (var ns in additionalNamespaces.OrderBy(x => x))
        {
            // 跳过已经添加的系统命名空间和当前接口的命名空间
            if (ns.StartsWith("System") || (!isGlobalNamespace && ns == namespaceName))
                continue;
            sb.AppendLine($"using {ns};");
        }

        // 添加当前类型的命名空间（非全局命名空间时）
        if (!isGlobalNamespace)
        {
            sb.AppendLine($"using {interfaceSymbol.ContainingNamespace.ToDisplayString()};");
        }

        sb.AppendLine();

        // 全局命名空间时不生成 namespace 块
        if (!isGlobalNamespace)
        {
            sb.AppendLine($"namespace {namespaceName}");
            sb.AppendLine("{");
        }

        // 缩进：全局命名空间时无缩进，否则有4空格缩进
        var indent = isGlobalNamespace ? "" : "    ";
        var memberIndent = indent + "    ";
        var bodyIndent = memberIndent + "    ";

        // 生成基于 IClientChannel 的代理类
        sb.AppendLine($"{indent}/// <summary>");
        sb.AppendLine($"{indent}/// 自动生成的 {interfaceName} 客户端代理");
        sb.AppendLine($"{indent}/// 使用协议号进行高性能方法路由");
        sb.AppendLine($"{indent}/// </summary>");
        sb.AppendLine($"{indent}public sealed class {interfaceName}Stub : {interfaceName}");
        sb.AppendLine($"{indent}{{");
        sb.AppendLine($"{memberIndent}private readonly IClientChannel _connection;");
        sb.AppendLine();

        // 生成协议号常量
        GenerateProtocolIdConstants(sb, interfaceSymbol, protocolIds, memberIndent);

        sb.AppendLine($"{memberIndent}/// <summary>");
        sb.AppendLine($"{memberIndent}/// 初始化 {interfaceName} 客户端 Stub");
        sb.AppendLine($"{memberIndent}/// </summary>");
        sb.AppendLine($"{memberIndent}/// <param name=\"connection\">连接</param>");
        sb.AppendLine($"{memberIndent}public {interfaceName}Stub(IClientChannel connection)");
        sb.AppendLine($"{memberIndent}{{");
        sb.AppendLine($"{bodyIndent}_connection = connection ?? throw new ArgumentNullException(nameof(connection));");
        sb.AppendLine($"{memberIndent}}}");
        sb.AppendLine();

        // 生成基于连接上下文的方法实现（包括继承的接口方法）
        foreach (var methodSymbol2 in GetAllInterfaceMethods(interfaceSymbol))
        {
            // 自动处理所有公共方法
            if (methodSymbol2.DeclaredAccessibility != Accessibility.Public)
                continue;

            // 获取方法的协议号（使用方法的声明接口来计算）
            var methodKey = MethodIdentity.CreateLookupKey(methodSymbol2);
            ushort protocolId = protocolIds.TryGetValue(methodKey, out var id) ? id : (ushort)0;

            // 生成基于连接上下文的方法实现
            GenerateConnectionContextMethodImplementation(sb, methodSymbol2, protocolId, defaultChannelName, namespaceName, interfaceName, memberIndent, bodyIndent);
        }

        // 结束连接代理类
        sb.AppendLine($"{indent}}}");

        // 结束命名空间（非全局命名空间时）
        if (!isGlobalNamespace)
        {
            sb.AppendLine("}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// 生成协议号常量（包括继承的接口方法）
    /// </summary>
    private static void GenerateProtocolIdConstants(StringBuilder sb, INamedTypeSymbol interfaceSymbol, Dictionary<string, ushort> protocolIds, string indent)
    {
        sb.AppendLine($"{indent}// ==================== 协议号常量 ====================");
        sb.AppendLine($"{indent}// 使用 FNV-1a 哈希算法生成，确保客户端和服务端一致");
        sb.AppendLine();

        foreach (var method in GetAllInterfaceMethods(interfaceSymbol))
        {
            if (method.DeclaredAccessibility != Accessibility.Public)
                continue;

            // 使用方法的声明接口来构建键
            var declaringInterface = method.ContainingType;
            var methodKey = MethodIdentity.CreateLookupKey(method);

            if (protocolIds.TryGetValue(methodKey, out var protocolId))
            {
                var methodSignature = ProtocolIdGenerator.BuildMethodSignature(method);
                var constName = ProtocolIdGenerator.GetProtocolIdConstantName(method);

                sb.AppendLine($"{indent}/// <summary>");
                sb.AppendLine($"{indent}/// 协议号: {method.Name}");
                sb.AppendLine($"{indent}/// 方法签名: {methodSignature}");
                sb.AppendLine($"{indent}/// 声明接口: {declaringInterface.ToDisplayString()}");
                sb.AppendLine($"{indent}/// </summary>");
                sb.AppendLine($"{indent}private const ushort {constName} = 0x{protocolId:X4}; // {protocolId}");
                sb.AppendLine();
            }
        }
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
        sb.AppendLine("using PulseRPC.Client.Configuration;");
        sb.AppendLine("using PulseRPC.Shared;");

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
                sb.AppendLine($"        /// 获取指定连接的 {interfaceName} 服务 Stub");
                sb.AppendLine($"        /// </summary>");
                sb.AppendLine($"        public static Task<{fullTypeName}Stub> Get{serviceName}StubAsync(this IPulseClient self, string connectionId, ServiceProxyOptions? options = null, CancellationToken cancellationToken = default)");
                sb.AppendLine("        {");
                sb.AppendLine("            if (options != null) throw new NotSupportedException(\"ServiceProxyOptions is not consumed when connectionId is explicit. Pass null.\");");
                sb.AppendLine("            var connection = self.Connections.GetConnection(connectionId);");
                sb.AppendLine("            if (connection == null)");
                sb.AppendLine("            {");
                sb.AppendLine("                throw new ArgumentException($\"连接不存在: {connectionId}\", nameof(connectionId));");
                sb.AppendLine("            }");
                sb.AppendLine();
                sb.AppendLine($"            return Task.FromResult(new {fullTypeName}Stub(connection));");
                sb.AppendLine("        }");
                sb.AppendLine();
            }
        }

        // 注意：EventHandler 工厂方法已移除（EventHandler 现在是可选生成的）
        // 用户可以通过以下方式使用：
        // 1. 直接 new EventHandler（如果接口标记了 [GenerateEventHandler]）
        // 2. 使用轻量级的 Dispatcher（始终生成）
        // 3. 使用 IClientChannel.RegisterReceiver<T>() 扩展方法

        // 生成事件监听器扩展方法（使用 Dispatcher，不依赖 EventHandler）
        if (eventTypes.Length > 0)
        {
            foreach (var interfaceSymbol in eventTypes)
            {
                if (interfaceSymbol == null) continue;

                var interfaceName = interfaceSymbol.Name;
                var fullTypeName = GetFullTypeName(interfaceSymbol);
                var dispatcherClassName = interfaceName.StartsWith("I")
                    ? interfaceName.Substring(1) + "Dispatcher"
                    : interfaceName + "Dispatcher";

                // 生成 IClientChannel 的事件监听器扩展方法（使用轻量级 Dispatcher）
                sb.AppendLine($"        /// <summary>");
                sb.AppendLine($"        /// 在指定连接上注册 {interfaceName} 事件监听器");
                sb.AppendLine($"        /// 使用轻量级 Dispatcher 进行事件分发");
                sb.AppendLine($"        /// </summary>");
                sb.AppendLine($"        public static ISubscriptionToken RegisterEventListener(this IClientChannel channel, {fullTypeName} listener)");
                sb.AppendLine("        {");
                sb.AppendLine("            if (channel == null)");
                sb.AppendLine("                throw new ArgumentNullException(nameof(channel));");
                sb.AppendLine("            if (listener == null)");
                sb.AppendLine("                throw new ArgumentNullException(nameof(listener));");
                sb.AppendLine();
                sb.AppendLine($"            var dispatcher = new {fullTypeName.Replace(interfaceName, dispatcherClassName)}(listener);");
                sb.AppendLine($"            return dispatcher.RegisterTo(channel);");
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

    private sealed class ServiceTypeCollection : IEquatable<ServiceTypeCollection>
    {
        public static ServiceTypeCollection Empty { get; } = new(Array.Empty<ServiceTypeInfo>());

        public ServiceTypeCollection(ServiceTypeInfo[] items)
        {
            Items = items;
        }

        public ServiceTypeInfo[] Items { get; }

        public bool Equals(ServiceTypeCollection? other)
        {
            if (other is null || Items.Length != other.Items.Length)
            {
                return false;
            }

            for (var index = 0; index < Items.Length; index++)
            {
                if (!SymbolEqualityComparer.Default.Equals(Items[index].Type, other.Items[index].Type))
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object? obj) => Equals(obj as ServiceTypeCollection);

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = 17;
                foreach (var item in Items)
                {
                    hashCode = (hashCode * 31) +
                        (item.Type is null ? 0 : SymbolEqualityComparer.Default.GetHashCode(item.Type));
                }

                return hashCode;
            }
        }
    }

    private readonly struct GeneratorOptions : IEquatable<GeneratorOptions>
    {
        public GeneratorOptions(string channelNames, bool generateEventHandlers)
        {
            ChannelNames = channelNames;
            GenerateEventHandlers = generateEventHandlers;
        }

        public string ChannelNames { get; }

        public bool GenerateEventHandlers { get; }

        public bool Equals(GeneratorOptions other) =>
            StringComparer.Ordinal.Equals(ChannelNames, other.ChannelNames) &&
            GenerateEventHandlers == other.GenerateEventHandlers;

        public override bool Equals(object? obj) => obj is GeneratorOptions other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (StringComparer.Ordinal.GetHashCode(ChannelNames) * 397) ^ GenerateEventHandlers.GetHashCode();
            }
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
            if (baseInterface.Name is "IPulseHub")
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
        return MethodIdentity.CreateClrSignatureKey(method);
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
    /// 统一去掉接口名称的 I 前缀
    /// </summary>
    private static string GetSafeFileName(INamedTypeSymbol typeSymbol)
    {
        // 统一去掉 I 前缀
        var typeName = typeSymbol.Name.StartsWith("I") && typeSymbol.Name.Length > 1 && char.IsUpper(typeSymbol.Name[1])
            ? typeSymbol.Name.Substring(1)
            : typeSymbol.Name;

        if (typeSymbol.ContainingNamespace.IsGlobalNamespace)
        {
            return typeName;
        }
        else
        {
            // 使用完整的类型名（包含命名空间），将点替换为下划线以生成有效的文件名
            var fullTypeName = $"{typeSymbol.ContainingNamespace.ToDisplayString()}.{typeName}";
            return fullTypeName.Replace('.', '_');
        }
    }

    private static void GenerateConnectionContextMethodImplementation(
        StringBuilder sb,
        IMethodSymbol methodSymbol,
        ushort protocolId,
        string defaultChannelName,
        string? namespaceName,
        string interfaceName,
        string memberIndent,
        string bodyIndent)
    {
        var methodName = methodSymbol.Name;
        var returnType = methodSymbol.ReturnType.ToDisplayString();

        // 获取方法级别的通道特性，如果没有则使用接口级别的默认通道
        var channelName = GetChannelAttributeValue(methodSymbol) ?? defaultChannelName;

        var constName = ProtocolIdGenerator.GetProtocolIdConstantName(methodSymbol);
        var methodSignature = ProtocolIdGenerator.BuildMethodSignature(methodSymbol);

        sb.AppendLine($"{memberIndent}/// <summary>");
        sb.AppendLine($"{memberIndent}/// 调用 {methodName} 方法");
        sb.AppendLine($"{memberIndent}/// Protocol ID: 0x{protocolId:X4} ({protocolId})");
        sb.AppendLine($"{memberIndent}/// </summary>");

        // 生成方法签名
        var parameters = methodSymbol.Parameters;
        var paramList = string.Join(", ", parameters.Select(p =>
            $"{p.Type.ToDisplayString()} {p.Name}"));

        // 检查是否是异步方法
        var isAsync = returnType.StartsWith("System.Threading.Tasks.Task") || returnType.StartsWith("System.Threading.Tasks.ValueTask");

        if (isAsync)
        {
            sb.AppendLine($"{memberIndent}public async {returnType} {methodName}({paramList})");
        }
        else
        {
            sb.AppendLine($"{memberIndent}public {returnType} {methodName}({paramList})");
        }

        sb.AppendLine($"{memberIndent}{{");

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
                GenerateCommandMethodBody(sb, methodSymbol, interfaceName, methodName, dataParameters, tokenName, protocolId, bodyIndent);
            }
            else
            {
                // Request/Response 方法（有返回值）- 使用零拷贝路径
                // 使用 ExtractGenericType 正确处理嵌套泛型类型（如 List<T>、Dictionary<K,V> 等）
                var taskType = ExtractGenericType(returnType);

                GenerateRequestMethodBody(sb, methodSymbol, interfaceName, methodName, dataParameters, taskType, tokenName, protocolId, bodyIndent);
            }
        }
        else
        {
            // 生成同步实现（通过异步包装）
            sb.AppendLine($"{bodyIndent}// 同步方法通过异步实现");
            var asyncCall = $"{methodName}Async({string.Join(", ", parameters.Select(p => p.Name))})";
            if (returnType == "void")
            {
                sb.AppendLine($"{bodyIndent}{asyncCall}.GetAwaiter().GetResult();");
            }
            else
            {
                sb.AppendLine($"{bodyIndent}return {asyncCall}.GetAwaiter().GetResult();");
            }
        }

        sb.AppendLine($"{memberIndent}}}");
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
        ushort protocolId,
        string indent)
    {
        var constName = ProtocolIdGenerator.GetProtocolIdConstantName(methodSymbol);
        var innerIndent = indent + "    ";

        sb.AppendLine($"{indent}// ========== 零拷贝优化路径：Request/Response ==========");
        sb.AppendLine($"{indent}// 使用协议号: {constName} = 0x{protocolId:X4} ({protocolId})");
        sb.AppendLine($"{indent}// Step 1: 租借序列化缓冲区");
        sb.AppendLine($"{indent}var __buffer__ = _connection.RentSerializationBuffer(256);");
        sb.AppendLine($"{indent}try");
        sb.AppendLine($"{indent}{{");

        // Step 2: 显式 MemoryPack 序列化
        if (dataParameters.Count == 0)
        {
            // 无参数
            sb.AppendLine($"{innerIndent}// 无参数，序列化空对象");
            sb.AppendLine($"{innerIndent}MemoryPack.MemoryPackSerializer.Serialize(__buffer__, PulseRPC.EmptyResponse.Instance);");
        }
        else if (dataParameters.Count == 1)
        {
            // 单参数
            var param = dataParameters[0];
            sb.AppendLine($"{innerIndent}// 序列化单个参数");
            sb.AppendLine($"{innerIndent}MemoryPack.MemoryPackSerializer.Serialize(__buffer__, {param.Name});");
        }
        else
        {
            // 多参数：创建元组
            var tupleType = "(" + string.Join(", ", dataParameters.Select(p => p.Type.ToDisplayString())) + ")";
            var tupleValues = "(" + string.Join(", ", dataParameters.Select(p => p.Name)) + ")";
            sb.AppendLine($"{innerIndent}// 序列化多个参数为元组");
            sb.AppendLine($"{innerIndent}var __request__ = {tupleValues};");
            sb.AppendLine($"{innerIndent}MemoryPack.MemoryPackSerializer.Serialize(__buffer__, __request__);");
        }

        sb.AppendLine($"");
        sb.AppendLine($"{innerIndent}// Step 3: 获取已序列化的字节");
        sb.AppendLine($"{innerIndent}var __serializedRequest__ = __buffer__ is System.Buffers.ArrayBufferWriter<byte> __abw__ ");
        sb.AppendLine($"{innerIndent}    ? __abw__.WrittenMemory ");
        sb.AppendLine($"{innerIndent}    : System.ReadOnlyMemory<byte>.Empty;");
        sb.AppendLine($"");
        sb.AppendLine($"{innerIndent}// Step 4: 使用协议号发送并等待响应（零拷贝）");
        var canonicalHub = interfaceName.TrimStart('I');
        sb.AppendLine($"{innerIndent}var __hubChannel__ = _connection as PulseRPC.Client.IHubAddressedClientChannel");
        sb.AppendLine($"{innerIndent}    ?? throw new InvalidOperationException(\"Generated PulseRPC stubs require IHubAddressedClientChannel for strict Hub routing.\");");
        sb.AppendLine($"{innerIndent}var __responseBytes__ = await __hubChannel__.InvokeHubRawAsync(\"{canonicalHub}\",");
        sb.AppendLine($"{innerIndent}    protocolId: {constName},");
        sb.AppendLine($"{innerIndent}    serializedRequest: __serializedRequest__,");
        sb.AppendLine($"{innerIndent}    cancellationToken: {tokenName});");
        sb.AppendLine($"");

        // Step 5: 反序列化响应
        if (responseType == "PulseRPC.EmptyResponse" || responseType == "EmptyResponse")
        {
            sb.AppendLine($"{innerIndent}// 空响应，直接返回");
            sb.AppendLine($"{innerIndent}return;");
        }
        else
        {
            sb.AppendLine($"{innerIndent}// Step 5: 显式反序列化响应");
            sb.AppendLine($"{innerIndent}return MemoryPack.MemoryPackSerializer.Deserialize<{responseType}>(__responseBytes__.Span)!;");
        }

        sb.AppendLine($"{indent}}}");
        sb.AppendLine($"{indent}finally");
        sb.AppendLine($"{indent}{{");
        sb.AppendLine($"{innerIndent}// Step 6: 归还缓冲区");
        sb.AppendLine($"{innerIndent}_connection.ReturnSerializationBuffer(__buffer__);");
        sb.AppendLine($"{indent}}}");
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
        ushort protocolId,
        string indent)
    {
        var constName = ProtocolIdGenerator.GetProtocolIdConstantName(methodSymbol);
        var innerIndent = indent + "    ";

        sb.AppendLine($"{indent}// ========== 零拷贝优化路径：Command/OneWay ==========");
        sb.AppendLine($"{indent}// 使用协议号: {constName} = 0x{protocolId:X4} ({protocolId})");
        sb.AppendLine($"{indent}// Step 1: 租借序列化缓冲区");
        sb.AppendLine($"{indent}var __buffer__ = _connection.RentSerializationBuffer(256);");
        sb.AppendLine($"{indent}try");
        sb.AppendLine($"{indent}{{");

        // Step 2: 显式 MemoryPack 序列化
        if (dataParameters.Count == 0)
        {
            sb.AppendLine($"{innerIndent}// 无参数，序列化空对象");
            sb.AppendLine($"{innerIndent}MemoryPack.MemoryPackSerializer.Serialize(__buffer__, PulseRPC.EmptyResponse.Instance);");
        }
        else if (dataParameters.Count == 1)
        {
            var param = dataParameters[0];
            sb.AppendLine($"{innerIndent}// 序列化单个参数");
            sb.AppendLine($"{innerIndent}MemoryPack.MemoryPackSerializer.Serialize(__buffer__, {param.Name});");
        }
        else
        {
            var tupleValues = "(" + string.Join(", ", dataParameters.Select(p => p.Name)) + ")";
            sb.AppendLine($"{innerIndent}// 序列化多个参数为元组");
            sb.AppendLine($"{innerIndent}var __command__ = {tupleValues};");
            sb.AppendLine($"{innerIndent}MemoryPack.MemoryPackSerializer.Serialize(__buffer__, __command__);");
        }

        sb.AppendLine($"");
        sb.AppendLine($"{innerIndent}// Step 3: 获取已序列化的字节");
        sb.AppendLine($"{innerIndent}var __serializedCommand__ = __buffer__ is System.Buffers.ArrayBufferWriter<byte> __abw__");
        sb.AppendLine($"{innerIndent}    ? __abw__.WrittenMemory");
        sb.AppendLine($"{innerIndent}    : System.ReadOnlyMemory<byte>.Empty;");
        sb.AppendLine($"");
        sb.AppendLine($"{innerIndent}// Step 4: 使用协议号发送（零拷贝，无需等待响应）");
        var canonicalHub = interfaceName.TrimStart('I');
        sb.AppendLine($"{innerIndent}var __hubChannel__ = _connection as PulseRPC.Client.IHubAddressedClientChannel");
        sb.AppendLine($"{innerIndent}    ?? throw new InvalidOperationException(\"Generated PulseRPC stubs require IHubAddressedClientChannel for strict Hub routing.\");");
        sb.AppendLine($"{innerIndent}await __hubChannel__.SendHubCommandAsync(\"{canonicalHub}\",");
        sb.AppendLine($"{innerIndent}    protocolId: {constName},");
        sb.AppendLine($"{innerIndent}    serializedCommand: __serializedCommand__,");
        sb.AppendLine($"{innerIndent}    cancellationToken: {tokenName});");
        sb.AppendLine($"{indent}}}");
        sb.AppendLine($"{indent}finally");
        sb.AppendLine($"{indent}{{");
        sb.AppendLine($"{innerIndent}// Step 5: 归还缓冲区");
        sb.AppendLine($"{innerIndent}_connection.ReturnSerializationBuffer(__buffer__);");
        sb.AppendLine($"{indent}}}");
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

    private static GeneratorOptions ReadGeneratorOptions(AnalyzerConfigOptionsProvider configOptions)
    {
        configOptions.GlobalOptions.TryGetValue("build_property.PulseRPC_ClientChannels", out var channelsValue);
        configOptions.GlobalOptions.TryGetValue("build_property.PulseRPC_GenerateEventHandlers", out var eventHandlersValue);

        var normalizedChannels = string.Join(",", ReadChannelNamesFromConfig(channelsValue));
        var generateEventHandlers = bool.TryParse(eventHandlersValue, out var parsedValue) && parsedValue;
        return new GeneratorOptions(normalizedChannels, generateEventHandlers);
    }

    /// <summary>
    /// 从 MSBuild 配置中读取 Channel 名称列表
    /// </summary>
    /// <param name="channelsValue">配置值</param>
    /// <returns>Channel 名称数组，如果未配置则返回空数组</returns>
    private static string[] ReadChannelNamesFromConfig(string? channelsValue)
    {
        if (!string.IsNullOrWhiteSpace(channelsValue))
        {
            return channelsValue!
                .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToArray();
        }

        return Array.Empty<string>();
    }

    /// <summary>
    /// 检查接口是否标记了 [GenerateEventHandler] 特性
    /// </summary>
    /// <param name="interfaceSymbol">接口符号</param>
    /// <returns>是否有 GenerateEventHandler 特性</returns>
    private static bool HasGenerateEventHandlerAttribute(INamedTypeSymbol interfaceSymbol)
    {
        return interfaceSymbol.GetAttributes()
            .Any(attr => attr.AttributeClass?.Name is "GenerateEventHandlerAttribute" or "GenerateEventHandler");
    }
}
