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
    public class PulseServiceClientGenerator : ISourceGenerator
    {
        public void Initialize(GeneratorInitializationContext context)
        {
            // 注册语法接收器
            context.RegisterForSyntaxNotifications(() => new ServiceInterfaceSyntaxReceiver());
        }

        public void Execute(GeneratorExecutionContext context)
        {
            // 获取语法接收器
            if (context.SyntaxReceiver is not ServiceInterfaceSyntaxReceiver receiver)
                return;

            // 获取IPulseService<T>接口符号
            var compilation = context.Compilation;
            var pulseServiceSymbol = compilation.GetTypeByMetadataName("PulseRPC.IPulseService`1");

            if (pulseServiceSymbol == null)
            {
                // 如果找不到IPulseService接口，则无法继续
                context.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "PULSE001",
                        "PulseRPC Core types not found",
                        "Could not find PulseRPC.IPulseService<T> interface. Make sure PulseRPC.Core is referenced.",
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

                // 检查这个接口是否实现了IPulseService<T>
                if (ImplementsPulseService(interfaceSymbol, pulseServiceSymbol))
                {
                    // 生成客户端代理代码
                    var generatedCode = GenerateClientProxyCode(interfaceSymbol);
                    if (!string.IsNullOrEmpty(generatedCode))
                    {
                        // 添加生成的代码
                        context.AddSource($"{interfaceSymbol.Name}ClientImpl.g.cs", SourceText.From(generatedCode, Encoding.UTF8));
                    }
                }
            }
        }

        private bool ImplementsPulseService(INamedTypeSymbol interfaceSymbol, INamedTypeSymbol pulseServiceSymbol)
        {
            // 检查接口是否实现了IPulseService<T>
            var interfaces = interfaceSymbol.AllInterfaces.Concat(new[] { interfaceSymbol });

            foreach (var @interface in interfaces)
            {
                if (@interface.OriginalDefinition.Equals(pulseServiceSymbol, SymbolEqualityComparer.Default))
                    return true;

                foreach (var implementedInterface in @interface.Interfaces)
                {
                    if (implementedInterface.OriginalDefinition.Equals(pulseServiceSymbol, SymbolEqualityComparer.Default))
                        return true;
                }
            }

            return false;
        }

        private string GenerateClientProxyCode(INamedTypeSymbol interfaceSymbol)
        {
            var namespaceName = interfaceSymbol.ContainingNamespace.ToDisplayString();
            var interfaceName = interfaceSymbol.Name;
            var methodsCode = new StringBuilder();
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

            // 处理每个方法
            foreach (var member in interfaceSymbol.GetMembers())
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
                    var returnType = ((INamedTypeSymbol)methodSymbol.ReturnType).TypeArguments.FirstOrDefault();
                    if (returnType != null)
                    {
                        var returnTypeNamespace = returnType.ContainingNamespace?.ToDisplayString();
                        if (!string.IsNullOrEmpty(returnTypeNamespace) && !usings.Contains(returnTypeNamespace))
                            usings.Add(returnTypeNamespace);
                    }

                    // 生成方法实现
                    methodsCode.AppendLine(GenerateMethodImplementation(methodSymbol));
                }
            }

            // 生成完整的类代码
            var proxyCode = $@"// <auto-generated/>
#nullable enable
{string.Join("\n", usings.Select(u => $"using {u};"))}

namespace PulseRPC.Client.Generated
{{
    /// <summary>
    /// 自动生成的 {interfaceName} 服务客户端代理
    /// </summary>
    public sealed class {interfaceName}ClientImpl : {namespaceName}.{interfaceName}
    {{
        private readonly IPulseConnection _connection;

        public {interfaceName}ClientImpl(IPulseConnection connection)
        {{
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        }}

{methodsCode}
    }}
}}";

            return proxyCode;
        }

        private string GenerateMethodImplementation(IMethodSymbol methodSymbol)
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
            var hasReturnValue = taskType?.TypeArguments.Length > 0;
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
                throw new RpcException($""调用{methodName}时出错: {{ex.Message}}"", ex);
            }}
        }}";

            return methodBody;
        }

        private string? GetDefaultValueString(IParameterSymbol parameter)
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
        /// 语法接收器，用于收集潜在的服务接口
        /// </summary>
        private class ServiceInterfaceSyntaxReceiver : ISyntaxReceiver
        {
            public List<InterfaceDeclarationSyntax> CandidateInterfaces { get; } = new List<InterfaceDeclarationSyntax>();

            public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
            {
                // 查找接口声明
                if (syntaxNode is InterfaceDeclarationSyntax interfaceDeclaration)
                {
                    // 检查是否继承了IPulseService
                    var baseList = interfaceDeclaration.BaseList;
                    if (baseList != null)
                    {
                        foreach (var baseType in baseList.Types)
                        {
                            if (baseType.Type is GenericNameSyntax genericName &&
                                genericName.Identifier.Text == "IPulseService")
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
