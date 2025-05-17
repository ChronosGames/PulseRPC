using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace PulseRPC.Generators;

/// <summary>
/// 服务客户端生成器
/// </summary>
[Generator]
public class ServiceClientGenerator : ISourceGenerator
{
    /// <summary>
    /// 初始化
    /// </summary>
    public void Initialize(GeneratorInitializationContext context)
    {
        context.RegisterForSyntaxNotifications(() => new ServiceSyntaxReceiver());
    }

    /// <summary>
    /// 执行生成
    /// </summary>
    public void Execute(GeneratorExecutionContext context)
    {
        // 获取语法接收器
        if (context.SyntaxContextReceiver is not ServiceSyntaxReceiver receiver)
        {
            return;
        }
        
        // 生成服务客户端代理
        foreach (var serviceType in receiver.ServiceTypes)
        {
            try
            {
                GenerateServiceClient(context, serviceType);
            }
            catch (Exception ex)
            {
                var diagnostic = Diagnostic.Create(
                    new DiagnosticDescriptor(
                        id: "PRPC501",
                        title: "服务客户端生成出错",
                        messageFormat: "生成服务 {0} 的客户端代码时出错: {1}",
                        category: "PulseRPC.Generator",
                        defaultSeverity: DiagnosticSeverity.Error,
                        isEnabledByDefault: true),
                    Location.None,
                    serviceType.Name,
                    ex.Message);
                
                context.ReportDiagnostic(diagnostic);
            }
        }
    }
    
