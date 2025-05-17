using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace PulseRPC.Generators;

/// <summary>
/// 接收器处理器生成器
/// </summary>
[Generator]
public class ReceiverHandlerGenerator : ISourceGenerator
{
    /// <summary>
    /// 初始化
    /// </summary>
    public void Initialize(GeneratorInitializationContext context)
    {
        context.RegisterForSyntaxNotifications(() => new ReceiverSyntaxReceiver());
    }

    /// <summary>
    /// 执行生成
    /// </summary>
    public void Execute(GeneratorExecutionContext context)
    {
        // 获取语法接收器
        if (context.SyntaxContextReceiver is not ReceiverSyntaxReceiver receiver)
        {
            return;
        }
        
        // 生成接收器处理器
        foreach (var receiverType in receiver.ReceiverTypes)
        {
            try
            {
                GenerateReceiverHandler(context, receiverType);
            }
            catch (Exception ex)
            {
                var diagnostic = Diagnostic.Create(
                    new DiagnosticDescriptor(
                        id: "PRPC601",
                        title: "接收器处理器生成出错",
                        messageFormat: "生成接收器 {0} 的处理器代码时出错: {1}",
                        category: "PulseRPC.Generator",
                        defaultSeverity: DiagnosticSeverity.Error,
                        isEnabledByDefault: true),
                    Location.None,
                    receiverType.Name,
                    ex.Message);
                
                context.ReportDiagnostic(diagnostic);
            }
        }
    }
    
