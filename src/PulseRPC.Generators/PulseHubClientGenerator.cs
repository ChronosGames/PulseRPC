using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace PulseRPC.Generators
{
    [Generator]
    public class PulseHubClientGenerator : ISourceGenerator
    {
        public void Initialize(GeneratorInitializationContext context)
        {
            // 注册语法接收器
            context.RegisterForSyntaxNotifications(() => new HubInterfaceSyntaxReceiver());
        }

        public void Execute(GeneratorExecutionContext context)
        {
            // 获取语法接收器
            if (context.SyntaxReceiver is not HubInterfaceSyntaxReceiver receiver)
                return;

            // 获取IPulseHub<,>接口符号
            var compilation = context.Compilation;
            var pulseHubSymbol = compilation.GetTypeByMetadataName("PulseRPC.IPulseHub`2");

            if (pulseHubSymbol == null)
            {
                // 如果找不到IPulseHub接口，则无法继续
                context.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "PULSE002",
                        "PulseRPC Core types not found",
                        "Could not find PulseRPC.IPulseHub<,> interface. Make sure PulseRPC.Core is referenced.",
                        "PulseRPC",
                        DiagnosticSeverity.Error,
                        isEnabledByDefault: true),
                    Location.None));
                return;
            }

            // 处理每个候选接口
            foreach (var interfaceDeclaration in receiver.CandidateInterfaces)
            {
                var model = compilation.GetSemanticModel(interfaceDeclaration.SyntaxTree);
                var interfaceSymbol = model.GetDeclaredSymbol(interfaceDeclaration) as INamedTypeSymbol;

                if (interfaceSymbol == null)
                    continue;

                // 检查这个接口是否实现了IPulseHub<,>
                if (ImplementsPulseHub(interfaceSymbol, pulseHubSymbol, out var receiverType))
                {
                    // 生成Hub客户端代理代码
                    var generatedCode = GenerateHubClientProxyCode(interfaceSymbol, receiverType);
                    if (!string.IsNullOrEmpty(generatedCode))
                    {
                        // 添加生成的代码
                        context.AddSource($"{interfaceSymbol.Name}ClientImpl.g.cs", SourceText.From(generatedCode, Encoding.UTF8));
                    }
                }
            }
        }

        private bool ImplementsPulseHub(INamedTypeSymbol interfaceSymbol, INamedTypeSymbol pulseHubSymbol, out INamedTypeSymbol receiverType)
        {
            receiverType = null;

            // 检查接口是否实现了IPulseHub<,>
            var interfaces = interfaceSymbol.AllInterfaces.Concat(new[] { interfaceSymbol });

            foreach (var @interface in interfaces)
            {
                if (@interface.OriginalDefinition.Equals(pulseHubSymbol, SymbolEqualityComparer.Default))
                {
                    receiverType = @interface.TypeArguments[1] as INamedTypeSymbol;
                    return true;
                }

                foreach (var implementedInterface in @interface.Interfaces)
                {
                    if (implementedInterface.OriginalDefinition.Equals(pulseHubSymbol, SymbolEqualityComparer.Default))
                    {
                        receiverType = implementedInterface.TypeArguments[1] as INamedTypeSymbol;
                        return true;
                    }
                }
            }

            return false;
        }

        private string GenerateHubClientProxyCode(INamedTypeSymbol hubInterface, INamedTypeSymbol receiverInterface)
        {
            var namespaceName = hubInterface.ContainingNamespace.ToDisplayString();
            var hubInterfaceName = hubInterface.Name;
            var receiverInterfaceName = receiverInterface.Name;
            var receiverNamespace = receiverInterface.ContainingNamespace.ToDisplayString();

            var methodsCode = new StringBuilder();
            var eventHandlerCode = new StringBuilder();
            var usings = new HashSet<string>
            {
                "System",
                "System.Threading",
                "System.Threading.Tasks",
                "System.Collections.Generic",
                "PulseRPC",
                "PulseRPC.Protocol",
                "MemoryPack"
            };

            // 添加接口所在的命名空间
            usings.Add(namespaceName);
            usings.Add(receiverNamespace);

            // 处理Hub接口的每个方法
            foreach (var member in hubInterface.GetMembers())
            {
                if (member is IMethodSymbol methodSymbol && methodSymbol.MethodKind == MethodKind.Ordinary)
                {
                    // 跳过非公开方法
                    if (methodSymbol.DeclaredAccessibility != Accessibility.Public)
                        continue;

                    // 添加所有参数类型的命名空间
                    foreach (var parameter in methodSymbol.Parameters)
                    {
                        var paramTypeNamespace = parameter.Type.ContainingNamespace?.ToDisplayString();
                        if (!string.IsNullOrEmpty(paramTypeNamespace) && !usings.Contains(paramTypeNamespace))
                            usings.Add(paramTypeNamespace);
                    }

                    // 添加返回类型的命名空间
                    var returnType = methodSymbol.ReturnType is INamedTypeSymbol namedReturnType ?
                        namedReturnType.TypeArguments.FirstOrDefault() : null;

                    if (returnType != null)
                    {
                        var returnTypeNamespace = returnType.ContainingNamespace?.ToDisplayString();
                        if (!string.IsNullOrEmpty(returnTypeNamespace) && !usings.Contains(returnTypeNamespace))
                            usings.Add(returnTypeNamespace);
                    }

                    // 生成方法实现
                    methodsCode.AppendLine(GenerateHubMethodImplementation(methodSymbol));
                }
            }

            // 处理接收器接口的每个方法（为事件处理创建switch-case）
            foreach (var member in receiverInterface.GetMembers())
            {
                if (member is IMethodSymbol methodSymbol && methodSymbol.MethodKind == MethodKind.Ordinary)
                {
                    // 跳过非公开方法
                    if (methodSymbol.DeclaredAccessibility != Accessibility.Public)
                        continue;

                    // 添加所有参数类型的命名空间
                    foreach (var parameter in methodSymbol.Parameters)
                    {
                        var paramTypeNamespace = parameter.Type.ContainingNamespace?.ToDisplayString();
                        if (!string.IsNullOrEmpty(paramTypeNamespace) && !usings.Contains(paramTypeNamespace))
                            usings.Add(paramTypeNamespace);
                    }

                    // 生成事件处理case
                    eventHandlerCode.AppendLine(GenerateEventHandlerCase(methodSymbol));
                }
            }

            // 生成完整的类代码
            var proxyCode = $@"// <auto-generated/>
#nullable enable
{string.Join("\n", usings.Select(u => $"using {u};"))}

namespace PulseRPC.Client.Generated
{{
    /// <summary>
    /// 自动生成的 {hubInterfaceName} Hub客户端代理
    /// </summary>
    public sealed class {hubInterfaceName}ClientImpl : {namespaceName}.{hubInterfaceName}
    {{
        private readonly IPulseConnection _connection;
        private readonly {receiverNamespace}.{receiverInterfaceName} _receiver;
        private readonly string _hubId;
        private readonly bool _autoReconnect;

        public {hubInterfaceName}ClientImpl(
            IPulseConnection connection,
            {receiverNamespace}.{receiverInterfaceName} receiver,
            bool autoReconnect = true)
        {{
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _receiver = receiver ?? throw new ArgumentNullException(nameof(receiver));
            _hubId = Guid.NewGuid().ToString();
            _autoReconnect = autoReconnect;

            // 注册事件处理
            _connection.OnEventReceived += HandleEventAsync;

            // 连接到Hub
            _ = ConnectHubAsync();
        }}

        /// <summary>
        /// 处理从服务器收到的事件
        /// </summary>
        private async Task HandleEventAsync(PulseEvent eventData)
        {{
            // 仅处理针对此Hub的事件
            if (eventData.HubId != _hubId)
                return;

            try
            {{
                switch (eventData.MethodName)
                {{
{eventHandlerCode}
                    default:
                        System.Diagnostics.Debug.WriteLine($""收到未知Hub事件: {{eventData.MethodName}}"");
                        break;
                }}
            }}
            catch (Exception ex)
            {{
                System.Diagnostics.Debug.WriteLine($""处理Hub事件时出错: {{ex.Message}}"");
            }}
        }}

        /// <summary>
        /// 连接到Hub
        /// </summary>
        private async Task ConnectHubAsync()
        {{
            try
            {{
                // 创建Hub连接请求
                var request = new PulseRequest
                {{
                    RequestId = Guid.NewGuid(),
                    ServiceName = ""{hubInterfaceName}"",
                    MethodName = ""__connect"",
                    Parameters = MemoryPackSerializer.Serialize(new object[] {{ _hubId }})
                }};

                // 发送请求并等待响应
                await _connection.SendRequestAsync(request);
            }}
            catch (Exception ex)
            {{
                System.Diagnostics.Debug.WriteLine($""连接Hub时出错: {{ex.Message}}"");

                // 如果自动重连，则尝试重新连接
                if (_autoReconnect)
                {{
                    await Task.Delay(3000); // 等待3秒后重试
                    _ = ConnectHubAsync();
                }}
            }}
        }}

{methodsCode}
    }}
}}";

            return proxyCode;
        }

        private string GenerateHubMethodImplementation(IMethodSymbol methodSymbol)
        {
            // 方法名和异步后缀
            var methodName = methodSymbol.Name;
            var methodNameWithoutAsync = methodName.EndsWith("Async") ? methodName.Substring(0, methodName.Length - 5) : methodName;

            // 获取参数列表
            var parameters = string.Join(", ", methodSymbol.Parameters.Select(p =>
                $"{p.Type.ToDisplayString()} {p.Name}{(p.HasExplicitDefaultValue ? $" = {GetDefaultValueString(p)}" : "")}"
            ));

            // 获取参数调用列表
            var parameterCallList = string.Join(", ", methodSymbol.Parameters.Select(p => p.Name));

            // 获取返回类型
            var taskType = methodSymbol.ReturnType as INamedTypeSymbol;
            var hasReturnValue = taskType != null && taskType.TypeArguments.Length > 0;
            var returnType = hasReturnValue ? taskType?.TypeArguments[0].ToDisplayString() : null;

            // 生成方法体
            var methodBody = $@"        /// <inheritdoc />
        public async {methodSymbol.ReturnType.ToDisplayString()} {methodName}({parameters})
        {{
            try
            {{
                // 创建请求
                var request = new PulseRequest
                {{
                    RequestId = Guid.NewGuid(),
                    ServiceName = ""{methodSymbol.ContainingType.Name}"",
                    MethodName = ""{methodNameWithoutAsync}"",
                    HubId = _hubId,
                    Parameters = MemoryPackSerializer.Serialize(new object[] {{ {string.Join(", ", methodSymbol.Parameters.Select(p => p.Name))} }})
                }};

                // 发送请求并等待响应
                var response = await _connection.SendRequestAsync(request, cancellationToken);

                // 检查响应
                if (!response.IsSuccess)
                {{
                    throw new RpcException(response.ErrorMessage ?? ""RPC调用失败"");
                }}

                {(hasReturnValue ? $@"// 反序列化返回值
                if (response.ReturnValue != null && response.ReturnValue.Length > 0)
                {{
                    return MemoryPackSerializer.Deserialize<{returnType}>(response.ReturnValue);
                }}

                return default!;" : "// 无返回值")}
            }}
            catch (RpcException)
            {{
                throw;
            }}
            catch (Exception ex)
            {{
                throw new RpcException($""调用Hub方法{methodName}时出错: {{ex.Message}}"", ex);
            }}
        }}";

            return methodBody;
        }

        private string GenerateEventHandlerCase(IMethodSymbol methodSymbol)
        {
            var methodName = methodSymbol.Name;
            var parameters = methodSymbol.Parameters;
            var parametersCount = parameters.Length;

            // 根据参数数量生成反序列化代码
            string deserializeCode;
            if (parametersCount == 0)
            {
                deserializeCode = "// 无参数方法";
            }
            else if (parametersCount == 1)
            {
                // 单个参数
                var paramType = parameters[0].Type.ToDisplayString();
                deserializeCode = $"var param = MemoryPackSerializer.Deserialize<{paramType}>(eventData.Parameters);";
            }
            else
            {
                // 多个参数
                deserializeCode = $"var parameters = MemoryPackSerializer.Deserialize<object[]>(eventData.Parameters);";
            }

            // 生成方法调用代码
            string methodCallCode;
            if (parametersCount == 0)
            {
                methodCallCode = $"await _receiver.{methodName}();";
            }
            else if (parametersCount == 1)
            {
                methodCallCode = $"await _receiver.{methodName}(param);";
            }
            else
            {
                var parameterCasts = string.Join(", ", parameters.Select((p, i) =>
                    $"({p.Type.ToDisplayString()})parameters[{i}]"));
                methodCallCode = $"await _receiver.{methodName}({parameterCasts});";
            }

            return $@"                    case ""{methodName}"":
                        {{
                            {deserializeCode}
                            {methodCallCode}
                            break;
                        }}";
        }

        private string GetDefaultValueString(IParameterSymbol parameter)
        {
            if (!parameter.HasExplicitDefaultValue)
                return string.Empty;

            if (parameter.ExplicitDefaultValue == null)
                return "null";

            if (parameter.Type.TypeKind == TypeKind.Enum)
                return $"({parameter.Type.ToDisplayString()}){Convert.ToInt32(parameter.ExplicitDefaultValue)}";

            if (parameter.ExplicitDefaultValue is string stringValue)
                return $"\"{stringValue}\"";

            return parameter.ExplicitDefaultValue.ToString();
        }

        /// <summary>
        /// 语法接收器，用于收集潜在的Hub接口
        /// </summary>
        private class HubInterfaceSyntaxReceiver : ISyntaxReceiver
        {
            public List<InterfaceDeclarationSyntax> CandidateInterfaces { get; } = new List<InterfaceDeclarationSyntax>();

            public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
            {
                // 查找接口声明
                if (syntaxNode is InterfaceDeclarationSyntax interfaceDeclaration)
                {
                    // 检查是否继承了IPulseHub
                    var baseList = interfaceDeclaration.BaseList;
                    if (baseList != null)
                    {
                        foreach (var baseType in baseList.Types)
                        {
                            if (baseType.Type is GenericNameSyntax genericName &&
                                genericName.Identifier.Text == "IPulseHub")
                            {
                                CandidateInterfaces.Add(interfaceDeclaration);
                                break;
                            }
                        }
                    }
                }
            }
        }
    }
}