    private void GenerateServiceClient(GeneratorExecutionContext context, INamedTypeSymbol serviceType)
    {
        // 获取服务名称和命名空间
        var serviceName = serviceType.Name;
        var serviceNamespace = serviceType.ContainingNamespace.ToDisplayString();
        var clientClassName = $"{serviceName}Client";
        
        var serviceFullName = serviceType.ToDisplayString();
        
        // 构建客户端代码
        var sb = new StringBuilder();
        
        // 添加命名空间
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using PulseRPC;");
        sb.AppendLine("using PulseRPC.Protocol.Network;");
        sb.AppendLine();
        
        // 打开命名空间
        sb.AppendLine($"namespace {serviceNamespace}.Generated");
        sb.AppendLine("{");
        
        // 客户端类定义
        sb.AppendLine($"    /// <summary>");
        sb.AppendLine($"    /// {serviceName} 服务的客户端代理");
        sb.AppendLine($"    /// </summary>");
        sb.AppendLine($"    public class {clientClassName} : {serviceFullName}");
        sb.AppendLine("    {");
        
        // 字段
        sb.AppendLine("        private readonly IPulseConnection _connection;");
        sb.AppendLine("        private readonly IPulseRPCSerializer _serializer;");
        sb.AppendLine("        private DateTime? _deadline;");
        sb.AppendLine("        private CancellationToken _cancellationToken;");
        sb.AppendLine("        private string? _host;");
        sb.AppendLine();
        
        // 构造函数
        sb.AppendLine($"        public {clientClassName}(IPulseConnection connection, IPulseRPCSerializer serializer)");
        sb.AppendLine("        {");
        sb.AppendLine("            _connection = connection;");
        sb.AppendLine("            _serializer = serializer;");
        sb.AppendLine("            _cancellationToken = CancellationToken.None;");
        sb.AppendLine("        }");
        sb.AppendLine();
        
        // 实现IService接口配置方法
        sb.AppendLine($"        public {serviceFullName} WithDeadline(DateTime deadline)");
        sb.AppendLine("        {");
        sb.AppendLine("            _deadline = deadline;");
        sb.AppendLine("            return this;");
        sb.AppendLine("        }");
        sb.AppendLine();
        
        sb.AppendLine($"        public {serviceFullName} WithCancellationToken(CancellationToken cancellationToken)");
        sb.AppendLine("        {");
        sb.AppendLine("            _cancellationToken = cancellationToken;");
        sb.AppendLine("            return this;");
        sb.AppendLine("        }");
        sb.AppendLine();
        
        sb.AppendLine($"        public {serviceFullName} WithHost(string host)");
        sb.AppendLine("        {");
        sb.AppendLine("            _host = host;");
        sb.AppendLine("            return this;");
        sb.AppendLine("        }");
        sb.AppendLine();
        
        // 实现所有服务方法
        foreach (var member in serviceType.GetMembers())
        {
            if (member is not IMethodSymbol method || method.IsStatic || !method.DeclaredAccessibility.HasFlag(Accessibility.Public))
            {
                continue; // 跳过非公开方法或静态方法
            }
            
            // 检查是否是父接口方法（如IService<T>的方法）
            if (!method.ContainingType.Equals(serviceType, SymbolEqualityComparer.Default))
            {
                continue;
            }
            
            // 获取方法ID
            var methodId = 0;
            foreach (var attr in method.GetAttributes())
            {
                if (attr.AttributeClass?.Name == "ServiceMethodAttribute" &&
                    attr.AttributeClass.ContainingNamespace.ToString() == "PulseRPC")
                {
                    if (attr.ConstructorArguments.Length > 0)
                    {
                        methodId = Convert.ToUInt16(attr.ConstructorArguments[0].Value);
                    }
                    break;
                }
            }
            
            // 如果没有指定ID，则根据方法名生成
            if (methodId == 0)
            {
                methodId = Math.Abs(FNV1A32.GetHashCode($"{serviceFullName}.{method.Name}")) % 65536;
            }
            
            // 分析返回类型
            var returnType = method.ReturnType;
            var isAsync = false;
            var actualReturnType = returnType;
            
            if (returnType is INamedTypeSymbol namedReturnType &&
                namedReturnType.IsGenericType &&
                namedReturnType.ConstructedFrom.ToString() == "System.Threading.Tasks.Task<T>")
            {
                isAsync = true;
                actualReturnType = namedReturnType.TypeArguments[0];
            }
            else if (returnType.ToString() == "System.Threading.Tasks.Task")
            {
                isAsync = true;
                actualReturnType = null;
            }
            
            // 生成方法实现
            var returnDeclaration = method.ReturnType.ToDisplayString();
            var methodName = method.Name;
            
            sb.AppendLine($"        public {returnDeclaration} {methodName}(");
            
            // 参数声明
            var parameters = method.Parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                var param = parameters[i];
                var paramType = param.Type.ToDisplayString();
                var paramName = param.Name;
                var isLast = i == parameters.Length - 1;
                
                sb.Append($"            {paramType} {paramName}");
                if (!isLast)
                {
                    sb.AppendLine(",");
                }
            }
            sb.AppendLine(")");
            sb.AppendLine("        {");
            
            // 方法实现
            sb.AppendLine($"            var request = new ServiceRequest");
            sb.AppendLine("            {");
            sb.AppendLine($"                ServiceType = \"{serviceFullName}\",");
            sb.AppendLine($"                MethodId = {methodId}");
            sb.AppendLine("            };");
            sb.AppendLine();
            
            // 序列化参数
            if (parameters.Length == 1)
            {
                sb.AppendLine($"            request.Parameters = _serializer.Serialize({parameters[0].Name});");
            }
            else if (parameters.Length > 1)
            {
                sb.AppendLine("            // 多参数暂不支持");
                sb.AppendLine("            throw new NotSupportedException(\"暂不支持多参数方法\");");
            }
            
            // 配置请求选项
            sb.AppendLine();
            sb.AppendLine("            // 应用配置的选项");
            sb.AppendLine("            var options = new RequestOptions");
            sb.AppendLine("            {");
            sb.AppendLine("                Deadline = _deadline,");
            sb.AppendLine("                CancellationToken = _cancellationToken,");
            sb.AppendLine("                Host = _host");
            sb.AppendLine("            };");
            sb.AppendLine();
            
            // 执行请求
            if (isAsync)
            {
                if (actualReturnType != null)
                {
                    // 异步有返回值
                    var returnTypeName = actualReturnType.ToDisplayString();
                    sb.AppendLine($"            return ExecuteAsync<{returnTypeName}>(request, options);");
                }
                else
                {
                    // 异步无返回值
                    sb.AppendLine($"            return ExecuteAsync(request, options);");
                }
            }
            else
            {
                if (actualReturnType.SpecialType == SpecialType.System_Void)
                {
                    // 同步无返回值
                    sb.AppendLine($"            Execute(request, options);");
                }
                else
                {
                    // 同步有返回值
                    var returnTypeName = actualReturnType.ToDisplayString();
                    sb.AppendLine($"            return Execute<{returnTypeName}>(request, options);");
                }
            }
            
            sb.AppendLine("        }");
            sb.AppendLine();
        }
        
