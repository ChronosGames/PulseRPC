using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using PulseRPC.Server.SourceGenerator.Analyzers;
using PulseRPC.Server.SourceGenerator.Generators;
using PulseRPC.Server.SourceGenerator.Helpers;
using PulseRPC.Server.SourceGenerator.Models;

namespace PulseRPC.Server.SourceGenerator;

/// <summary>
/// PulseRPC 主Source Generator - 编译时性能优化核心
/// </summary>
[Generator]
public class PulseRPCSourceGenerator : ISourceGenerator
{
    private const string PulseServerGenerationAttributeName = "PulseServerGenerationAttribute";

    public void Initialize(GeneratorInitializationContext context)
    {
        // 注册语法接收器来收集候选接口和类
        context.RegisterForSyntaxNotifications(() => new ServiceSyntaxReceiver());
    }

    public void Execute(GeneratorExecutionContext context)
    {
// #if DEBUG
//         if (!System.Diagnostics.Debugger.IsAttached)
//         {
//             System.Diagnostics.Debugger.Launch();
//         }
// #endif

        try
        {
            // 读取 MSBuild 配置：PulseRPC_ServerChannels
            var channelNames = ReadChannelNamesFromConfig(context);

            // 获取语法接收器
            if (context.SyntaxReceiver is not ServiceSyntaxReceiver syntaxReceiver)
                return;

            // 分析PulseServerGeneration特性标记的类
            var serviceModels = new List<ServiceModel>();
            var serverGenerationClasses = new List<ClassDeclarationSyntax>();
            var scannedAssemblies = new HashSet<IAssemblySymbol>(SymbolEqualityComparer.Default);

            // 收集带有PulseServerGeneration特性的类
            foreach (var classDeclaration in syntaxReceiver.CandidateClasses)
            {
                var semanticModel = context.Compilation.GetSemanticModel(classDeclaration.SyntaxTree);
                var classSymbol = semanticModel.GetDeclaredSymbol(classDeclaration);

                if (classSymbol != null && HasPulseServerGenerationAttribute(classSymbol))
                {
                    serverGenerationClasses.Add(classDeclaration);

                    // 从PulseServerGeneration特性中获取要扫描的类型
                    var typesToScan = GetMarkerTypesFromClass(classSymbol, semanticModel);

                    foreach (var markerType in typesToScan)
                    {
                        var assembly = markerType.ContainingAssembly;
                        if (scannedAssemblies.Add(assembly))
                        {
                            // 扫描标记类型所在程序集中的所有服务接口
                            var assemblyServiceModels = ScanAssemblyForServices(assembly, context.Compilation);

                            // 去重：只添加不存在的服务模型
                            foreach (var serviceModel in assemblyServiceModels)
                            {
                                if (serviceModels.All(s => s.InterfaceFullName != serviceModel.InterfaceFullName))
                                {
                                    serviceModels.Add(serviceModel);
                                }
                            }
                        }
                    }
                }
            }

            // 自动扫描直接引用的用户项目程序集（新增功能）
            var autoScanAssemblies = GetUserProjectAssemblies(context.Compilation);
            foreach (var assembly in autoScanAssemblies)
            {
                if (scannedAssemblies.Add(assembly))
                {
                    var assemblyServiceModels = ScanAssemblyForServices(assembly, context.Compilation);

                    foreach (var serviceModel in assemblyServiceModels)
                    {
                        if (serviceModels.All(s => s.InterfaceFullName != serviceModel.InterfaceFullName))
                        {
                            serviceModels.Add(serviceModel);
                        }
                    }
                }
            }

            // 分析直接找到的候选接口（向后兼容）
            foreach (var interfaceDeclaration in syntaxReceiver.CandidateInterfaces)
            {
                var semanticModel = context.Compilation.GetSemanticModel(interfaceDeclaration.SyntaxTree);
                var serviceModel = ServiceAnalyzer.AnalyzeInterface(interfaceDeclaration, semanticModel);

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

                if (serverGenerationClasses.Count > 0)
                {
                    var descriptor = new DiagnosticDescriptor(
                        "PULSE002",
                        "PulseServerGeneration configured but no services found",
                        "PulseServerGeneration attribute is configured but no services implementing IPulseHub were found in the referenced assemblies",
                        "PulseRPC.Server.SourceGenerator",
                        DiagnosticSeverity.Warning,
                        true);

                    context.ReportDiagnostic(Diagnostic.Create(descriptor, Location.None));
                }
                else
                {
                    // 生成诊断信息
                    var descriptor = new DiagnosticDescriptor(
                        "PULSE001",
                        "No PulseService interfaces found",
                        "No interfaces marked with [PulseService] attribute were found in the compilation",
                        "PulseRPC.Server.SourceGenerator",
                        DiagnosticSeverity.Info,
                        true);

                    context.ReportDiagnostic(Diagnostic.Create(descriptor, Location.None));
                }
                return;
            }

            // 为所有服务方法分配协议号
            ProtocolIdGenerator.AssignProtocolIds(serviceModels, context);

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
    /// 生成服务代理类
    /// </summary>
    private static void GenerateServiceProxy(GeneratorExecutionContext context, ServiceModel serviceModel)
    {
        var sourceText = ServiceProxyGenerator.GenerateSourceText(serviceModel);
        var fileName = $"{serviceModel.InterfaceName.TrimStart('I')}.Proxy.g.cs";

        context.AddSource(fileName, sourceText);
    }

    /// <summary>
    /// 生成全局路由表
    /// </summary>
    private static void GenerateGlobalRoutingTable(GeneratorExecutionContext context, List<ServiceModel> serviceModels, string[] channelNames)
    {
        var sourceText = RoutingTableGenerator.GenerateSourceText(serviceModels, channelNames);
        context.AddSource("ServiceRoutingTable.g.cs", sourceText);
    }

    /// <summary>
    /// 生成响应序列化器
    /// </summary>
    private static void GenerateResponseSerializers(GeneratorExecutionContext context, List<ServiceModel> serviceModels)
    {
        var sourceText = ResponseSerializerGenerator.GenerateSourceText(serviceModels);
        context.AddSource("ResponseSerializers.g.cs", sourceText);
    }

    /// <summary>
    /// 生成协议号映射表
    /// </summary>
    private static void GenerateProtocolIdMapping(GeneratorExecutionContext context, List<ServiceModel> serviceModels)
    {
        var sourceText = ProtocolIdGenerator.GenerateProtocolIdMappingTable(serviceModels);
        context.AddSource("ProtocolIdMapping.g.cs", sourceText);
    }

    /// <summary>
    /// 生成空的协议号映射表（当没有服务时）
    /// </summary>
    private static void GenerateEmptyProtocolIdMapping(GeneratorExecutionContext context)
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
public static class ProtocolIdMapping
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
    private static void ReportGenerationSuccess(GeneratorExecutionContext context, List<ServiceModel> serviceModels)
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
    /// 检查类是否有PulseServerGeneration特性
    /// </summary>
    private static bool HasPulseServerGenerationAttribute(INamedTypeSymbol classSymbol)
    {
        return classSymbol.GetAttributes().Any(attr =>
            attr.AttributeClass?.Name == PulseServerGenerationAttributeName ||
            attr.AttributeClass?.Name == "PulseServerGeneration");
    }

    /// <summary>
    /// 从类的PulseServerGeneration特性中获取标记类型
    /// </summary>
    private static List<ITypeSymbol> GetMarkerTypesFromClass(INamedTypeSymbol classSymbol, SemanticModel semanticModel)
    {
        var markerTypes = new List<ITypeSymbol>();

        foreach (var attribute in classSymbol.GetAttributes())
        {
            if (attribute.AttributeClass?.Name != PulseServerGenerationAttributeName &&
                attribute.AttributeClass?.Name != "PulseServerGeneration")
            {
                continue;
            }

            // 获取特性的构造函数参数（markerType）
            if (attribute.ConstructorArguments.Length > 0 &&
                attribute.ConstructorArguments[0].Value is ITypeSymbol markerType)
            {
                markerTypes.Add(markerType);
            }
        }

        return markerTypes;
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
    /// 检查接口是否为服务接口
    /// </summary>
    private static bool IsServiceInterface(INamedTypeSymbol typeSymbol)
    {
        // 检查是否有PulseService特性
        if (typeSymbol.GetAttributes().Any(attr => attr.AttributeClass?.Name == "PulseServiceAttribute" ||
                                                   attr.AttributeClass?.Name == "PulseService"))
        {
            return true;
        }

        // TODO: 排除IPulseReceiver接口本身（历史原因）
        if (typeSymbol.Name is "IPulseReceiver")
            return false;

        // 检查是否实现了IPulseHub接口
        return typeSymbol.AllInterfaces.Any(i => i.Name == "IPulseHub");
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
                        Authorization = methodAuthorization
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

    /// <summary>
    /// 从 MSBuild 配置中读取 Channel 名称列表
    /// </summary>
    /// <param name="context">生成器执行上下文</param>
    /// <returns>Channel 名称数组，如果未配置则返回空数组</returns>
    private static string[] ReadChannelNamesFromConfig(GeneratorExecutionContext context)
    {
        // 尝试读取全局配置
        if (context.AnalyzerConfigOptions.GlobalOptions.TryGetValue("build_property.PulseRPC_ServerChannels", out var channelsValue))
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

/// <summary>
/// 语法接收器 - 收集候选的PulseService接口和PulseServerGeneration类
/// </summary>
internal class ServiceSyntaxReceiver : ISyntaxReceiver
{
    public List<InterfaceDeclarationSyntax> CandidateInterfaces { get; } = new();
    public List<ClassDeclarationSyntax> CandidateClasses { get; } = new();

    public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
    {
        // 寻找接口声明
        if (syntaxNode is InterfaceDeclarationSyntax interfaceDeclaration)
        {
            // 检查是否有可能是PulseService接口的特征
            if (HasPotentialPulseServiceAttribute(interfaceDeclaration))
            {
                CandidateInterfaces.Add(interfaceDeclaration);
            }
        }
        // 寻找类声明（用于PulseServerGeneration特性）
        else if (syntaxNode is ClassDeclarationSyntax classDeclaration)
        {
            // 检查是否有可能是PulseServerGeneration类的特征
            if (HasPotentialPulseServerGenerationAttribute(classDeclaration))
            {
                CandidateClasses.Add(classDeclaration);
            }
        }
    }

    /// <summary>
    /// 快速检查接口是否可能有PulseService特性或继承IPulseHub
    /// </summary>
    private static bool HasPotentialPulseServiceAttribute(InterfaceDeclarationSyntax interfaceDeclaration)
    {
        // 检查特性
        var hasAttribute = interfaceDeclaration.AttributeLists
            .SelectMany(al => al.Attributes)
            .Any(attr =>
            {
                var name = attr.Name.ToString();
                return name.Contains("PulseService") ||
                       name.Contains("Service") ||
                       name == "PulseService" ||
                       name == "PulseServiceAttribute";
            });

        if (hasAttribute)
            return true;

        // 检查是否继承IPulseHub接口
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
    /// 快速检查类是否可能有PulseServerGeneration特性
    /// </summary>
    private static bool HasPotentialPulseServerGenerationAttribute(ClassDeclarationSyntax classDeclaration)
    {
        // 检查特性
        return classDeclaration.AttributeLists
            .SelectMany(al => al.Attributes)
            .Any(attr =>
            {
                var name = attr.Name.ToString();
                return name.Contains("PulseServerGeneration") ||
                       name == "PulseServerGeneration" ||
                       name == "PulseServerGenerationAttribute";
            });
    }

}