    private void GenerateReceiverHandler(GeneratorExecutionContext context, INamedTypeSymbol receiverType)
    {
        // 获取接收器名称和命名空间
        var receiverName = receiverType.Name;
        var receiverNamespace = receiverType.ContainingNamespace.ToDisplayString();
        var handlerClassName = $"{receiverName}Handler";
        
        var receiverFullName = receiverType.ToDisplayString();
        
        // 构建处理器代码
        var sb = new StringBuilder();
        
        // 添加命名空间
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using PulseRPC;");
        sb.AppendLine("using PulseRPC.Protocol.Network;");
        sb.AppendLine();
        
        // 打开命名空间
        sb.AppendLine($"namespace {receiverNamespace}.Generated");
        sb.AppendLine("{");
        
        // 处理器类定义
        sb.AppendLine($"    /// <summary>");
        sb.AppendLine($"    /// {receiverName} 接收器的消息处理器");
        sb.AppendLine($"    /// </summary>");
        sb.AppendLine($"    public class {handlerClassName} : IMessageHandler");
        sb.AppendLine("    {");
        
        // 字段
        sb.AppendLine($"        private readonly {receiverFullName} _receiver;");
        sb.AppendLine("        private readonly IPulseRPCSerializer _serializer;");
        sb.AppendLine();
        
        // 构造函数
        sb.AppendLine($"        public {handlerClassName}({receiverFullName} receiver, IPulseRPCSerializer serializer)");
        sb.AppendLine("        {");
        sb.AppendLine("            _receiver = receiver;");
        sb.AppendLine("            _serializer = serializer;");
        sb.AppendLine("        }");
        sb.AppendLine();
        
        // 实现HandleMessageAsync方法
        sb.AppendLine("        public async Task HandleMessageAsync(NetworkSession session, ReceiverNotification notification, CancellationToken cancellationToken = default)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (notification.ReceiverType != typeof(ReceiverType).FullName)");
        sb.AppendLine("            {");
        sb.AppendLine("                throw new InvalidOperationException($\"接收器类型不匹配: {notification.ReceiverType}\");");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            switch (notification.MethodId)");
        sb.AppendLine("            {");
        
        // 为每个方法生成处理分支
        foreach (var member in receiverType.GetMembers())
        {
            if (member is not IMethodSymbol method || method.IsStatic || !method.DeclaredAccessibility.HasFlag(Accessibility.Public))
            {
                continue; // 跳过非公开方法或静态方法
            }
            
            // 检查是否是父接口方法
            if (!method.ContainingType.Equals(receiverType, SymbolEqualityComparer.Default))
            {
                continue;
            }
            
            // 获取方法ID
            var methodId = 0;
            foreach (var attr in method.GetAttributes())
            {
                if (attr.AttributeClass?.Name == "ReceiverMethodAttribute" &&
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
                methodId = Math.Abs(FNV1A32.GetHashCode($"{receiverFullName}.{method.Name}")) % 65536;
            }
            
            var methodName = method.Name;
            
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
            
            // 获取参数类型
            var parameters = method.Parameters;
            var parameterType = parameters.Length > 0 ? parameters[0].Type : null;
            
            // 生成方法处理分支
            sb.AppendLine($"                case {methodId}: // {methodName}");
            sb.AppendLine("                {");
            
            // 反序列化参数
            if (parameterType != null)
            {
                var paramTypeName = parameterType.ToDisplayString();
                sb.AppendLine($"                    var parameter = ({paramTypeName})_serializer.Deserialize(notification.Parameters, typeof({paramTypeName}))!;");
            }
            
            // 调用接收器方法
            if (isAsync)
            {
                if (actualReturnType != null)
                {
                    // 异步有返回值
                    if (parameterType != null)
                    {
                        sb.AppendLine($"                    var result = await _receiver.{methodName}(parameter);");
                    }
                    else
                    {
                        sb.AppendLine($"                    var result = await _receiver.{methodName}();");
                    }
                    
                    // 发送响应
                    sb.AppendLine();
                    sb.AppendLine("                    // 发送响应");
                    sb.AppendLine("                    var response = new ReceiverResponse");
                    sb.AppendLine("                    {");
                    sb.AppendLine("                        NotificationId = Guid.NewGuid(),");
                    sb.AppendLine("                        Result = _serializer.Serialize(result)");
                    sb.AppendLine("                    };");
                    sb.AppendLine();
                    sb.AppendLine("                    await session.SendPacketAsync(response);");
                }
                else
                {
                    // 异步无返回值
                    if (parameterType != null)
                    {
                        sb.AppendLine($"                    await _receiver.{methodName}(parameter);");
                    }
                    else
                    {
                        sb.AppendLine($"                    await _receiver.{methodName}();");
                    }
                }
            }
            else
            {
                // 同步方法
                if (parameterType != null)
                {
                    if (returnType.SpecialType != SpecialType.System_Void)
                    {
                        sb.AppendLine($"                    var result = _receiver.{methodName}(parameter);");
                        
                        // 发送响应
                        sb.AppendLine();
                        sb.AppendLine("                    // 发送响应");
                        sb.AppendLine("                    var response = new ReceiverResponse");
                        sb.AppendLine("                    {");
                        sb.AppendLine("                        NotificationId = Guid.NewGuid(),");
                        sb.AppendLine("                        Result = _serializer.Serialize(result)");
                        sb.AppendLine("                    };");
                        sb.AppendLine();
                        sb.AppendLine("                    await session.SendPacketAsync(response);");
                    }
                    else
                    {
                        sb.AppendLine($"                    _receiver.{methodName}(parameter);");
                    }
                }
                else
                {
                    if (returnType.SpecialType != SpecialType.System_Void)
                    {
                        sb.AppendLine($"                    var result = _receiver.{methodName}();");
                        
                        // 发送响应
                        sb.AppendLine();
                        sb.AppendLine("                    // 发送响应");
                        sb.AppendLine("                    var response = new ReceiverResponse");
                        sb.AppendLine("                    {");
                        sb.AppendLine("                        NotificationId = Guid.NewGuid(),");
                        sb.AppendLine("                        Result = _serializer.Serialize(result)");
                        sb.AppendLine("                    };");
                        sb.AppendLine();
                        sb.AppendLine("                    await session.SendPacketAsync(response);");
                    }
                    else
                    {
                        sb.AppendLine($"                    _receiver.{methodName}();");
                    }
                }
            }
            
            sb.AppendLine("                    break;");
            sb.AppendLine("                }");
        }
        
        // 默认分支
        sb.AppendLine("                default:");
        sb.AppendLine("                    throw new NotSupportedException($\"未知的方法ID: {notification.MethodId}\");");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        
        // GetSupportedReceiverType方法
        sb.AppendLine();
        sb.AppendLine("        public Type ReceiverType => typeof(ReceiverType);");
        sb.AppendLine();
        
        sb.AppendLine("        public async Task HandleMessageAsync(NetworkSession session, IPacket packet, CancellationToken cancellationToken = default)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (packet is ReceiverNotification notification)");
        sb.AppendLine("            {");
        sb.AppendLine("                await HandleMessageAsync(session, notification, cancellationToken);");
        sb.AppendLine("            }");
        sb.AppendLine("            else");
        sb.AppendLine("            {");
        sb.AppendLine("                throw new InvalidOperationException($\"不支持的消息类型: {packet.GetType().Name}\");");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        
        // 类结束
        sb.AppendLine("    }");
        
        // 接收器类型标记
        sb.AppendLine();
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// 接收器类型标记");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine($"    public class ReceiverType : {receiverFullName}");
        sb.AppendLine("    {");
        
        // 实现接收器接口的所有方法
        foreach (var member in receiverType.GetMembers())
        {
            if (member is not IMethodSymbol method || method.IsStatic || !method.DeclaredAccessibility.HasFlag(Accessibility.Public))
            {
                continue; // 跳过非公开方法或静态方法
            }
            
            // 检查是否是父接口方法
            if (!method.ContainingType.Equals(receiverType, SymbolEqualityComparer.Default))
            {
                continue;
            }
            
            var methodName = method.Name;
            var returnDeclaration = method.ReturnType.ToDisplayString();
            
            // 生成方法实现
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
            
            // 方法体（返回默认值）
            if (method.ReturnType.SpecialType == SpecialType.System_Void)
            {
                sb.AppendLine("            // 此方法不会被直接调用");
            }
            else if (method.ReturnType.ToString() == "System.Threading.Tasks.Task")
            {
                sb.AppendLine("            // 此方法不会被直接调用");
                sb.AppendLine("            return Task.CompletedTask;");
            }
            else if (method.ReturnType is INamedTypeSymbol taskReturnType &&
                     taskReturnType.IsGenericType &&
                     taskReturnType.ConstructedFrom.ToString() == "System.Threading.Tasks.Task<T>")
            {
                var taskResultType = taskReturnType.TypeArguments[0];
                sb.AppendLine("            // 此方法不会被直接调用");
                
                if (taskResultType.IsReferenceType)
                {
                    sb.AppendLine($"            return Task.FromResult<{taskResultType.ToDisplayString()}>(null!);");
                }
                else
                {
                    sb.AppendLine($"            return Task.FromResult(default({taskResultType.ToDisplayString()}));");
                }
            }
            else
            {
                sb.AppendLine("            // 此方法不会被直接调用");
                if (method.ReturnType.IsReferenceType)
                {
                    sb.AppendLine("            return null!;");
                }
                else
                {
                    sb.AppendLine($"            return default({method.ReturnType.ToDisplayString()});");
                }
            }
            
            sb.AppendLine("        }");
            sb.AppendLine();
        }
        
        // 类结束
        sb.AppendLine("    }");
        
        // 命名空间结束
        sb.AppendLine("}");
        
        // 添加生成的代码
        context.AddSource($"{handlerClassName}.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }
    
    /// <summary>
    /// 接收器语法接收器，用于收集接收器接口
    /// </summary>
    private class ReceiverSyntaxReceiver : ISyntaxContextReceiver
    {
        /// <summary>
        /// 发现的接收器类型
        /// </summary>
        public List<INamedTypeSymbol> ReceiverTypes { get; } = new();
        
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
                
                // 检查是否实现了IReceiver<TReceiver>接口
                foreach (var intf in symbol.AllInterfaces)
                {
                    if (intf.IsGenericType && 
                        intf.ConstructedFrom.ToDisplayString() == "PulseRPC.IReceiver<TReceiver>" &&
                        intf.TypeArguments[0].Equals(symbol, SymbolEqualityComparer.Default))
                    {
                        ReceiverTypes.Add(symbol);
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