        // 辅助方法
        sb.AppendLine("        private async Task<T> ExecuteAsync<T>(ServiceRequest request, RequestOptions options)");
        sb.AppendLine("        {");
        sb.AppendLine("            var ct = options.CancellationToken;");
        sb.AppendLine("            var response = await _connection.SendRequestAsync<ServiceResponse>(request, ct);");
        sb.AppendLine();
        sb.AppendLine("            if (!response.IsSuccess)");
        sb.AppendLine("            {");
        sb.AppendLine("                throw new RpcException(response.ErrorMessage ?? \"未知错误\", response.ErrorType);");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            if (response.Result == null)");
        sb.AppendLine("            {");
        sb.AppendLine("                throw new RpcException(\"服务返回了空结果\");");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            return (T)_serializer.Deserialize(response.Result, typeof(T))!;");
        sb.AppendLine("        }");
        sb.AppendLine();
        
        sb.AppendLine("        private async Task ExecuteAsync(ServiceRequest request, RequestOptions options)");
        sb.AppendLine("        {");
        sb.AppendLine("            var ct = options.CancellationToken;");
        sb.AppendLine("            var response = await _connection.SendRequestAsync<ServiceResponse>(request, ct);");
        sb.AppendLine();
        sb.AppendLine("            if (!response.IsSuccess)");
        sb.AppendLine("            {");
        sb.AppendLine("                throw new RpcException(response.ErrorMessage ?? \"未知错误\", response.ErrorType);");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine();
        
        sb.AppendLine("        private T Execute<T>(ServiceRequest request, RequestOptions options)");
        sb.AppendLine("        {");
        sb.AppendLine("            return ExecuteAsync<T>(request, options).GetAwaiter().GetResult();");
        sb.AppendLine("        }");
        sb.AppendLine();
        
        sb.AppendLine("        private void Execute(ServiceRequest request, RequestOptions options)");
        sb.AppendLine("        {");
        sb.AppendLine("            ExecuteAsync(request, options).GetAwaiter().GetResult();");
        sb.AppendLine("        }");
        
        // 类结束
        sb.AppendLine("    }");
        
        // 请求选项类
        sb.AppendLine();
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// 请求选项");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    internal class RequestOptions");
        sb.AppendLine("    {");
        sb.AppendLine("        public DateTime? Deadline { get; set; }");
        sb.AppendLine("        public CancellationToken CancellationToken { get; set; }");
        sb.AppendLine("        public string? Host { get; set; }");
        sb.AppendLine("    }");
        
        // 命名空间结束
        sb.AppendLine("}");
        
        // 添加生成的代码
        context.AddSource($"{clientClassName}.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }
    
    /// <summary>
    /// 服务语法接收器，用于收集服务接口
    /// </summary>
    private class ServiceSyntaxReceiver : ISyntaxContextReceiver
    {
        /// <summary>
        /// 发现的服务类型
        /// </summary>
        public List<INamedTypeSymbol> ServiceTypes { get; } = new();
        
        /// <summary>
        /// 访问语法节点
        /// </summary>
        public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
        {
            // 检查是否为接口声明
            if (context.Node is InterfaceDeclarationSyntax interfaceDeclaration)
            {
                var symbol = context.SemanticModel.GetDeclaredSymbol(interfaceDeclaration) as INamedTypeSymbol;
                if (symbol == null)
                {
                    return;
                }
                
                // 检查是否实现了IService<TSelf>接口
                foreach (var intf in symbol.AllInterfaces)
                {
                    if (intf.IsGenericType && 
                        intf.ConstructedFrom.ToDisplayString() == "PulseRPC.IService<TSelf>" &&
                        intf.TypeArguments[0].Equals(symbol, SymbolEqualityComparer.Default))
                    {
                        ServiceTypes.Add(symbol);
                        break;
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// FNV-1a哈希函数实现，用于生成方法ID
    /// </summary>
    private static class FNV1A32
    {
        private const uint FnvPrime = 16777619;
        private const uint FnvOffsetBasis = 2166136261;

        /// <summary>
        /// 计算字符串的FNV-1a哈希值
        /// </summary>
        /// <param name="text">输入文本</param>
        /// <returns>哈希值</returns>
        public static int GetHashCode(string text)
        {
            uint hash = FnvOffsetBasis;

            foreach (var c in text)
            {
                hash ^= c;
                hash *= FnvPrime;
            }

            return (int)(hash & 0x7FFFFFFF);
        }
    }
} 