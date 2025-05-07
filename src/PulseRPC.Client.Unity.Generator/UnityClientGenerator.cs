using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace PulseRPC.Client.Unity.Generator
{
    /// <summary>
    /// Unity客户端生成器，用于生成AOT友好的代码
    /// </summary>
    [Generator]
    public class UnityClientGenerator : ISourceGenerator
    {
        public void Initialize(GeneratorInitializationContext context)
        {
            // 注册语法接收器
            context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());
        }

        public void Execute(GeneratorExecutionContext context)
        {
            // 获取语法接收器
            if (!(context.SyntaxContextReceiver is SyntaxReceiver receiver))
                return;

            // 生成AOT注册代码
            GenerateAOTRegistrationCode(context, receiver);

            // 生成客户端代理
            GenerateClientProxies(context, receiver);
        }

        /// <summary>
        /// 生成AOT注册代码
        /// </summary>
        private void GenerateAOTRegistrationCode(GeneratorExecutionContext context, SyntaxReceiver receiver)
        {
            var messageTypes = receiver.MessageTypes;
            if (messageTypes.Count == 0)
                return;

            var sb = new StringBuilder();
            sb.AppendLine("using System;");
            sb.AppendLine("using MemoryPack;");
            sb.AppendLine("using PulseRPC.AOT;");
            sb.AppendLine();
            sb.AppendLine("namespace PulseRPC.Generated");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// 自动生成的AOT注册类，用于在IL2CPP环境中注册所有消息类型");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    public static class GeneratedAOTRegistration");
            sb.AppendLine("    {");
            sb.AppendLine("        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]");
            sb.AppendLine("        public static void RegisterTypes()");
            sb.AppendLine("        {");
            sb.AppendLine("            UnityEngine.Debug.Log(\"PulseRPC: 注册生成的AOT类型\");");
            sb.AppendLine();

            // 注册所有消息类型
            foreach (var type in messageTypes)
            {
                sb.AppendLine($"            // 注册 {type.Name} 类型");
                sb.AppendLine($"            MemoryPackFormatterProvider.Register<{type.ToDisplayString()}>();");
            }

            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            // 添加生成的代码
            context.AddSource("GeneratedAOTRegistration.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
        }

        /// <summary>
        /// 生成客户端代理
        /// </summary>
        private void GenerateClientProxies(GeneratorExecutionContext context, SyntaxReceiver receiver)
        {
            var serviceInterfaces = receiver.ServiceInterfaces;
            if (serviceInterfaces.Count == 0)
                return;

            foreach (var serviceInterface in serviceInterfaces)
            {
                // 生成客户端代理
                var proxyCode = GenerateClientProxy(serviceInterface);
                var fileName = $"{serviceInterface.Name.TrimStart('I')}ClientProxy.g.cs";

                // 添加生成的代码
                context.AddSource(fileName, SourceText.From(proxyCode, Encoding.UTF8));
            }
        }

        /// <summary>
        /// 为服务接口生成客户端代理
        /// </summary>
        private string GenerateClientProxy(INamedTypeSymbol serviceInterface)
        {
            var interfaceName = serviceInterface.Name;
            var serviceName = interfaceName.TrimStart('I');
            var namespaceName = serviceInterface.ContainingNamespace.ToDisplayString();

            var sb = new StringBuilder();
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Threading.Tasks;");
            sb.AppendLine("using PulseRPC;");
            sb.AppendLine("using PulseRPC.Serialization;");
            sb.AppendLine();
            sb.AppendLine($"namespace {namespaceName}.Generated");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>");
            sb.AppendLine($"    /// {serviceName}服务的Unity客户端代理");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine($"    public class {serviceName}ClientProxy : {interfaceName}");
            sb.AppendLine("    {");
            sb.AppendLine("        private readonly UnityClient _client;");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>");
            sb.AppendLine($"        /// 创建{serviceName}服务的客户端代理");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        /// <param name=\"client\">Unity客户端实例</param>");
            sb.AppendLine($"        public {serviceName}ClientProxy(UnityClient client)");
            sb.AppendLine("        {");
            sb.AppendLine("            _client = client ?? throw new ArgumentNullException(nameof(client));");
            sb.AppendLine("        }");
            sb.AppendLine();

            // 生成方法实现
            foreach (var member in serviceInterface.GetMembers().OfType<IMethodSymbol>())
            {
                if (member.MethodKind != MethodKind.Ordinary)
                    continue;

                // 获取方法信息
                var methodName = member.Name;
                var returnType = member.ReturnType;
                var parameters = member.Parameters;

                sb.AppendLine("        /// <summary>");
                sb.AppendLine($"        /// {methodName}方法的客户端代理实现");
                sb.AppendLine("        /// </summary>");

                // 生成方法签名
                sb.Append($"        public {returnType.ToDisplayString()} {methodName}(");
                sb.Append(string.Join(", ", parameters.Select(p => $"{p.Type.ToDisplayString()} {p.Name}")));
                sb.AppendLine(")");
                sb.AppendLine("        {");

                // 检查返回类型是否为Task<T>
                if (returnType is INamedTypeSymbol namedReturnType &&
                    namedReturnType.OriginalDefinition.ToDisplayString() == "System.Threading.Tasks.Task<T>")
                {
                    // 获取响应类型
                    var responseType = namedReturnType.TypeArguments[0];

                    // 创建请求类型名称
                    var requestTypeName = $"{serviceName}{methodName}Request";

                    // 生成异步方法实现
                    sb.AppendLine($"            // 创建请求对象");
                    sb.AppendLine($"            var request = new {namespaceName}.{requestTypeName}");
                    sb.AppendLine("            {");

                    // 添加参数
                    foreach (var param in parameters)
                    {
                        sb.AppendLine($"                {char.ToUpper(param.Name[0]) + param.Name.Substring(1)} = {param.Name},");
                    }

                    sb.AppendLine("            };");
                    sb.AppendLine();
                    sb.AppendLine("            // 发送请求并返回响应");
                    sb.AppendLine($"            return _client.SendRequest<{namespaceName}.{requestTypeName}, {responseType.ToDisplayString()}>(request);");
                }
                else
                {
                    sb.AppendLine("            throw new NotImplementedException(\"只支持Task<T>返回类型的方法\");");
                }

                sb.AppendLine("        }");
                sb.AppendLine();
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        /// <summary>
        /// 语法接收器，用于收集服务接口和消息类型
        /// </summary>
        private class SyntaxReceiver : ISyntaxContextReceiver
        {
            public List<INamedTypeSymbol> ServiceInterfaces { get; } = new List<INamedTypeSymbol>();
            public List<INamedTypeSymbol> MessageTypes { get; } = new List<INamedTypeSymbol>();

            public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
            {
                // 查找服务接口
                if (context.Node is InterfaceDeclarationSyntax interfaceDeclaration &&
                    interfaceDeclaration.AttributeLists.Count > 0)
                {
                    var symbol = context.SemanticModel.GetDeclaredSymbol(interfaceDeclaration) as INamedTypeSymbol;
                    if (symbol != null && HasServiceAttribute(symbol))
                    {
                        ServiceInterfaces.Add(symbol);

                        // 收集接口中使用的消息类型
                        CollectMessageTypes(symbol);
                    }
                }

                // 查找消息类型（带有MessagePackObject特性的类）
                if (context.Node is ClassDeclarationSyntax classDeclaration &&
                    classDeclaration.AttributeLists.Count > 0)
                {
                    var symbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration) as INamedTypeSymbol;
                    if (symbol != null && HasMemoryPackAttribute(symbol) && !MessageTypes.Contains(symbol))
                    {
                        MessageTypes.Add(symbol);
                    }
                }
            }

            /// <summary>
            /// 检查类型是否具有服务特性
            /// </summary>
            private bool HasServiceAttribute(INamedTypeSymbol symbol)
            {
                return symbol.GetAttributes().Any(attr =>
                    attr.AttributeClass?.Name == "ServiceAttribute" ||
                    attr.AttributeClass?.Name == "ServiceContractAttribute");
            }

            /// <summary>
            /// 检查类型是否具有MemoryPack特性
            /// </summary>
            private bool HasMemoryPackAttribute(INamedTypeSymbol symbol)
            {
                return symbol.GetAttributes().Any(attr =>
                    attr.AttributeClass?.Name == "MemoryPackableAttribute" ||
                    attr.AttributeClass?.ToDisplayString() == "MemoryPack.MemoryPackableAttribute");
            }

            /// <summary>
            /// 收集接口中使用的消息类型
            /// </summary>
            private void CollectMessageTypes(INamedTypeSymbol interfaceSymbol)
            {
                foreach (var member in interfaceSymbol.GetMembers().OfType<IMethodSymbol>())
                {
                    // 检查返回类型
                    if (member.ReturnType is INamedTypeSymbol namedReturnType &&
                        namedReturnType.OriginalDefinition.ToDisplayString() == "System.Threading.Tasks.Task<T>")
                    {
                        var responseType = namedReturnType.TypeArguments[0];
                        if (responseType is INamedTypeSymbol responseTypeSymbol &&
                            HasMemoryPackAttribute(responseTypeSymbol) &&
                            !MessageTypes.Contains(responseTypeSymbol))
                        {
                            MessageTypes.Add(responseTypeSymbol);
                        }
                    }

                    // 检查参数类型
                    foreach (var parameter in member.Parameters)
                    {
                        if (parameter.Type is INamedTypeSymbol paramTypeSymbol &&
                            HasMemoryPackAttribute(paramTypeSymbol) &&
                            !MessageTypes.Contains(paramTypeSymbol))
                        {
                            MessageTypes.Add(paramTypeSymbol);
                        }
                    }
                }
            }
        }
    }
}
