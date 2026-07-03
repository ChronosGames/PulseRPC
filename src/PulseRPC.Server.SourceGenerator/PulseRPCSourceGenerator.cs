using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using PulseRPC.Server.SourceGenerator.Analyzers;
using PulseRPC.Server.SourceGenerator.Generators;
using PulseRPC.Server.SourceGenerator.Helpers;
using PulseRPC.Server.SourceGenerator.Models;
using System.Collections.Immutable;
using System.Text;

namespace PulseRPC.Server.SourceGenerator;

/// <summary>
/// PulseRPC 主Source Generator - 编译时性能优化核心（增量生成器版本）
/// </summary>
[Generator]
public class PulseRPCSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 1. 收集候选接口（只识别继承 IPulseHub 的接口）
        var candidateInterfaces = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsCandidateInterface(node),
                transform: static (ctx, _) => GetInterfaceDeclaration(ctx))
            .Where(static i => i != null)
            .Collect();

        // 2. 读取 MSBuild 配置：PulseRPC_ServerChannels
        var channelNames = context.AnalyzerConfigOptionsProvider
            .Select(static (provider, _) =>
            {
                if (provider.GlobalOptions.TryGetValue("build_property.PulseRPC_ServerChannels", out var value))
                {
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(s => s.Trim())
                            .Where(s => !string.IsNullOrEmpty(s))
                            .ToArray();
                    }
                }
                return Array.Empty<string>();
            });

        // 3. 获取编译信息（用于程序集扫描）
        var compilation = context.CompilationProvider;

        // 4. 组合所有数据源
        var combinedData = candidateInterfaces
            .Combine(channelNames)
            .Combine(compilation);

        // 5. 注册主生成逻辑
        context.RegisterSourceOutput(combinedData, static (spc, source) =>
        {
            var ((interfaces, channels), compilation) = source;
            ExecuteGeneration(spc, interfaces, channels, compilation);
        });
    }

    /// <summary>
    /// 执行代码生成的主逻辑
    /// </summary>
    private static void ExecuteGeneration(
        SourceProductionContext context,
        ImmutableArray<InterfaceDeclarationSyntax?> candidateInterfaces,
        string[] channelNames,
        Compilation compilation)
    {
// #if DEBUG
//         if (!System.Diagnostics.Debugger.IsAttached)
//         {
//             System.Diagnostics.Debugger.Launch();
//         }
// #endif

        try
        {
            var serviceModels = new List<ServiceModel>();
            var scannedAssemblies = new HashSet<IAssemblySymbol>(SymbolEqualityComparer.Default);

            // 自动扫描直接引用的用户项目程序集
            var autoScanAssemblies = GetUserProjectAssemblies(compilation);
            foreach (var assembly in autoScanAssemblies)
            {
                if (scannedAssemblies.Add(assembly))
                {
                    var assemblyServiceModels = ScanAssemblyForServices(assembly, compilation);

                    foreach (var serviceModel in assemblyServiceModels)
                    {
                        if (serviceModels.All(s => s.InterfaceFullName != serviceModel.InterfaceFullName))
                        {
                            serviceModels.Add(serviceModel);
                        }
                    }
                }
            }

            // 分析直接找到的候选接口
            foreach (var interfaceDeclaration in candidateInterfaces.Where(i => i != null))
            {
                var semanticModel = compilation.GetSemanticModel(interfaceDeclaration!.SyntaxTree);
                var serviceModel = ServiceAnalyzer.AnalyzeInterface(interfaceDeclaration!, semanticModel);

                if (serviceModel != null && serviceModels.All(s => s.InterfaceName != serviceModel.InterfaceName))
                {
                    serviceModels.Add(serviceModel);
                }
            }

            // 如果没有找到服务接口，生成空的协议号映射表以保持编译兼容性
            if (serviceModels.Count == 0)
            {
                // 生成空的协议号映射表
                GenerateEmptyProtocolIdMapping(context);

                // 生成诊断信息
                var descriptor = new DiagnosticDescriptor(
                    "PULSE001",
                    "No IPulseHub interfaces found",
                    "No interfaces inheriting from IPulseHub were found in the compilation",
                    "PulseRPC.Server.SourceGenerator",
                    DiagnosticSeverity.Info,
                    true);

                context.ReportDiagnostic(Diagnostic.Create(descriptor, Location.None));
                return;
            }

            // 为所有服务方法分配协议号 - 需要适配新的上下文
            AssignProtocolIdsForIncremental(serviceModels, context);

            // 生成协议号映射表
            GenerateProtocolIdMapping(context, serviceModels);

            // 为每个服务生成代理类
            foreach (var serviceModel in serviceModels)
            {
                GenerateServiceProxy(context, serviceModel);
            }

            // 生成全局路由表（传入 channelNames）
            GenerateGlobalRoutingTable(context, serviceModels, channelNames);

            // 生成响应序列化器注册表
            GenerateResponseSerializers(context, serviceModels);

            // 报告成功信息
            ReportGenerationSuccess(context, serviceModels);

            // ========== IPulseReceiver 代码生成 ==========
            // 扫描所有 IPulseReceiver 接口并生成服务端推送代理
            var receiverModels = new List<ReceiverModel>();

            // 从已扫描的程序集中查找 IPulseReceiver 接口
            foreach (var assembly in scannedAssemblies)
            {
                var assemblyReceivers = ScanAssemblyForReceivers(assembly);
                receiverModels.AddRange(assemblyReceivers);
            }

            // 为每个接收器生成代理代码
            if (receiverModels.Count > 0)
            {
                // 为接收器分配协议号
                AssignReceiverProtocolIds(receiverModels);

                // 为每个接收器生成代理
                foreach (var receiver in receiverModels)
                {
                    GenerateReceiverProxy(context, receiver);
                }

                // 生成统一的 DI 扩展方法
                var unifiedExtensions = ReceiverProxyGenerator.GenerateUnifiedDIExtensions(receiverModels);
                context.AddSource("PulseReceiverServiceExtensions.g.cs", SourceText.From(unifiedExtensions, Encoding.UTF8));

                // 生成接收器协议号映射表
                var receiverProtocolMapping = ReceiverProxyGenerator.GenerateReceiverProtocolIdMapping(receiverModels);
                context.AddSource("ReceiverProtocolIdMapping.g.cs", SourceText.From(receiverProtocolMapping, Encoding.UTF8));

                // 报告接收器生成成功
                ReportReceiverGenerationSuccess(context, receiverModels);
            }
        }
        catch (Exception ex)
        {
            // 报告生成错误
            var descriptor = new DiagnosticDescriptor(
                "PULSE999",
                "Source generation failed",
                $"PulseRPC source generation failed with error: {ex.Message}",
                "PulseRPC.Server.SourceGenerator",
                DiagnosticSeverity.Error,
                true);

            context.ReportDiagnostic(Diagnostic.Create(descriptor, Location.None));
        }
    }

    /// <summary>
    /// 检查节点是否为候选接口（只识别继承 IPulseHub 的接口）
    /// </summary>
    private static bool IsCandidateInterface(SyntaxNode node)
    {
        if (node is not InterfaceDeclarationSyntax interfaceDeclaration)
            return false;

        // 只检查是否继承 IPulseHub 接口
        if (interfaceDeclaration.BaseList?.Types != null)
        {
            return interfaceDeclaration.BaseList.Types
                .Any(baseType =>
                {
                    var typeName = baseType.Type.ToString();
                    return typeName == "IPulseHub" ||
                           typeName.EndsWith(".IPulseHub") ||
                           typeName.Contains("IPulseHub");
                });
        }

        return false;
    }

    /// <summary>
    /// 获取接口声明（只验证继承 IPulseHub 的接口）
    /// </summary>
    private static InterfaceDeclarationSyntax? GetInterfaceDeclaration(GeneratorSyntaxContext context)
    {
        var interfaceDeclaration = (InterfaceDeclarationSyntax)context.Node;
        var interfaceSymbol = context.SemanticModel.GetDeclaredSymbol(interfaceDeclaration);

        if (interfaceSymbol == null)
            return null;

        // 只验证是否继承 IPulseHub 接口
        var implementsIPulseHub = interfaceSymbol.AllInterfaces.Any(i => i.Name == "IPulseHub");

        if (implementsIPulseHub)
            return interfaceDeclaration;

        return null;
    }

    /// <summary>
    /// 为增量生成器分配协议号
    /// </summary>
    private static void AssignProtocolIdsForIncremental(List<ServiceModel> serviceModels, SourceProductionContext context)
    {
        var usedIds = new Dictionary<ushort, (string service, string method)>();
        var manualIds = new HashSet<ushort>();

        // 第一遍：收集所有手动指定的协议号
        foreach (var service in serviceModels)
        {
            foreach (var method in service.Methods)
            {
                if (method.ProtocolId != 0)
                {
                    manualIds.Add(method.ProtocolId);

                    // 检查手动指定的协议号是否冲突
                    if (usedIds.TryGetValue(method.ProtocolId, out var existing))
                    {
                        var descriptor = new DiagnosticDescriptor(
                            "PULSE003",
                            "Protocol ID conflict detected",
                            $"Protocol ID 0x{method.ProtocolId:X4} is already used by {existing.service}.{existing.method}. " +
                            $"Method {service.InterfaceName}.{method.MethodName} cannot use the same protocol ID. " +
                            $"Please manually specify a different protocol ID using [Protocol(0xXXXX)] attribute.",
                            "PulseRPC.Server.SourceGenerator",
                            DiagnosticSeverity.Error,
                            true);

                        context.ReportDiagnostic(Diagnostic.Create(descriptor, Location.None));
                    }
                    else
                    {
                        usedIds[method.ProtocolId] = (service.InterfaceName, method.MethodName);
                    }
                }
            }
        }

        // 第二遍：为没有手动指定的方法生成协议号
        foreach (var service in serviceModels)
        {
            foreach (var method in service.Methods)
            {
                if (method.ProtocolId == 0)
                {
                    var protocolId = GenerateProtocolIdInternal(service, method, usedIds, manualIds);
                    method.ProtocolId = protocolId;
                    usedIds[protocolId] = (service.InterfaceName, method.MethodName);
                }
            }
        }
    }

    /// <summary>
    /// 生成协议号（基于 FNV-1a 哈希）
    /// </summary>
    private static ushort GenerateProtocolIdInternal(
        ServiceModel service,
        MethodModel method,
        Dictionary<ushort, (string service, string method)> usedIds,
        HashSet<ushort> manualIds)
    {
        // 构造方法签名字符串 - 排除 CancellationToken 参数（与客户端保持一致）
        var parameterTypes = ProtocolIdHelper.FilterCancellationToken(
            method.Parameters.Select(p => p.TypeFullName));

        var signature = ProtocolIdHelper.BuildMethodSignature(
            service.InterfaceFullName,
            method.MethodName,
            parameterTypes);

        // 使用辅助类生成唯一协议号
        var usedIdSet = new HashSet<ushort>(usedIds.Keys);
        return ProtocolIdHelper.GenerateUniqueProtocolId(signature, usedIdSet, manualIds);
    }

    /// <summary>
    /// 生成服务代理类
    /// </summary>
    private static void GenerateServiceProxy(SourceProductionContext context, ServiceModel serviceModel)
    {
        var sourceText = ServiceProxyGenerator.GenerateSourceText(serviceModel);
        var fileName = $"{serviceModel.InterfaceName.TrimStart('I')}.Proxy.g.cs";

        context.AddSource(fileName, sourceText);
    }

    /// <summary>
    /// 生成全局路由表
    /// </summary>
    private static void GenerateGlobalRoutingTable(SourceProductionContext context, List<ServiceModel> serviceModels, string[] channelNames)
    {
        var sourceText = RoutingTableGenerator.GenerateSourceText(serviceModels, channelNames);
        context.AddSource("ServiceRoutingTable.g.cs", sourceText);
    }

    /// <summary>
    /// 生成响应序列化器
    /// </summary>
    private static void GenerateResponseSerializers(SourceProductionContext context, List<ServiceModel> serviceModels)
    {
        var sourceText = ResponseSerializerGenerator.GenerateSourceText(serviceModels);
        context.AddSource("ResponseSerializers.g.cs", sourceText);
    }

    /// <summary>
    /// 生成协议号映射表
    /// </summary>
    private static void GenerateProtocolIdMapping(SourceProductionContext context, List<ServiceModel> serviceModels)
    {
        var sourceText = ProtocolIdGenerator.GenerateProtocolIdMappingTable(serviceModels);
        context.AddSource("ProtocolIdMapping.g.cs", sourceText);
    }

    /// <summary>
    /// 生成空的协议号映射表（当没有服务时）
    /// </summary>
    private static void GenerateEmptyProtocolIdMapping(SourceProductionContext context)
    {
        var code = @"// <auto-generated />
#nullable enable

using System;
using System.Reflection;
using PulseRPC.Protocol;

namespace PulseRPC.Generated;

/// <summary>
/// Empty protocol ID mapping table (no services found)
/// </summary>
public static partial class ProtocolIdMapping
{
    /// <summary>
    /// Get MethodInfo by protocol ID (always returns null when no services exist)
    /// </summary>
    public static MethodInfo? GetMethod(Type serviceType, ProtocolId protocolId)
    {
        return null;
    }

    /// <summary>
    /// Check if a protocol ID is valid (always returns false when no services exist)
    /// </summary>
    public static bool IsValidProtocolId(Type serviceType, ProtocolId protocolId)
    {
        return false;
    }
}";
        context.AddSource("ProtocolIdMapping.g.cs", code);
    }

    /// <summary>
    /// 报告生成成功信息
    /// </summary>
    private static void ReportGenerationSuccess(SourceProductionContext context, List<ServiceModel> serviceModels)
    {
        var totalMethods = serviceModels.Sum(s => s.Methods.Count);

        var descriptor = new DiagnosticDescriptor(
            "PULSE100",
            "PulseRPC source generation completed successfully",
            $"Generated optimized code for {serviceModels.Count} services with {totalMethods} methods. " +
            $"Generated service proxies, routing table, and response serializers.",
            "PulseRPC.Server.SourceGenerator",
            DiagnosticSeverity.Info,
            true);

        context.ReportDiagnostic(Diagnostic.Create(descriptor, Location.None));
    }

    /// <summary>
    /// 获取用户项目程序集（排除框架库和NuGet包）
    /// </summary>
    private static List<IAssemblySymbol> GetUserProjectAssemblies(Compilation compilation)
    {
        var userAssemblies = new List<IAssemblySymbol>();

        foreach (var referencedAssembly in compilation.References)
        {
            var assembly = compilation.GetAssemblyOrModuleSymbol(referencedAssembly) as IAssemblySymbol;
            if (assembly == null)
                continue;

            // 过滤掉框架库和常见 NuGet 包
            var assemblyName = assembly.Name;
            if (IsFrameworkOrNuGetAssembly(assemblyName))
                continue;

            userAssemblies.Add(assembly);
        }

        return userAssemblies;
    }

    /// <summary>
    /// 判断是否为框架库或 NuGet 包
    /// </summary>
    private static bool IsFrameworkOrNuGetAssembly(string assemblyName)
    {
        // 排除常见的框架和 NuGet 包前缀
        var excludedPrefixes = new[]
        {
            "System.",
            "Microsoft.",
            "netstandard",
            "mscorlib",
            "Newtonsoft.",
            "MessagePack",
            "MemoryPack",
            "Cysharp.",
            "NuGet.",
            "NSubstitute",
            "xunit",
            "FluentAssertions",
            "Testcontainers",
            "Consul",
            "dotnet-etcd",
        };

        foreach (var prefix in excludedPrefixes)
        {
            if (assemblyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 扫描程序集中的所有服务接口
    /// </summary>
    private static List<ServiceModel> ScanAssemblyForServices(IAssemblySymbol assembly, Compilation compilation)
    {
        var serviceModels = new List<ServiceModel>();

        // 遍历程序集中的所有类型
        foreach (var type in GetAllTypesInAssembly(assembly))
        {
            if (type is INamedTypeSymbol namedType && namedType.TypeKind == TypeKind.Interface)
            {
                // 检查是否实现了IPulseHub接口或有PulseService特性
                if (IsServiceInterface(namedType))
                {
                    var serviceModel = CreateServiceModelFromSymbol(namedType);
                    if (serviceModel != null)
                    {
                        serviceModels.Add(serviceModel);
                    }
                }
            }
        }

        return serviceModels;
    }

    /// <summary>
    /// 获取程序集中的所有类型
    /// </summary>
    private static IEnumerable<INamedTypeSymbol> GetAllTypesInAssembly(IAssemblySymbol assembly)
    {
        var stack = new Stack<INamespaceSymbol>();
        stack.Push(assembly.GlobalNamespace);

        while (stack.Count > 0)
        {
            var @namespace = stack.Pop();

            foreach (var type in @namespace.GetTypeMembers())
            {
                yield return type;
            }

            foreach (var nestedNamespace in @namespace.GetNamespaceMembers())
            {
                stack.Push(nestedNamespace);
            }
        }
    }

    /// <summary>
    /// 检查接口是否为服务接口（只识别继承 IPulseHub 的接口）
    /// </summary>
    private static bool IsServiceInterface(INamedTypeSymbol typeSymbol)
    {
        // 排除 IPulseReceiver 接口（它们有独立的处理逻辑）
        if (IsReceiverInterface(typeSymbol))
            return false;

        // 只检查是否继承 IPulseHub 接口
        return typeSymbol.AllInterfaces.Any(i => i.Name == "IPulseHub");
    }

    /// <summary>
    /// 检查接口是否为接收器接口（继承 IPulseReceiver）
    /// </summary>
    private static bool IsReceiverInterface(INamedTypeSymbol typeSymbol)
    {
        // 排除 IPulseReceiver 接口本身
        if (typeSymbol.Name == "IPulseReceiver")
            return false;

        // 检查是否直接继承 IPulseReceiver
        return typeSymbol.AllInterfaces.Any(i => i.Name == "IPulseReceiver");
    }

    /// <summary>
    /// 扫描程序集中的所有接收器接口
    /// </summary>
    private static List<ReceiverModel> ScanAssemblyForReceivers(IAssemblySymbol assembly)
    {
        var receiverModels = new List<ReceiverModel>();

        foreach (var type in GetAllTypesInAssembly(assembly))
        {
            if (type is INamedTypeSymbol namedType &&
                namedType.TypeKind == TypeKind.Interface &&
                IsReceiverInterface(namedType))
            {
                var receiverModel = CreateReceiverModelFromSymbol(namedType);
                if (receiverModel != null)
                {
                    receiverModels.Add(receiverModel);
                }
            }
        }

        return receiverModels;
    }

    /// <summary>
    /// 从符号创建接收器模型
    /// </summary>
    private static ReceiverModel? CreateReceiverModelFromSymbol(INamedTypeSymbol typeSymbol)
    {
        try
        {
            var methods = new List<ReceiverMethodModel>();

            foreach (var member in typeSymbol.GetMembers())
            {
                if (member is IMethodSymbol methodSymbol &&
                    methodSymbol.DeclaredAccessibility == Accessibility.Public &&
                    methodSymbol.MethodKind == MethodKind.Ordinary)
                {
                    var returnType = methodSymbol.ReturnType.ToDisplayString();
                    var isAsync = IsAsyncMethod(methodSymbol);

                    // 仅支持 Task/ValueTask 返回类型，跳过非异步方法（void 返回等）
                    if (!isAsync)
                    {
                        // 跳过非 Task/ValueTask 返回类型的方法
                        continue;
                    }

                    var method = new ReceiverMethodModel
                    {
                        MethodName = methodSymbol.Name,
                        ReturnTypeName = returnType,
                        IsAsync = isAsync,
                        Parameters = methodSymbol.Parameters.Select(p => new ReceiverParameterModel
                        {
                            Name = p.Name,
                            TypeName = p.Type.Name,
                            TypeFullName = p.Type.ToDisplayString()
                        }).ToList()
                    };

                    methods.Add(method);
                }
            }

            return new ReceiverModel
            {
                InterfaceName = typeSymbol.Name,
                InterfaceFullName = typeSymbol.ToDisplayString(),
                Namespace = typeSymbol.ContainingNamespace.ToDisplayString(),
                Methods = methods
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 为接收器方法分配协议号
    /// </summary>
    private static void AssignReceiverProtocolIds(List<ReceiverModel> receivers)
    {
        var usedIds = new HashSet<ushort>();

        foreach (var receiver in receivers)
        {
            foreach (var method in receiver.Methods)
            {
                // 构造方法签名 - 排除 CancellationToken 参数（与客户端保持一致）
                var parameterTypes = ProtocolIdHelper.FilterCancellationToken(
                    method.Parameters.Select(p => p.TypeFullName));

                var signature = ProtocolIdHelper.BuildMethodSignature(
                    receiver.InterfaceFullName,
                    method.MethodName,
                    parameterTypes);

                // 生成唯一协议号
                var protocolId = ProtocolIdHelper.GenerateUniqueProtocolId(signature, usedIds);

                usedIds.Add(protocolId);
                method.ProtocolId = protocolId;
            }
        }
    }

    /// <summary>
    /// 生成接收器代理
    /// </summary>
    private static void GenerateReceiverProxy(SourceProductionContext context, ReceiverModel receiver)
    {
        var sourceText = ReceiverProxyGenerator.GenerateSourceText(receiver);
        var fileName = $"{receiver.InterfaceName.TrimStart('I')}.ReceiverProxy.g.cs";

        context.AddSource(fileName, sourceText);
    }

    /// <summary>
    /// 报告接收器生成成功
    /// </summary>
    private static void ReportReceiverGenerationSuccess(SourceProductionContext context, List<ReceiverModel> receivers)
    {
        var totalMethods = receivers.Sum(r => r.Methods.Count);

        var descriptor = new DiagnosticDescriptor(
            "PULSE101",
            "PulseReceiver source generation completed",
            $"Generated HubContext proxies for {receivers.Count} receivers with {totalMethods} methods.",
            "PulseRPC.Server.SourceGenerator",
            DiagnosticSeverity.Info,
            true);

        context.ReportDiagnostic(Diagnostic.Create(descriptor, Location.None));
    }

    /// <summary>
    /// 从符号创建服务模型
    /// </summary>
    private static ServiceModel? CreateServiceModelFromSymbol(INamedTypeSymbol typeSymbol)
    {
        try
        {
            var methods = new List<MethodModel>();

            foreach (var member in typeSymbol.GetMembers())
            {
                if (member is IMethodSymbol methodSymbol && methodSymbol.DeclaredAccessibility == Accessibility.Public)
                {
                    var returnType = methodSymbol.ReturnType.ToDisplayString();
                    var isAsync = IsAsyncMethod(methodSymbol);
                    var responseTypeFullName = GetResponseTypeFullName(returnType);

                    // 尝试读取 [Protocol] 特性
                    var manualProtocolId = TryGetProtocolIdFromAttribute(methodSymbol);

                    var methodAuthorization = AuthorizationHelper.GetAuthorization(methodSymbol);

                    var isReentrant = methodSymbol.GetAttributes()
                        .Any(attr => attr.AttributeClass?.Name is "ReentrantAttribute" or "Reentrant");

                    var method = new MethodModel
                    {
                        MethodName = methodSymbol.Name,
                        IsAsync = isAsync,
                        ReturnTypeName = returnType,
                        ReturnTypeFullName = returnType,
                        IsGenericTask = isAsync && (returnType.Contains("Task<") || returnType.Contains("ValueTask<")),
                        Parameters = methodSymbol.Parameters.Select(p => new ParameterModel
                        {
                            Name = p.Name,
                            TypeName = p.Type.Name,
                            TypeFullName = p.Type.ToDisplayString(),
                            IsMemoryPackable = false // TODO: 检查是否可序列化
                        }).ToList(),
                        ResponseTypeFullName = responseTypeFullName,
                        IsResponseMemoryPackable = responseTypeFullName != null,
                        ProtocolId = manualProtocolId ?? 0, // 0 表示需要自动生成
                        Authorization = methodAuthorization,
                        IsReentrant = isReentrant
                    };

                    methods.Add(method);
                }
            }

            // Extract ChannelName and ServiceName from ChannelAttribute
            var channelName = "default";
            string? serviceName = null;

            var channelAttr = typeSymbol.GetAttributes()
                .FirstOrDefault(attr => attr.AttributeClass?.Name is "ChannelAttribute" or "Channel");

            if (channelAttr != null)
            {
                // Get ChannelName from first constructor argument
                if (channelAttr.ConstructorArguments.Length > 0)
                {
                    channelName = channelAttr.ConstructorArguments[0].Value?.ToString() ?? "default";
                }

                // Get ServiceName from named argument or second constructor argument
                var serviceNameArg = channelAttr.NamedArguments
                    .FirstOrDefault(na => na.Key == "ServiceName");

                if (!serviceNameArg.Equals(default(KeyValuePair<string, TypedConstant>)))
                {
                    serviceName = serviceNameArg.Value.Value?.ToString();
                }
                else if (channelAttr.ConstructorArguments.Length > 1)
                {
                    serviceName = channelAttr.ConstructorArguments[1].Value?.ToString();
                }
            }

            // If ServiceName is not specified, use interface name as fallback
            serviceName ??= typeSymbol.Name;

            // Extract authorization information from interface
            var authorization = AuthorizationHelper.GetAuthorization(typeSymbol);

            return new ServiceModel
            {
                InterfaceName = typeSymbol.Name,
                InterfaceFullName = typeSymbol.ToDisplayString(),
                Namespace = typeSymbol.ContainingNamespace.ToDisplayString(),
                ChannelName = channelName,
                ServiceName = serviceName,
                Methods = methods,
                Authorization = authorization
            };
        }
        catch
        {
            // 如果创建服务模型时出错，返回null
            return null;
        }
    }

    /// <summary>
    /// 检查方法是否为异步方法
    /// </summary>
    private static bool IsAsyncMethod(IMethodSymbol methodSymbol)
    {
        var returnTypeName = methodSymbol.ReturnType.ToDisplayString();
        return returnTypeName.StartsWith("System.Threading.Tasks.Task") ||
               returnTypeName.StartsWith("Task") ||
               returnTypeName.StartsWith("System.Threading.Tasks.ValueTask") ||
               returnTypeName.StartsWith("ValueTask");
    }

    private static string? GetResponseTypeFullName(string returnType)
    {
        // 处理非泛型 Task 和 ValueTask
        if (returnType is "Task" or "System.Threading.Tasks.Task" or "ValueTask" or "System.Threading.Tasks.ValueTask")
        {
            return null;
        }

        if (returnType.StartsWith("Task<") && returnType.EndsWith(">"))
        {
            return returnType[5..^1].Trim();
        }

        if (returnType.StartsWith("ValueTask<") && returnType.EndsWith(">"))
        {
            return returnType[10..^1].Trim();
        }

        if (returnType.StartsWith("System.Threading.Tasks.Task<") && returnType.EndsWith(">"))
        {
            return returnType[28..^1].Trim();
        }

        if (returnType.StartsWith("System.Threading.Tasks.ValueTask<") && returnType.EndsWith(">"))
        {
            return returnType[33..^1].Trim();
        }

        throw new InvalidOperationException($"{returnType} is not a valid response type");
    }

    /// <summary>
    /// 尝试从方法的 [Protocol] 特性读取协议号
    /// </summary>
    private static ushort? TryGetProtocolIdFromAttribute(IMethodSymbol methodSymbol)
    {
        var protocolAttr = methodSymbol.GetAttributes()
            .FirstOrDefault(attr => attr.AttributeClass?.Name is "ProtocolAttribute" or "Protocol");

        if (protocolAttr == null)
            return null;

        // 读取构造函数参数
        if (protocolAttr.ConstructorArguments.Length > 0)
        {
            var arg = protocolAttr.ConstructorArguments[0];

            // 处理 ushort 参数
            if (arg.Type?.SpecialType == SpecialType.System_UInt16 && arg.Value is ushort ushortValue)
            {
                return ushortValue;
            }

            // 处理字符串参数（十六进制）
            if (arg.Type?.SpecialType == SpecialType.System_String && arg.Value is string hexValue)
            {
                // 移除 "0x" 或 "0X" 前缀
                var valueStr = hexValue.Trim();
                if (valueStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    valueStr = valueStr.Substring(2);

                // 解析十六进制
                if (ushort.TryParse(valueStr, System.Globalization.NumberStyles.HexNumber, null, out var parsedValue))
                {
                    return parsedValue;
                }
            }
        }

        return null;
    }

}
