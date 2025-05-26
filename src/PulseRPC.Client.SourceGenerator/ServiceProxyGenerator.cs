using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Generic;

namespace PulseRPC.Generator;

[Generator]
public class ServiceProxyGenerator : IIncrementalGenerator
{
    private const string PulseClientGenerationAttributeName = "PulseClientGenerationAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 获取配置选项
        var configProvider = context.AnalyzerConfigOptionsProvider;

        // 方法一：查找带有 ServiceContract 特性的接口
        var serviceInterfaces =
            context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (s, _) => IsInterfaceWithAttribute(s, "ServiceContract"),
                    transform: static (ctx, _) => GetServiceTypeFromInterface(ctx))
                .Where(static m => m.Type is not null);

        // 方法二：查找带有 PulseClientGeneration 特性的类
        var classDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => s is ClassDeclarationSyntax { AttributeLists.Count: > 0 },
                transform: (ctx, _) => GetServiceTypesFromClass(ctx))
            .Where(m => m.Length > 0);

        // 方法三：查找带有 EventContract 特性的接口
        var eventInterfaces =
            context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (s, _) => IsInterfaceWithAttribute(s, "EventContract"),
                    transform: static (ctx, _) => GetServiceTypeFromInterface(ctx))
                .Where(static m => m.Type is not null);

        // 合并所有类型提供者
        var allTypesProvider = serviceInterfaces.Collect()
            .Combine(classDeclarations.Collect())
            .Combine(eventInterfaces.Collect())
            .Select((data, _) =>
            {
                var serviceInterfaces = data.Left.Left;
                var classDeclarations = data.Left.Right;
                var eventInterfaces = data.Right;

                // 全局去重集合，使用类型的完整名称
                var uniqueServiceTypes = new Dictionary<string, ServiceTypeInfo>();
                var uniqueEventTypes = new Dictionary<string, ServiceTypeInfo>();

                // 从 ServiceContract 接口收集服务类型
                foreach (var serviceInterface in serviceInterfaces)
                {
                    if (serviceInterface.Type != null)
                    {
                        var key = serviceInterface.Type.ToDisplayString();
                        uniqueServiceTypes[key] = serviceInterface;
                    }
                }

                // 从 EventContract 接口收集事件类型
                foreach (var eventInterface in eventInterfaces)
                {
                    if (eventInterface.Type != null)
                    {
                        var key = eventInterface.Type.ToDisplayString();
                        uniqueEventTypes[key] = eventInterface;
                    }
                }

                // 从 PulseClientGeneration 特性收集类型
                foreach (var classDeclaration in classDeclarations)
                {
                    foreach (var serviceType in classDeclaration)
                    {
                        if (serviceType.Type != null)
                        {
                            var key = serviceType.Type.ToDisplayString();
                            var typeName = serviceType.Type.Name ?? string.Empty;

                            if (typeName.EndsWith("Events"))
                            {
                                // 事件接口
                                uniqueEventTypes[key] = serviceType;
                            }
                            else
                            {
                                // 服务接口
                                uniqueServiceTypes[key] = serviceType;
                            }
                        }
                    }
                }

                return new
                {
                    ServiceTypes = uniqueServiceTypes.Values.ToImmutableArray(),
                    EventTypes = uniqueEventTypes.Values.ToImmutableArray()
                };
            });

        // 统一的源代码输出注册 - 处理所有类型
        context.RegisterSourceOutput(allTypesProvider.Combine(configProvider), (spc, combined) =>
        {
            var types = combined.Left;
            var config = combined.Right;

            // 获取Unity兼容性选项
            var unityOptions = UnityCompatibility.GetCodeGenOptions(config);

            // 完全禁用文件写入，让Unity使用标准Source Generator输出
            var writeFilesToDisk = false;
            var outputFolder = unityOptions.OutputDirectory;

            // 全局的hintName去重集合
            var generatedHintNames = new HashSet<string>();

            // 生成服务代理
            foreach (var serviceTypeInfo in types.ServiceTypes)
            {
                if (serviceTypeInfo.Type is INamedTypeSymbol namedType)
                {
                    var proxyCode = GenerateServiceProxy(namedType, unityOptions);
                    var fileName = $"{namedType.Name}Proxy.g.cs";

                    // 使用唯一的hintName避免冲突
                    var baseHintName = $"PulseRPC.ServiceProxy.{namedType.ContainingNamespace?.ToDisplayString()}.{namedType.Name}Proxy.g.cs";
                    var hintName = baseHintName;
                    var counter = 1;

                    // 如果hintName已存在，添加计数器
                    while (generatedHintNames.Contains(hintName))
                    {
                        hintName = $"{baseHintName}.{counter}";
                        counter++;
                    }
                    generatedHintNames.Add(hintName);

                    // 添加到编译
                    spc.AddSource(hintName, SourceText.From(proxyCode, Encoding.UTF8));

                    // 如果需要写入磁盘，则尝试写入文件
                    if (writeFilesToDisk)
                    {
                        TryWriteFileToDisk(spc, proxyCode, fileName, outputFolder, config, unityOptions);
                    }
                }
            }

            // 生成事件处理器
            foreach (var eventTypeInfo in types.EventTypes)
            {
                if (eventTypeInfo.Type is INamedTypeSymbol namedType)
                {
                    var handlerCode = GenerateEventHandler(namedType);
                    var baseHandlerHintName = $"PulseRPC.EventHandler.{namedType.ContainingNamespace?.ToDisplayString()}.{namedType.Name}Handler.g.cs";
                    var handlerHintName = baseHandlerHintName;
                    var counter = 1;

                    while (generatedHintNames.Contains(handlerHintName))
                    {
                        handlerHintName = $"{baseHandlerHintName}.{counter}";
                        counter++;
                    }
                    generatedHintNames.Add(handlerHintName);

                    spc.AddSource(handlerHintName, SourceText.From(handlerCode, Encoding.UTF8));

                    if (writeFilesToDisk)
                    {
                        TryWriteFileToDisk(spc, handlerCode, $"{namedType.Name}Handler.g.cs", outputFolder, config, unityOptions);
                    }

                    // 生成事件处理器接口
                    var handlerInterfaceCode = GenerateEventHandlerInterface(namedType);
                    var baseInterfaceHintName = $"PulseRPC.EventHandlerInterface.{namedType.ContainingNamespace?.ToDisplayString()}.I{namedType.Name}Handler.g.cs";
                    var interfaceHintName = baseInterfaceHintName;
                    counter = 1;

                    while (generatedHintNames.Contains(interfaceHintName))
                    {
                        interfaceHintName = $"{baseInterfaceHintName}.{counter}";
                        counter++;
                    }
                    generatedHintNames.Add(interfaceHintName);

                    spc.AddSource(interfaceHintName, SourceText.From(handlerInterfaceCode, Encoding.UTF8));

                    if (writeFilesToDisk)
                    {
                        TryWriteFileToDisk(spc, handlerInterfaceCode, $"I{namedType.Name}Handler.g.cs", outputFolder, config, unityOptions);
                    }
                }
            }

            // 生成服务扩展方法（如果有服务类型）
            if (types.ServiceTypes.Length > 0)
            {
                var serviceNamedTypes = types.ServiceTypes.Select(t => t.Type).OfType<INamedTypeSymbol>().ToImmutableArray();
                var extensionsCode = GenerateChannelManagerExtensions(serviceNamedTypes, ImmutableArray<INamedTypeSymbol>.Empty);
                var hintName = "PulseRPC.ServiceChannelManagerExtensions.g.cs";

                // 确保扩展方法的hintName也是唯一的
                var counter = 1;
                while (generatedHintNames.Contains(hintName))
                {
                    hintName = $"PulseRPC.ServiceChannelManagerExtensions.g.cs.{counter}";
                    counter++;
                }
                generatedHintNames.Add(hintName);

                spc.AddSource(hintName, SourceText.From(extensionsCode, Encoding.UTF8));

                if (writeFilesToDisk)
                {
                    TryWriteFileToDisk(spc, extensionsCode, "ServiceChannelManagerExtensions.g.cs", outputFolder, config, unityOptions);
                }
            }

            // 生成事件扩展方法（如果有事件类型）
            if (types.EventTypes.Length > 0)
            {
                var eventNamedTypes = types.EventTypes.Select(t => t.Type).OfType<INamedTypeSymbol>().ToImmutableArray();
                var eventExtensionsCode = GenerateChannelManagerExtensions(
                    ImmutableArray<INamedTypeSymbol>.Empty,
                    eventNamedTypes);
                var baseHintName = "PulseRPC.EventChannelManagerExtensions.g.cs";
                var hintName = baseHintName;
                var counter = 1;

                while (generatedHintNames.Contains(hintName))
                {
                    hintName = $"{baseHintName}.{counter}";
                    counter++;
                }
                generatedHintNames.Add(hintName);

                spc.AddSource(hintName, SourceText.From(eventExtensionsCode, Encoding.UTF8));

                if (writeFilesToDisk)
                {
                    TryWriteFileToDisk(spc, eventExtensionsCode, "EventChannelManagerExtensions.g.cs", outputFolder, config, unityOptions);
                }
            }
        });
    }

    private static ServiceTypeInfo GetServiceTypeFromInterface(GeneratorSyntaxContext context)
    {
        var interfaceDeclaration = (InterfaceDeclarationSyntax)context.Node;
        var semanticModel = context.SemanticModel;

        // 获取接口的语义模型
        var interfaceSymbol = semanticModel.GetDeclaredSymbol(interfaceDeclaration);
        if (interfaceSymbol == null)
            return new ServiceTypeInfo(null);

        // 检查是否实现了 INetworkService 接口
        if (!IsNetworkService(interfaceSymbol))
            return new ServiceTypeInfo(null);

        return new ServiceTypeInfo(interfaceSymbol);
    }

    private static ServiceTypeInfo[] GetServiceTypesFromClass(GeneratorSyntaxContext context)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
        var semanticModel = context.SemanticModel;

        // 获取类的语义模型
        var classSymbol = semanticModel.GetDeclaredSymbol(classDeclaration);
        if (classSymbol == null)
            return Array.Empty<ServiceTypeInfo>();

        var result = new List<ServiceTypeInfo>();

        // 查找 PulseClientGeneration 特性
        foreach (var attribute in classSymbol.GetAttributes())
        {
            if (attribute.AttributeClass?.Name != "PulseClientGenerationAttribute")
                continue;

            // 获取特性的参数
            if (attribute.ConstructorArguments.Length != 1)
                continue;

            var serviceType = attribute.ConstructorArguments[0].Value as INamedTypeSymbol;
            if (serviceType == null)
                continue;

            result.Add(new ServiceTypeInfo(serviceType));
        }

        return result.ToArray();
    }

    private static bool IsInterfaceWithAttribute(SyntaxNode node, string attributeName)
    {
        // 仅检查接口声明
        if (node is not InterfaceDeclarationSyntax interfaceDecl)
        {
            return false;
        }

        // 检查是否有特定特性
        return (from attributeList in interfaceDecl.AttributeLists from attribute in attributeList.Attributes select attribute.Name.ToString()).Any(name => name == attributeName || name == $"{attributeName}Attribute");
    }

    private static bool IsNetworkService(INamedTypeSymbol typeSymbol)
    {
        // 检查是否实现了 INetworkService 接口
        return typeSymbol.AllInterfaces.Any(i => i.Name == "INetworkService");
    }

    private static void ExecuteEventHandlerGeneration(
        Compilation compilation,
        ImmutableArray<InterfaceDeclarationSyntax> interfaces,
        SourceProductionContext context)
    {
        if (interfaces.IsDefaultOrEmpty)
        {
            return;
        }

        // 处理每个事件接口
        foreach (var interfaceDecl in interfaces)
        {
            // 获取语义模型
            var semanticModel = compilation.GetSemanticModel(interfaceDecl.SyntaxTree);

            // 获取接口符号
            if (semanticModel.GetDeclaredSymbol(interfaceDecl) is not INamedTypeSymbol interfaceSymbol)
            {
                continue;
            }

            // 生成事件处理器代码
            var handlerCode = GenerateEventHandler(interfaceSymbol);

            // 添加生成的源代码
            context.AddSource($"{interfaceSymbol.Name}Handler.g.cs", SourceText.From(handlerCode, Encoding.UTF8));
        }
    }

    private static void ExecuteExtensionsGeneration(
        Compilation compilation,
        ImmutableArray<INamedTypeSymbol> serviceTypes,
        ImmutableArray<INamedTypeSymbol> eventTypes,
        SourceProductionContext context)
    {
        // 生成通道管理器扩展代码
        var extensionsCode = GenerateChannelManagerExtensions(serviceTypes, eventTypes);

        // 添加生成的源代码
        context.AddSource("ChannelManagerExtensions.g.cs", SourceText.From(extensionsCode, Encoding.UTF8));
    }

    private static string GenerateServiceProxy(INamedTypeSymbol interfaceSymbol, UnityCodeGenOptions unityOptions)
    {
        var interfaceName = interfaceSymbol.Name;
        var namespaceName = interfaceSymbol.ContainingNamespace.ToDisplayString();
        var className = interfaceName.StartsWith("I") ? interfaceName.Substring(1) + "Proxy" : interfaceName + "Proxy";

        var sb = new StringBuilder();

        // 生成文件头
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using PulseRPC.Transport;");
        sb.AppendLine("using PulseRPC.Messaging;");
        sb.AppendLine();

        // 生成命名空间
        sb.AppendLine($"namespace {namespaceName}");
        sb.AppendLine("{");

        // 生成代理类
        sb.AppendLine($"    /// <summary>");
        sb.AppendLine($"    /// {interfaceName} 服务代理实现");
        sb.AppendLine($"    /// </summary>");
        sb.AppendLine($"    public partial class {className} : {interfaceName}");
        sb.AppendLine("    {");
        sb.AppendLine("        private readonly PulseRPC.Transport.IChannelManager _channelManager;");
        sb.AppendLine();
        sb.AppendLine($"        /// <summary>");
        sb.AppendLine($"        /// 初始化 {interfaceName} 服务代理");
        sb.AppendLine($"        /// </summary>");
        sb.AppendLine($"        public {className}(PulseRPC.Transport.IChannelManager channelManager)");
        sb.AppendLine("        {");
        sb.AppendLine("            _channelManager = channelManager ?? throw new ArgumentNullException(nameof(channelManager));");
        sb.AppendLine("        }");
        sb.AppendLine();

        // 生成方法实现
        foreach (var member in interfaceSymbol.GetMembers())
        {
            if (member is not IMethodSymbol methodSymbol)
                continue;

            // 跳过继承的方法
            if (!SymbolEqualityComparer.Default.Equals(methodSymbol.ContainingType, interfaceSymbol))
                continue;

            GenerateMethodImplementation(sb, methodSymbol);
        }

        // 结束类和命名空间
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return UnityCompatibility.GenerateUnityCompatibleCode(sb.ToString(), unityOptions);
    }

    private static void GenerateMethodImplementation(StringBuilder sb, IMethodSymbol methodSymbol)
    {
        var methodName = methodSymbol.Name;
        var returnType = methodSymbol.ReturnType.ToDisplayString();
        var hasReturnValue = !methodSymbol.ReturnsVoid && returnType != "System.Threading.Tasks.Task";

        // 生成方法签名
        sb.AppendLine($"        /// <summary>");
        sb.AppendLine($"        /// {methodName} 方法实现");
        sb.AppendLine($"        /// </summary>");
        sb.Append($"        public {returnType} {methodName}(");

        // 生成参数列表
        var parameters = methodSymbol.Parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            var param = parameters[i];
            if (i > 0) sb.Append(", ");
            sb.Append($"{param.Type.ToDisplayString()} {param.Name}");
        }

        sb.AppendLine(")");
        sb.AppendLine("        {");

        // 生成方法体
        if (hasReturnValue)
        {
            // 有返回值的方法
            sb.AppendLine("            var channel = _channelManager.GetDefaultChannel();");
            if (parameters.Length > 0)
            {
                sb.AppendLine($"            return channel.CallAsync<{parameters[0].Type.ToDisplayString()}, {GetGenericReturnType(returnType)}>(\"{methodName}\", {parameters[0].Name});");
            }
            else
            {
                sb.AppendLine($"            return channel.CallAsync<{GetGenericReturnType(returnType)}>(\"{methodName}\");");
            }
        }
        else if (returnType == "System.Threading.Tasks.Task")
        {
            // 异步无返回值方法
            sb.AppendLine("            var channel = _channelManager.GetDefaultChannel();");
            if (parameters.Length > 0)
            {
                sb.AppendLine($"            return channel.SendAsync(\"{methodName}\", {parameters[0].Name});");
            }
            else
            {
                sb.AppendLine($"            return channel.SendAsync(\"{methodName}\");");
            }
        }
        else
        {
            // 同步方法
            sb.AppendLine("            throw new NotImplementedException(\"同步方法暂不支持\");");
        }

        sb.AppendLine("        }");
        sb.AppendLine();
    }

    private static string GetGenericReturnType(string returnType)
    {
        // 提取 Task<T> 中的 T
        if (returnType.StartsWith("System.Threading.Tasks.Task<") && returnType.EndsWith(">"))
        {
            return returnType.Substring("System.Threading.Tasks.Task<".Length, returnType.Length - "System.Threading.Tasks.Task<".Length - 1);
        }
        return "object";
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
        sb.AppendLine("        private readonly IMessageChannel _channel;");
        sb.AppendLine($"        private readonly Dictionary<{interfaceName}, List<ISubscriptionToken>> _subscriptions = new Dictionary<{interfaceName}, List<ISubscriptionToken>>();");
        sb.AppendLine();
        sb.AppendLine($"        /// <summary>");
        sb.AppendLine($"        /// 初始化 {interfaceName} 事件处理器");
        sb.AppendLine($"        /// </summary>");
        sb.AppendLine($"        public {handlerClassName}(IMessageChannel channel)");
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

            // 检查是否有 Event 特性
            if (!HasAttribute(methodSymbol, "EventAttribute"))
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
            var namespaceName = interfaceSymbol.ContainingNamespace.ToDisplayString();

            // 去掉I前缀
            var serviceName = interfaceName.StartsWith("I") ? interfaceName.Substring(1) : interfaceName;

            sb.AppendLine($"        /// <summary>");
            sb.AppendLine($"        /// 获取 {interfaceName} 服务");
            sb.AppendLine($"        /// </summary>");
            sb.AppendLine($"        public static {namespaceName}.{interfaceName} Get{serviceName}(this PulseRPC.Transport.IChannelManager channelManager)");
            sb.AppendLine("        {");
            sb.AppendLine($"            if (channelManager == null)");
            sb.AppendLine($"                throw new ArgumentNullException(nameof(channelManager));");
            sb.AppendLine();
            sb.AppendLine($"            return new {namespaceName}.{interfaceName}Proxy(channelManager);");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        // 为每个事件接口生成扩展方法
        foreach (var interfaceSymbol in eventTypes)
        {
            if (interfaceSymbol == null) continue;

            var interfaceName = interfaceSymbol.Name;
            var namespaceName = interfaceSymbol.ContainingNamespace.ToDisplayString();

            // 去掉I前缀获取服务名
            var serviceName = interfaceName.StartsWith("I") ? interfaceName.Substring(1) : interfaceName;

            // 确保生成的接口名称不重复I前缀
            var handlerInterfaceName = "I" + (interfaceName.StartsWith("I")
                ? interfaceName.Substring(1) + "Handler"
                : interfaceName + "Handler");

            var handlerClassName = interfaceName.StartsWith("I")
                ? interfaceName.Substring(1) + "Handler"
                : interfaceName + "Handler";

            sb.AppendLine($"        /// <summary>");
            sb.AppendLine($"        /// 获取 {interfaceName} 事件处理器");
            sb.AppendLine($"        /// </summary>");
            sb.AppendLine($"        public static {namespaceName}.{handlerInterfaceName} Get{serviceName}Handler(this PulseRPC.Transport.IChannelManager channelManager)");
            sb.AppendLine("        {");
            sb.AppendLine($"            if (channelManager == null)");
            sb.AppendLine($"                throw new ArgumentNullException(nameof(channelManager));");
            sb.AppendLine();
            sb.AppendLine($"            var channel = channelManager.GetChannel(\"{GetChannelAttributeValue(interfaceSymbol) ?? "default"}\");");
            sb.AppendLine($"            return new {namespaceName}.{handlerClassName}(channel);");
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

    private static bool HasAttribute(ISymbol symbol, string attributeName)
    {
        var shortName = attributeName.EndsWith("Attribute", StringComparison.Ordinal)
            ? attributeName.Substring(0, attributeName.Length - "Attribute".Length)
            : attributeName;

        var longName = shortName + "Attribute";

        return symbol.GetAttributes().Select(attribute => attribute.AttributeClass?.Name).Any(name => name == shortName || name == longName);
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

    private record ServiceTypeInfo
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
    private static void TryWriteFileToDisk(SourceProductionContext context, string sourceText, string fileName, string outputFolder, AnalyzerConfigOptionsProvider configProvider, UnityCodeGenOptions unityOptions)
    {
        try
        {
            // Ensure output directory exists
            if (!Directory.Exists(outputFolder))
            {
                Directory.CreateDirectory(outputFolder);
            }

            var filePath = Path.Combine(outputFolder, fileName);

            // Write the source file
            File.WriteAllText(filePath, sourceText, Encoding.UTF8);

            // Create .meta file for Unity if needed
            if (unityOptions.GenerateUnityMetaFiles)
            {
                UnityCompatibility.CreateUnityMetaFile(filePath);
            }

        }
        catch (Exception ex)
        {
            // Create diagnostic for file write error
            context.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor(
                    "PRPC102",
                    "文件写入失败",
                    $"无法写入文件 {fileName}: {ex.Message}",
                    "PulseRPC",
                    DiagnosticSeverity.Warning,
                    true),
                Location.None));
        }
    }
}
