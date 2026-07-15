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
            var routerModels = new List<ServiceModel>();
            var receiverModels = new List<ReceiverModel>();
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
                        AddServiceModelIfMissing(serviceModels, serviceModel);
                    }

                    foreach (var routerModel in ScanAssemblyForRouterConsumers(assembly))
                    {
                        AddServiceModelIfMissing(routerModels, routerModel);
                    }

                    foreach (var receiverModel in ScanAssemblyForReceivers(assembly))
                    {
                        AddReceiverModelIfMissing(receiverModels, receiverModel);
                    }
                }
            }

            // 分析直接找到的候选接口
            foreach (var interfaceDeclaration in candidateInterfaces.Where(i => i != null))
            {
                var semanticModel = compilation.GetSemanticModel(interfaceDeclaration!.SyntaxTree);
                var serviceModel = ServiceAnalyzer.AnalyzeInterface(interfaceDeclaration!, semanticModel);

                if (serviceModel != null)
                {
                    AddServiceModelIfMissing(serviceModels, serviceModel);
                }

                if (semanticModel.GetDeclaredSymbol(interfaceDeclaration!) is INamedTypeSymbol interfaceSymbol)
                {
                    if (IsRouterConsumerInterface(interfaceSymbol))
                    {
                        var routerModel = ServiceAnalyzer.AnalyzeInterfaceForConsumption(interfaceDeclaration!, semanticModel);
                        if (routerModel != null)
                        {
                            AddServiceModelIfMissing(routerModels, routerModel);
                        }
                    }

                    if (IsReceiverInterface(interfaceSymbol))
                    {
                        var receiverModel = CreateReceiverModelFromSymbol(interfaceSymbol);
                        if (receiverModel != null)
                        {
                            AddReceiverModelIfMissing(receiverModels, receiverModel);
                        }
                    }
                }
            }

            if (serviceModels.Count == 0 && routerModels.Count == 0 && receiverModels.Count == 0)
            {
                GenerateEmptyProtocolIdMapping(context);
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

            if (serviceModels.Count > 0)
            {
                // 被调方路由表对独立 facet 去重；出站 RouterProxy 仍需实现完整的继承接口方法。
                DeduplicateFacadeInheritedMethods(serviceModels);
            }

            var protocolModels = new List<ServiceModel>(serviceModels);
            foreach (var routerModel in routerModels)
            {
                AddServiceModelIfMissing(protocolModels, routerModel);
            }

            if (protocolModels.Count > 0 && !ValidateCanonicalHubNames(context, protocolModels))
            {
                return;
            }

            if (protocolModels.Count > 0)
            {
                AssignProtocolIdsForIncremental(protocolModels, context);
                SynchronizeRouterProtocolIds(routerModels, protocolModels);

                if (!ValidateResponseTypes(context, protocolModels))
                {
                    return;
                }

                GenerateProtocolIdMapping(context, protocolModels);
            }
            else
            {
                GenerateEmptyProtocolIdMapping(context);
            }

            if (serviceModels.Count > 0)
            {
                foreach (var serviceModel in serviceModels)
                {
                    GenerateServiceProxy(context, serviceModel);
                }

                GenerateGlobalRoutingTable(context, serviceModels, channelNames);
                GenerateResponseSerializers(context, serviceModels);
                GenerateServiceManifest(context, serviceModels);
                ReportGenerationSuccess(context, serviceModels);
            }

            foreach (var routerModel in routerModels)
            {
                GenerateRouterProxy(context, routerModel);
            }

            if (receiverModels.Count > 0)
            {
                DeduplicateFacadeInheritedReceiverMethods(receiverModels);
                AssignReceiverProtocolIds(receiverModels, context);
                MarkReceiverOverloads(receiverModels);

                foreach (var receiver in receiverModels)
                {
                    GenerateReceiverProxy(context, receiver);
                }

                var unifiedExtensions = ReceiverProxyGenerator.GenerateDIExtensions(receiverModels);
                context.AddSource("PulseReceiverServiceExtensions.g.cs", SourceText.From(unifiedExtensions, Encoding.UTF8));

                var receiverProtocolMapping = ReceiverProxyGenerator.GenerateReceiverProtocolIdMapping(receiverModels);
                context.AddSource("ReceiverProtocolIdMapping.g.cs", SourceText.From(receiverProtocolMapping, Encoding.UTF8));

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
    /// <summary>
    /// 移除派生 facet Hub 中「继承自本编译单元内也独立作为顶层 Hub 被扫描」的基接口方法。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 扩大方法收集范围以包含继承接口方法（见 §11.2 风险 #1）后，像
    /// <c>IBackendHub : IPulseHub, IGuildHub</c> 这样"通过继承组合多个 facet"的既有模式
    /// 会导致 <c>IGuildHub.CreateGuildAsync</c> 同时出现在 <c>IGuildHub</c> 自身的路由表
    /// 和 <c>IBackendHub</c> 的路由表中——两者协议号相同（因为都基于同一声明接口计算），
    /// 但生成的全局路由表是单一 switch，无法容纳重复 case。
    /// </para>
    /// <para>
    /// 处理方式：只要方法的声明接口本身也在本次编译中被独立识别为顶层 Hub（有自己的
    /// <see cref="ServiceModel"/>），就认为该方法的路由已由声明接口自己提供，从派生 facet
    /// 中移除实际 switch case，避免重复路由项；同时把它保存在
    /// <see cref="ServiceModel.ProtocolAliases"/>，使严格 <c>(Hub, ProtocolId)</c> 映射仍接受
    /// 派生 facet 的 canonical Hub 名。纯 mixin 基接口（未独立注册为 Hub）的方法则保留，
    /// 从而仍然修复"客户端能调用、服务端未路由"的静默丢失问题。
    /// </para>
    /// </remarks>
    private static void DeduplicateFacadeInheritedMethods(List<ServiceModel> serviceModels)
    {
        var topLevelInterfaces = new HashSet<string>(serviceModels.Select(s => s.InterfaceFullName));

        foreach (var service in serviceModels)
        {
            var aliases = service.Methods.Where(method =>
                method.DeclaringInterfaceFullName != null &&
                method.DeclaringInterfaceFullName != service.InterfaceFullName &&
                topLevelInterfaces.Contains(method.DeclaringInterfaceFullName)).ToList();

            service.ProtocolAliases.AddRange(aliases);
            service.Methods.RemoveAll(aliases.Contains);
        }
    }

    private static void AddServiceModelIfMissing(List<ServiceModel> models, ServiceModel candidate)
    {
        if (models.All(model => !string.Equals(
                model.InterfaceFullName,
                candidate.InterfaceFullName,
                StringComparison.Ordinal)))
        {
            models.Add(candidate);
        }
    }

    private static void AddReceiverModelIfMissing(List<ReceiverModel> models, ReceiverModel candidate)
    {
        if (models.All(model => !string.Equals(
                model.InterfaceFullName,
                candidate.InterfaceFullName,
                StringComparison.Ordinal)))
        {
            models.Add(candidate);
        }
    }

    private static void SynchronizeRouterProtocolIds(
        List<ServiceModel> routerModels,
        List<ServiceModel> protocolModels)
    {
        foreach (var routerModel in routerModels)
        {
            var protocolModel = protocolModels.FirstOrDefault(model => string.Equals(
                model.InterfaceFullName,
                routerModel.InterfaceFullName,
                StringComparison.Ordinal));
            if (protocolModel is null || ReferenceEquals(protocolModel, routerModel))
            {
                continue;
            }

            var methodsByIdentity = protocolModel.Methods
                .Concat(protocolModel.ProtocolAliases)
                .GroupBy(method => method.MethodIdentity, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

            foreach (var method in routerModel.Methods)
            {
                if (methodsByIdentity.TryGetValue(method.MethodIdentity, out var assignedMethod))
                {
                    method.ProtocolId = assignedMethod.ProtocolId;
                    method.HasExplicitProtocolId = assignedMethod.HasExplicitProtocolId;
                    method.HasInvalidProtocolId = assignedMethod.HasInvalidProtocolId;
                }
            }

            foreach (var overloadGroup in routerModel.Methods.GroupBy(
                         method => method.MethodName,
                         StringComparer.Ordinal))
            {
                var hasOverloads = overloadGroup.Skip(1).Any();
                foreach (var method in overloadGroup)
                {
                    method.HasOverloads = hasOverloads;
                }
            }
        }
    }

    private static bool ValidateCanonicalHubNames(
        SourceProductionContext context,
        List<ServiceModel> serviceModels)
    {
        var isValid = true;
        foreach (var group in serviceModels.GroupBy(
                     service => service.InterfaceName.TrimStart('I'),
                     StringComparer.Ordinal))
        {
            var conflictingServices = group
                .Select(service => service.InterfaceFullName)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (conflictingServices.Count < 2)
            {
                continue;
            }

            isValid = false;
            var descriptor = new DiagnosticDescriptor(
                "PULSE005",
                "Canonical Hub name conflict",
                "Hub interfaces {0} map to the same canonical Hub name '{1}'. Rename a Hub so (Hub, ProtocolId) routing remains unambiguous.",
                "PulseRPC.Server.SourceGenerator",
                DiagnosticSeverity.Error,
                true);
            context.ReportDiagnostic(Diagnostic.Create(
                descriptor,
                Location.None,
                string.Join(", ", conflictingServices),
                group.Key));
        }

        return isValid;
    }

    /// <summary>
    /// 接收器版本的 <see cref="DeduplicateFacadeInheritedMethods"/>，理由相同。
    /// </summary>
    private static void DeduplicateFacadeInheritedReceiverMethods(List<ReceiverModel> receiverModels)
    {
        var topLevelInterfaces = new HashSet<string>(receiverModels.Select(r => r.InterfaceFullName));

        foreach (var receiver in receiverModels)
        {
            receiver.Methods.RemoveAll(method =>
                method.DeclaringInterfaceFullName != null &&
                method.DeclaringInterfaceFullName != receiver.InterfaceFullName &&
                topLevelInterfaces.Contains(method.DeclaringInterfaceFullName));
        }
    }

    private static void MarkReceiverOverloads(List<ReceiverModel> receiverModels)
    {
        foreach (var receiver in receiverModels)
        {
            foreach (var overloadGroup in receiver.Methods
                         .GroupBy(method => method.MethodName, StringComparer.Ordinal)
                         .Where(group => group.Skip(1).Any()))
            {
                foreach (var method in overloadGroup)
                {
                    method.HasOverloads = true;
                }
            }
        }
    }

    private static void AssignProtocolIdsForIncremental(List<ServiceModel> serviceModels, SourceProductionContext context)
    {
        var invalidMethods = new HashSet<MethodModel>();

        foreach (var service in serviceModels)
        {
            foreach (var overloadGroup in service.Methods.GroupBy(method => method.MethodName, StringComparer.Ordinal))
            {
                var hasOverloads = overloadGroup.Skip(1).Any();
                foreach (var method in overloadGroup)
                {
                    method.HasOverloads = hasOverloads;
                }
            }

            foreach (var wireSignatureGroup in service.Methods.GroupBy(method => method.MethodIdentity, StringComparer.Ordinal))
            {
                var methods = wireSignatureGroup.ToList();
                if (methods.Count < 2)
                {
                    continue;
                }

                foreach (var method in methods)
                {
                    invalidMethods.Add(method);
                }
                ReportWireSignatureCollision(context, service.InterfaceName, methods[1]);
            }

            foreach (var method in service.Methods)
            {
                if (method.Parameters.Count(parameter => parameter.IsCancellationToken) > 1)
                {
                    invalidMethods.Add(method);
                    ReportMultipleCancellationTokens(context, service.InterfaceName, method.MethodName, method.Location);
                }

                if (method.HasInvalidProtocolId)
                {
                    invalidMethods.Add(method);
                    ReportInvalidProtocolId(context, service.InterfaceName, method.MethodName, method.Location);
                }
            }
        }

        var usedIds = new Dictionary<ushort, (string service, string method, Location? location)>();

        // 第一遍：收集所有手动指定的协议号
        foreach (var service in serviceModels)
        {
            foreach (var method in service.Methods)
            {
                if (invalidMethods.Contains(method))
                {
                    continue;
                }

                if (method.HasExplicitProtocolId)
                {
                    if (method.ProtocolId == 0)
                    {
                        ReportReservedProtocolId(context, service.InterfaceName, method.MethodName, method.Location, isManual: true);
                        continue;
                    }

                    // 检查手动指定的协议号是否冲突
                    if (usedIds.TryGetValue(method.ProtocolId, out var existing))
                    {
                        ReportProtocolIdConflict(context, method.ProtocolId, existing, service.InterfaceName, method.MethodName, method.Location, usedIds.Keys, isManual: true);
                    }
                    else
                    {
                        usedIds[method.ProtocolId] = (service.InterfaceName, method.MethodName, method.Location);
                    }
                }
            }
        }

        // 第二遍：为没有手动指定的方法生成协议号（纯哈希函数，不做线性探测——探测会让协议号
        // 随编译单元中方法的增删而漂移，且客户端/服务端各自独立编译时可见的"邻居"集合可能不同，
        // 导致双方为同一方法算出不同协议号）。一旦发现冲突立即报错，要求开发者显式指定 [Protocol]。
        foreach (var service in serviceModels)
        {
            foreach (var method in service.Methods)
            {
                if (!invalidMethods.Contains(method) && !method.HasExplicitProtocolId)
                {
                    var protocolId = GenerateProtocolIdInternal(service, method);
                    method.ProtocolId = protocolId;

                    if (protocolId == 0)
                    {
                        ReportReservedProtocolId(context, service.InterfaceName, method.MethodName, method.Location, isManual: false);
                        continue;
                    }

                    if (usedIds.TryGetValue(protocolId, out var existing))
                    {
                        ReportProtocolIdConflict(context, protocolId, existing, service.InterfaceName, method.MethodName, method.Location, usedIds.Keys, isManual: false);
                    }
                    else
                    {
                        usedIds[protocolId] = (service.InterfaceName, method.MethodName, method.Location);
                    }
                }
            }

            // 组合 facet 的继承方法由其声明 Hub 提供实际 switch case，但派生 Hub 仍是该协议的
            // 合法寻址别名。别名使用同一纯哈希/手动协议号，不参与全局冲突登记。
            foreach (var alias in service.ProtocolAliases)
            {
                if (!alias.HasInvalidProtocolId && !alias.HasExplicitProtocolId)
                {
                    alias.ProtocolId = GenerateProtocolIdInternal(service, alias);
                    if (alias.ProtocolId == 0)
                    {
                        ReportReservedProtocolId(context, service.InterfaceName, alias.MethodName, alias.Location, isManual: false);
                    }
                }
            }
        }
    }

    /// <summary>
    /// 生成协议号（基于 FNV-1a 哈希的纯函数，不做线性探测）
    /// </summary>
    private static ushort GenerateProtocolIdInternal(ServiceModel service, MethodModel method)
    {
        // 构造方法签名字符串 - 排除 CancellationToken 参数（与客户端保持一致）
        var parameterTypes = ProtocolIdHelper.FilterCancellationToken(
            method.Parameters.Select(p => p.TypeFullName));

        // 使用方法实际声明所在接口的全名（继承方法为基接口），与客户端 BuildMethodSignature(IMethodSymbol)
        // 使用 method.ContainingType.ToDisplayString() 的取值口径完全一致（见 §11.2 风险 #1）
        var signature = ProtocolIdHelper.BuildMethodSignature(
            method.DeclaringInterfaceFullName ?? service.InterfaceFullName,
            method.MethodName,
            parameterTypes);

        return ProtocolIdHelper.GenerateProtocolId(signature);
    }

    /// <summary>
    /// 报告协议号冲突（PULSE003）
    /// </summary>
    /// <remarks>
    /// 协议号是方法签名的纯哈希函数，不做线性探测；一旦冲突（无论是手动号之间、自动号之间，
    /// 还是手动号与自动号之间）立即报错，要求开发者用 <c>[Protocol(0xXXXX)]</c> 显式区分，
    /// 而不是静默改号，避免协议号随代码提交或编译单元内容变化而漂移。
    /// 诊断定位在冲突方法的声明处（而非 <see cref="Location.None"/>），并在
    /// <see cref="Diagnostic.Properties"/> 中附带一个当前未被占用的建议协议号
    /// （键为 <c>SuggestedProtocolId</c>，值为 4 位十六进制字符串），供
    /// <c>ProtocolIdConflictCodeFixProvider</c> 自动插入 <c>[Protocol(0xXXXX)]</c> 使用。
    /// </remarks>
    private static void ReportProtocolIdConflict(
        SourceProductionContext context,
        ushort protocolId,
        (string service, string method, Location? location) existing,
        string conflictingService,
        string conflictingMethod,
        Location? conflictingLocation,
        ICollection<ushort> usedProtocolIds,
        bool isManual)
    {
        var reason = isManual
            ? "手动指定的协议号与既有方法冲突"
            : "自动计算的协议号（FNV-1a 哈希）与既有方法冲突";

        var descriptor = new DiagnosticDescriptor(
            "PULSE003",
            "Protocol ID conflict detected",
            $"[{reason}] Protocol ID 0x{protocolId:X4} is already used by {existing.service}.{existing.method}. " +
            $"Method {conflictingService}.{conflictingMethod} cannot use the same protocol ID. " +
            $"Please manually specify a different protocol ID using [Protocol(0xXXXX)] attribute.",
            "PulseRPC.Server.SourceGenerator",
            DiagnosticSeverity.Error,
            true);

        var properties = BuildSuggestedProtocolIdProperties(protocolId, usedProtocolIds);

        context.ReportDiagnostic(Diagnostic.Create(
            descriptor,
            conflictingLocation ?? Location.None,
            existing.location != null ? new[] { existing.location } : null,
            properties));
    }

    private static void ReportReservedProtocolId(
        SourceProductionContext context,
        string contractName,
        string methodName,
        Location? location,
        bool isManual)
    {
        var descriptor = new DiagnosticDescriptor(
            "PULSE006",
            "Protocol ID 0 is reserved",
            isManual
                ? "Method {0}.{1} explicitly uses protocol ID 0, which is reserved for non-RPC/control messages. Choose an ID from 0x0001 to 0xFFFF."
                : "The generated protocol ID for method {0}.{1} is 0, which is reserved for non-RPC/control messages. Add [Protocol(\"0xXXXX\")] with an ID from 0x0001 to 0xFFFF.",
            "PulseRPC.Server.SourceGenerator",
            DiagnosticSeverity.Error,
            true);

        context.ReportDiagnostic(Diagnostic.Create(descriptor, location ?? Location.None, contractName, methodName));
    }

    private static void ReportInvalidProtocolId(
        SourceProductionContext context,
        string contractName,
        string methodName,
        Location? location)
    {
        var descriptor = new DiagnosticDescriptor(
            "PULSE007",
            "Invalid Protocol ID",
            "Method {0}.{1} has an invalid [Protocol] value. Use a hexadecimal ushort such as [Protocol(\"0x1234\")].",
            "PulseRPC.Server.SourceGenerator",
            DiagnosticSeverity.Error,
            true);

        context.ReportDiagnostic(Diagnostic.Create(descriptor, location ?? Location.None, contractName, methodName));
    }

    private static void ReportWireSignatureCollision(
        SourceProductionContext context,
        string contractName,
        MethodModel second)
    {
        var descriptor = new DiagnosticDescriptor(
            "PULSE009",
            "RPC methods have the same wire signature",
            "Methods {0}.{1} differ only by local-only CancellationToken parameters and therefore have the same wire signature. Remove one overload or rename it.",
            "PulseRPC.Server.SourceGenerator",
            DiagnosticSeverity.Error,
            true);

        context.ReportDiagnostic(Diagnostic.Create(
            descriptor,
            second.Location ?? Location.None,
            contractName,
            second.MethodName));
    }

    private static void ReportMultipleCancellationTokens(
        SourceProductionContext context,
        string contractName,
        string methodName,
        Location? location)
    {
        var descriptor = new DiagnosticDescriptor(
            "PULSE010",
            "Multiple CancellationToken parameters are not supported",
            "Method {0}.{1} declares more than one CancellationToken. RPC methods may declare at most one local cancellation token.",
            "PulseRPC.Server.SourceGenerator",
            DiagnosticSeverity.Error,
            true);

        context.ReportDiagnostic(Diagnostic.Create(descriptor, location ?? Location.None, contractName, methodName));
    }

    /// <summary>
    /// 从指定协议号开始向后查找一个尚未被占用的协议号，作为 CodeFixProvider 的插入建议。
    /// </summary>
    private static ImmutableDictionary<string, string?> BuildSuggestedProtocolIdProperties(ushort protocolId, ICollection<ushort> usedProtocolIds)
    {
        for (var offset = 1; offset <= ushort.MaxValue; offset++)
        {
            var candidate = unchecked((ushort)(protocolId + offset));
            if (candidate != 0 && !usedProtocolIds.Contains(candidate))
            {
                return ImmutableDictionary<string, string?>.Empty.Add("SuggestedProtocolId", candidate.ToString("X4"));
            }
        }

        return ImmutableDictionary<string, string?>.Empty;
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

    private static void GenerateRouterProxy(SourceProductionContext context, ServiceModel serviceModel)
    {
        var sourceText = RouterProxyGenerator.GenerateSourceText(serviceModel);
        var namespacePrefix = string.IsNullOrWhiteSpace(serviceModel.Namespace)
            ? string.Empty
            : serviceModel.Namespace.Replace('.', '_') + "_";
        var fileName = $"{namespacePrefix}{serviceModel.InterfaceName.TrimStart('I')}.RouterProxy.g.cs";
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
    /// 生成服务元数据清单
    /// </summary>
    private static void GenerateServiceManifest(SourceProductionContext context, List<ServiceModel> serviceModels)
    {
        var sourceText = ServiceManifestGenerator.GenerateSourceText(serviceModels);
        context.AddSource("ServiceManifest.g.cs", sourceText);
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
        // PulseRPC runtime packages run their own generators. Rescanning them from every host would
        // duplicate built-in routing/manifest module initializers and makes consumer-only output impure.
        if (assemblyName is
            "PulseRPC.Abstractions" or
            "PulseRPC.Shared" or
            "PulseRPC.Client" or
            "PulseRPC.Server" or
            "PulseRPC.Backplane.Redis" ||
            assemblyName.StartsWith("PulseRPC.Infrastructure", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

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

    private static List<ServiceModel> ScanAssemblyForRouterConsumers(IAssemblySymbol assembly)
    {
        var routerModels = new List<ServiceModel>();

        foreach (var type in GetAllTypesInAssembly(assembly))
        {
            if (type.TypeKind != TypeKind.Interface || !IsRouterConsumerInterface(type))
            {
                continue;
            }

            var model = CreateServiceModelFromSymbol(type);
            if (model != null)
            {
                routerModels.Add(model);
            }
        }

        return routerModels;
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
    /// 检查接口是否为服务接口（继承 IPulseHub，且非客户端实现的推送接收器）
    /// </summary>
    private static bool IsServiceInterface(INamedTypeSymbol typeSymbol)
    {
        // 只处理继承 IPulseHub 的接口
        if (!typeSymbol.AllInterfaces.Any(i => i.Name == "IPulseHub"))
            return false;

        // 排除客户端实现的推送接收器（[Channel("CLIENT")]，按结构判定），它们有独立的处理逻辑
        if (HasClientChannel(typeSymbol))
            return false;

        // §5.2-C 显式覆盖：[PulseHub(Provide=false)] 表示本编译侧（服务端）不生成被调方骨架
        if (PulseHubOverrideHelper.TryGetOverride(typeSymbol, out var provide, out _) && !provide)
            return false;

        return true;
    }

    /// <summary>
    /// 服务端只在显式声明 <c>[PulseHub(Consume=true)]</c> 时生成出站 RouterProxy；
    /// 未标注的普通 Hub 仍按默认推断只生成入站骨架。
    /// </summary>
    private static bool IsRouterConsumerInterface(INamedTypeSymbol typeSymbol)
    {
        if (!typeSymbol.AllInterfaces.Any(i => i.Name == "IPulseHub") || HasClientChannel(typeSymbol))
        {
            return false;
        }

        return PulseHubOverrideHelper.TryGetOverride(typeSymbol, out _, out var consume) && consume;
    }

    /// <summary>
    /// 检查接口是否为（客户端实现的）推送接收器接口。
    /// </summary>
    /// <remarks>
    /// 统一标记模型：所有远程契约都继承 <c>IPulseHub</c>；方向由 <c>[Channel("CLIENT")]</c> 声明——
    /// 标注为 CLIENT 表示"客户端实现、服务端推送"，服务端为其生成 Fan-out 调用方代理（HubContext/HubClients）。
    /// </remarks>
    private static bool IsReceiverInterface(INamedTypeSymbol typeSymbol)
    {
        // 必须继承 IPulseHub
        if (!typeSymbol.AllInterfaces.Any(i => i.Name == "IPulseHub"))
            return false;

        // 由 [Channel("CLIENT")] 判定为客户端实现的推送接收器
        if (!HasClientChannel(typeSymbol))
            return false;

        // §5.2-C 显式覆盖：[PulseHub(Consume=false)] 表示本编译侧（服务端）不生成 Fan-out 调用方代理
        if (PulseHubOverrideHelper.TryGetOverride(typeSymbol, out _, out var consume) && !consume)
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

            // 含直接成员 + 继承接口成员，与客户端生成器方法收集范围对齐（见 §11.2 风险 #1）
            foreach (var member in ProtocolIdHelper.GetAllPublicMethods(typeSymbol))
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

                    // 尝试读取 [Protocol] 特性（与 Hub 方法一致，用于解决协议号冲突）
                    var protocolIdParseResult = ProtocolIdHelper.ParseManualProtocolId(methodSymbol, out var manualProtocolId);

                    var method = new ReceiverMethodModel
                    {
                        MethodName = methodSymbol.Name,
                        ReturnTypeName = returnType,
                        IsAsync = isAsync,
                        DeclaringInterfaceFullName = methodSymbol.ContainingType.ToDisplayString(),
                        // [P-4] Task<T>/ValueTask<T> => 反向 Ask 的响应类型；非泛型返回 null（保持单向 push）
                        ResponseTypeName = GetResponseTypeFullName(returnType),
                        Parameters = methodSymbol.Parameters.Select(p => new ReceiverParameterModel
                        {
                            Name = p.Name,
                            TypeName = p.Type.Name,
                            TypeFullName = p.Type.ToDisplayString(),
                            IsCancellationToken = ProtocolIdHelper.IsCancellationToken(p.Type)
                        }).ToList(),
                        ProtocolId = manualProtocolId,
                        HasExplicitProtocolId = protocolIdParseResult == ManualProtocolIdParseResult.Valid,
                        HasInvalidProtocolId = protocolIdParseResult == ManualProtocolIdParseResult.Invalid,
                        Location = methodSymbol.Locations.FirstOrDefault()
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
    /// <remarks>
    /// 独立于 Hub 方法的协议号空间；同样使用纯哈希函数、不做线性探测。支持 <c>[Protocol(0xXXXX)]</c>
    /// 手动指定协议号（与 Hub 方法一致，由 <c>CreateReceiverModelFromSymbol</c> 读取），冲突
    /// （无论手动号之间、自动号之间，还是手动号与自动号之间）一律报告 <c>PULSE004</c> 编译错误，
    /// 要求开发者手动指定不同的协议号来区分。
    /// </remarks>
    private static void AssignReceiverProtocolIds(List<ReceiverModel> receivers, SourceProductionContext context)
    {
        var usedIds = new Dictionary<ushort, (string receiver, string method, Location? location)>();
        var invalidMethods = new HashSet<ReceiverMethodModel>();

        foreach (var receiver in receivers)
        {
            foreach (var method in receiver.Methods)
            {
                if (method.Parameters.Count(parameter => parameter.IsCancellationToken) > 1)
                {
                    invalidMethods.Add(method);
                    ReportMultipleCancellationTokens(context, receiver.InterfaceName, method.MethodName, method.Location);
                }

                if (method.HasInvalidProtocolId)
                {
                    invalidMethods.Add(method);
                    ReportInvalidProtocolId(context, receiver.InterfaceName, method.MethodName, method.Location);
                }
            }
        }

        // 第一遍：收集所有手动指定的协议号
        foreach (var receiver in receivers)
        {
            foreach (var method in receiver.Methods)
            {
                if (invalidMethods.Contains(method))
                {
                    continue;
                }

                if (method.HasExplicitProtocolId)
                {
                    if (method.ProtocolId == 0)
                    {
                        ReportReservedProtocolId(context, receiver.InterfaceName, method.MethodName, method.Location, isManual: true);
                        continue;
                    }

                    if (usedIds.TryGetValue(method.ProtocolId, out var existing))
                    {
                        ReportReceiverProtocolIdConflict(context, method.ProtocolId, existing, receiver.InterfaceName, method.MethodName, method.Location, usedIds.Keys);
                    }
                    else
                    {
                        usedIds[method.ProtocolId] = (receiver.InterfaceName, method.MethodName, method.Location);
                    }
                }
            }
        }

        // 第二遍：为没有手动指定的方法生成协议号（纯哈希函数，不做线性探测）
        foreach (var receiver in receivers)
        {
            foreach (var method in receiver.Methods)
            {
                if (invalidMethods.Contains(method) || method.HasExplicitProtocolId)
                    continue;

                // 构造方法签名 - 排除 CancellationToken 参数（与客户端保持一致）
                var parameterTypes = ProtocolIdHelper.FilterCancellationToken(
                    method.Parameters.Select(p => p.TypeFullName));

                // 使用方法实际声明所在接口的全名（继承方法为基接口），与客户端保持一致（见 §11.2 风险 #1）
                var signature = ProtocolIdHelper.BuildMethodSignature(
                    method.DeclaringInterfaceFullName ?? receiver.InterfaceFullName,
                    method.MethodName,
                    parameterTypes);

                var protocolId = ProtocolIdHelper.GenerateProtocolId(signature);
                method.ProtocolId = protocolId;

                if (protocolId == 0)
                {
                    ReportReservedProtocolId(context, receiver.InterfaceName, method.MethodName, method.Location, isManual: false);
                    continue;
                }

                if (usedIds.TryGetValue(protocolId, out var existing))
                {
                    ReportReceiverProtocolIdConflict(context, protocolId, existing, receiver.InterfaceName, method.MethodName, method.Location, usedIds.Keys);
                }
                else
                {
                    usedIds[protocolId] = (receiver.InterfaceName, method.MethodName, method.Location);
                }
            }
        }
    }

    /// <summary>
    /// 报告接收器协议号冲突（PULSE004）
    /// </summary>
    /// <remarks>
    /// 诊断定位在冲突方法的声明处，并在 <see cref="Diagnostic.Properties"/> 中附带建议协议号
    /// （键 <c>SuggestedProtocolId</c>），供 <c>ProtocolIdConflictCodeFixProvider</c> 使用。
    /// </remarks>
    private static void ReportReceiverProtocolIdConflict(
        SourceProductionContext context,
        ushort protocolId,
        (string receiver, string method, Location? location) existing,
        string conflictingReceiver,
        string conflictingMethod,
        Location? conflictingLocation,
        ICollection<ushort> usedProtocolIds)
    {
        var descriptor = new DiagnosticDescriptor(
            "PULSE004",
            "Receiver protocol ID conflict detected",
            $"Protocol ID 0x{protocolId:X4} is already used by {existing.receiver}.{existing.method}. " +
            $"Method {conflictingReceiver}.{conflictingMethod} cannot use the same protocol ID. " +
            $"Please manually specify a different protocol ID using [Protocol(0xXXXX)] attribute.",
            "PulseRPC.Server.SourceGenerator",
            DiagnosticSeverity.Error,
            true);

        var properties = BuildSuggestedProtocolIdProperties(protocolId, usedProtocolIds);

        context.ReportDiagnostic(Diagnostic.Create(
            descriptor,
            conflictingLocation ?? Location.None,
            existing.location != null ? new[] { existing.location } : null,
            properties));
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

            // P-6：facet（接口）级客户端可见性默认值；未标注 [ClientFacing] 时默认不可见（白名单语义）。
            var facetClientFacing = ClientFacingHelper.GetClientFacing(typeSymbol) ?? false;

            // 含直接成员 + 继承接口成员，与客户端生成器方法收集范围对齐（见 §11.2 风险 #1）
            foreach (var member in ProtocolIdHelper.GetAllPublicMethods(typeSymbol))
            {
                if (member is IMethodSymbol methodSymbol && methodSymbol.DeclaredAccessibility == Accessibility.Public)
                {
                    var returnType = methodSymbol.ReturnType.ToDisplayString();
                    var isAsync = IsAsyncMethod(methodSymbol);
                    var responseTypeFullName = GetResponseTypeFullName(methodSymbol.ReturnType);

                    // 尝试读取 [Protocol] 特性
                    var protocolIdParseResult = ProtocolIdHelper.ParseManualProtocolId(methodSymbol, out var manualProtocolId);

                    var methodAuthorization = AuthorizationHelper.GetEffectiveAuthorization(typeSymbol, methodSymbol);

                    var isReentrant = methodSymbol.GetAttributes()
                        .Any(attr => attr.AttributeClass?.Name is "ReentrantAttribute" or "Reentrant");

                    // P-6：方法级 [ClientFacing] 覆盖 facet 级默认值
                    var isClientFacing = ClientFacingHelper.GetClientFacing(methodSymbol) ?? facetClientFacing;

                    var method = new MethodModel
                    {
                        MethodName = methodSymbol.Name,
                        MethodIdentity = MethodIdentity.CreateLookupKey(methodSymbol),
                        GenericArity = methodSymbol.Arity,
                        IsAsync = isAsync,
                        ReturnTypeName = returnType,
                        ReturnTypeFullName = returnType,
                        IsGenericTask = isAsync && (returnType.Contains("Task<") || returnType.Contains("ValueTask<")),
                        Parameters = methodSymbol.Parameters.Select(p => new ParameterModel
                        {
                            Name = p.Name,
                            TypeName = p.Type.Name,
                            TypeFullName = p.Type.ToDisplayString(),
                            IsMemoryPackable = IsMemoryPackable(p.Type),
                            IsCancellationToken = ProtocolIdHelper.IsCancellationToken(p.Type),
                            RefKind = p.RefKind
                        }).ToList(),
                        DeclaringInterfaceFullName = methodSymbol.ContainingType.ToDisplayString(),
                        ResponseTypeFullName = responseTypeFullName,
                        IsResponseMemoryPackable = responseTypeFullName is null || IsMemoryPackSerializableResponse(methodSymbol.ReturnType),
                        ProtocolId = manualProtocolId,
                        HasExplicitProtocolId = protocolIdParseResult == ManualProtocolIdParseResult.Valid,
                        HasInvalidProtocolId = protocolIdParseResult == ManualProtocolIdParseResult.Invalid,
                        Authorization = methodAuthorization,
                        IsReentrant = isReentrant,
                        IsClientFacing = isClientFacing,
                        Location = methodSymbol.Locations.FirstOrDefault()
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
            return returnType.Substring(5, returnType.Length - 6).Trim();
        }

        if (returnType.StartsWith("ValueTask<") && returnType.EndsWith(">"))
        {
            return returnType.Substring(10, returnType.Length - 11).Trim();
        }

        if (returnType.StartsWith("System.Threading.Tasks.Task<") && returnType.EndsWith(">"))
        {
            return returnType.Substring(28, returnType.Length - 29).Trim();
        }

        if (returnType.StartsWith("System.Threading.Tasks.ValueTask<") && returnType.EndsWith(">"))
        {
            return returnType.Substring(33, returnType.Length - 34).Trim();
        }

        throw new InvalidOperationException($"{returnType} is not a valid response type");
    }

    private static string? GetResponseTypeFullName(ITypeSymbol returnType)
    {
        if (returnType is not INamedTypeSymbol namedType ||
            !namedType.IsGenericType ||
            namedType.TypeArguments.Length == 0 ||
            namedType.ConstructedFrom.Name is not ("Task" or "ValueTask"))
        {
            return null;
        }

        return namedType.TypeArguments[0].ToDisplayString(
            SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
                SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions |
                SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier));
    }

    private static bool ValidateResponseTypes(SourceProductionContext context, List<ServiceModel> serviceModels)
    {
        var valid = true;
        var descriptor = new DiagnosticDescriptor(
            "PULSE004",
            "Response type is not MemoryPack serializable",
            "Method '{0}.{1}' returns '{2}', which is not MemoryPack serializable. Mark the custom response type with [MemoryPackable] or return a MemoryPack-supported primitive/collection type.",
            "PulseRPC.Server.SourceGenerator",
            DiagnosticSeverity.Error,
            true);

        foreach (var service in serviceModels)
        {
            foreach (var method in service.Methods)
            {
                if (!string.IsNullOrWhiteSpace(method.ResponseTypeFullName) && !method.IsResponseMemoryPackable)
                {
                    valid = false;
                    context.ReportDiagnostic(Diagnostic.Create(
                        descriptor,
                        method.Location ?? Location.None,
                        service.InterfaceFullName,
                        method.MethodName,
                        method.ResponseTypeFullName));
                }
            }
        }

        return valid;
    }

    private static bool IsMemoryPackSerializableResponse(ITypeSymbol returnType)
    {
        if (returnType is INamedTypeSymbol namedType && namedType.IsGenericType && namedType.TypeArguments.Length > 0)
        {
            return IsMemoryPackSerializable(namedType.TypeArguments[0]);
        }

        return IsMemoryPackSerializable(returnType);
    }

    private static bool IsMemoryPackSerializable(ITypeSymbol typeSymbol)
    {
        if (typeSymbol.TypeKind == TypeKind.Enum)
        {
            return true;
        }

        if (typeSymbol is IArrayTypeSymbol arrayType)
        {
            return IsMemoryPackSerializable(arrayType.ElementType);
        }

        if (typeSymbol is INamedTypeSymbol namedType)
        {
            if (namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
                namedType.TypeArguments.Length == 1)
            {
                return IsMemoryPackSerializable(namedType.TypeArguments[0]);
            }

            if (namedType.IsTupleType)
            {
                return namedType.TupleElements.All(e => IsMemoryPackSerializable(e.Type));
            }

            if (IsMemoryPackable(namedType))
            {
                return true;
            }

            var fullName = namedType.ToDisplayString();
            if (fullName is "System.Guid" or "System.DateTime" or "System.DateTimeOffset" or "System.TimeSpan")
            {
                return true;
            }

            if (fullName.StartsWith("System.Collections.Generic.", StringComparison.Ordinal) &&
                namedType.TypeArguments.All(IsMemoryPackSerializable))
            {
                return true;
            }
        }

        return typeSymbol.SpecialType
            is SpecialType.System_Boolean
            or SpecialType.System_Byte
            or SpecialType.System_SByte
            or SpecialType.System_Int16
            or SpecialType.System_UInt16
            or SpecialType.System_Int32
            or SpecialType.System_UInt32
            or SpecialType.System_Int64
            or SpecialType.System_UInt64
            or SpecialType.System_Single
            or SpecialType.System_Double
            or SpecialType.System_Decimal
            or SpecialType.System_Char
            or SpecialType.System_String;
    }

    private static bool IsMemoryPackable(ITypeSymbol typeSymbol)
    {
        return typeSymbol.GetAttributes()
            .Any(attr => attr.AttributeClass?.Name is "MemoryPackableAttribute" or "MemoryPackable");
    }

}
