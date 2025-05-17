
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace PulseRPC.Client.SourceGenerator;

/// <summary>
/// 接收器实现类生成器
/// </summary>
[Generator]
public class ReceiverImplementationGenerator : ISourceGenerator
{
    /// <summary>
    /// 初始化
    /// </summary>
    public void Initialize(GeneratorInitializationContext context)
    {
        context.RegisterForSyntaxNotifications(() => new ReceiverImplementationSyntaxReceiver());
    }

    /// <summary>
    /// 执行生成
    /// </summary>
    public void Execute(GeneratorExecutionContext context)
    {
        // 获取语法接收器
        if (context.SyntaxContextReceiver is not ReceiverImplementationSyntaxReceiver receiver)
        {
            return;
        }

        // 生成接收器实现类
        foreach (var receiverType in receiver.ReceiverTypes)
        {
            try
            {
                GenerateReceiverImplementation(context, receiverType, receiver.NotificationHandlers);
            }
            catch (Exception ex)
            {
                var diagnostic = Diagnostic.Create(
                    new DiagnosticDescriptor(
                        id: "PRPC701",
                        title: "接收器实现类生成出错",
                        messageFormat: "生成接收器 {0} 的实现类时出错: {1}",
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

    private void GenerateReceiverImplementation(
        GeneratorExecutionContext context,
        INamedTypeSymbol receiverType,
        List<NotificationHandlerInfo> handlers)
    {
        // 获取接收器名称和命名空间
        var receiverName = receiverType.Name;
        var receiverNamespace = receiverType.ContainingNamespace.ToDisplayString();
        var implClassName = $"{receiverName}Impl";

        var receiverFullName = receiverType.ToDisplayString();

        // 构建实现类代码
        var sb = new StringBuilder();

        // 添加命名空间
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using PulseRPC;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine();

        // 打开命名空间
        sb.AppendLine($"namespace {receiverNamespace}.Generated");
        sb.AppendLine("{");

        // 实现类定义
        sb.AppendLine($"    /// <summary>");
        sb.AppendLine($"    /// {receiverName} 接收器的自动生成实现类");
        sb.AppendLine($"    /// </summary>");
        sb.AppendLine($"    public class {implClassName} : {receiverFullName}");
        sb.AppendLine("    {");

        // 字段
        sb.AppendLine("        private readonly IServiceProvider _serviceProvider;");
        sb.AppendLine();

        // 构造函数
        sb.AppendLine($"        public {implClassName}(IServiceProvider serviceProvider)");
        sb.AppendLine("        {");
        sb.AppendLine("            _serviceProvider = serviceProvider;");
        sb.AppendLine("        }");
        sb.AppendLine();

        // 实现接收器接口的所有方法
        foreach (var member in receiverType.GetMembers())
        {
            if (member is not IMethodSymbol method || method.MethodKind != MethodKind.Ordinary)
                continue;

            // 获取对应的通知处理器
            var methodName = method.Name;
            var paramType = method.Parameters.FirstOrDefault()?.Type;

            var matchingHandler = handlers.FirstOrDefault(h =>
                h.HandlerType.AllInterfaces.Any(i =>
                    i.IsGenericType &&
                    i.ConstructedFrom.ToDisplayString() == "PulseRPC.INotificationHandler<T>" &&
                    i.TypeArguments[0].Equals(paramType, SymbolEqualityComparer.Default)));

            // 方法签名
            sb.AppendLine($"        public {method.ReturnType} {methodName}({string.Join(", ", method.Parameters.Select(p => $"{p.Type} {p.Name}"))})");
            sb.AppendLine("        {");

            if (matchingHandler != null)
            {
                var handlerTypeName = matchingHandler.HandlerType.ToDisplayString();
                sb.AppendLine($"            // 使用INotificationHandler处理通知");
                sb.AppendLine($"            var handler = _serviceProvider.GetRequiredService<{handlerTypeName}>();");

                var paramName = method.Parameters.FirstOrDefault()?.Name ?? "notification";
                if (method.ReturnsVoid)
                {
                    sb.AppendLine($"            handler.Handle({paramName});");
                }
                else if (method.ReturnType.Name == "Task")
                {
                    sb.AppendLine($"            return handler.Handle({paramName});");
                }
                else if (method.ReturnType.Name == "Task`1")
                {
                    sb.AppendLine($"            // 注意：INotificationHandler返回Task，需要适配成Task<T>");
                    sb.AppendLine($"            throw new NotImplementedException(\"需要适配INotificationHandler返回类型\");");
                }
                else
                {
                    sb.AppendLine($"            // 不支持的返回类型");
                    sb.AppendLine($"            throw new NotImplementedException(\"不支持的返回类型\");");
                }
            }
            else
            {
                // 没有匹配的Handler，提供默认实现
                if (method.ReturnsVoid)
                {
                    sb.AppendLine($"            // 未找到匹配的INotificationHandler<{paramType}>");
                    sb.AppendLine($"            Console.WriteLine($\"接收到通知: {{({paramType}){method.Parameters.FirstOrDefault()?.Name}}}\");");
                }
                else if (method.ReturnType.Name == "Task")
                {
                    sb.AppendLine($"            // 未找到匹配的INotificationHandler<{paramType}>");
                    sb.AppendLine($"            Console.WriteLine($\"接收到通知: {{({paramType}){method.Parameters.FirstOrDefault()?.Name}}}\");");
                    sb.AppendLine($"            return Task.CompletedTask;");
                }
                else if (method.ReturnType.Name == "Task`1")
                {
                    var returnTypeArg = ((INamedTypeSymbol)method.ReturnType).TypeArguments[0];
                    sb.AppendLine($"            // 未找到匹配的INotificationHandler<{paramType}>");
                    sb.AppendLine($"            Console.WriteLine($\"接收到通知: {{({paramType}){method.Parameters.FirstOrDefault()?.Name}}}\");");
                    sb.AppendLine($"            return Task.FromResult<{returnTypeArg}>(default);");
                }
                else
                {
                    sb.AppendLine($"            // 不支持的返回类型");
                    sb.AppendLine($"            throw new NotImplementedException(\"不支持的返回类型\");");
                }
            }

            sb.AppendLine("        }");
            sb.AppendLine();
        }

        // 类结束
        sb.AppendLine("    }");

        // 扩展方法类，用于注册接收器
        sb.AppendLine();
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// 接收器注册扩展方法");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public static class ReceiverExtensions");
        sb.AppendLine("    {");
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// 注册接收器实现");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        public static IServiceCollection AddGeneratedReceiver(this IServiceCollection services)");
        sb.AppendLine("        {");
        sb.AppendLine($"            services.AddTransient<{receiverFullName}, {implClassName}>();");

        // 注册所有Handler
        foreach (var handler in handlers)
        {
            sb.AppendLine($"            services.AddTransient<{handler.HandlerType.ToDisplayString()}>();");
        }

        sb.AppendLine("            return services;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");

        // 命名空间结束
        sb.AppendLine("}");

        // 添加生成的代码
        context.AddSource($"{implClassName}.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    /// <summary>
    /// 接收器接口和通知处理器语法接收器
    /// </summary>
    private class ReceiverImplementationSyntaxReceiver : ISyntaxContextReceiver
    {
        /// <summary>
        /// 发现的接收器类型
        /// </summary>
        public List<INamedTypeSymbol> ReceiverTypes { get; } = new();

        /// <summary>
        /// 发现的通知处理器
        /// </summary>
        public List<NotificationHandlerInfo> NotificationHandlers { get; } = new();

        /// <summary>
        /// 访问语法节点
        /// </summary>
        public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
        {
            if (context.Node is InterfaceDeclarationSyntax interfaceDeclaration)
            {
                var symbol = context.SemanticModel.GetDeclaredSymbol(interfaceDeclaration) as INamedTypeSymbol;
                if (symbol == null) return;

                // 检查是否为IStreamingReceiver接口
                foreach (var intf in symbol.AllInterfaces)
                {
                    if (intf.ToDisplayString() == "PulseRPC.IStreamingReceiver")
                    {
                        ReceiverTypes.Add(symbol);
                        break;
                    }
                }
            }
            else if (context.Node is ClassDeclarationSyntax classDeclaration)
            {
                var symbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration) as INamedTypeSymbol;
                if (symbol == null) return;

                // 检查是否实现了INotificationHandler<T>
                foreach (var intf in symbol.AllInterfaces)
                {
                    if (intf.IsGenericType && intf.ConstructedFrom.ToDisplayString() == "PulseRPC.INotificationHandler<T>")
                    {
                        NotificationHandlers.Add(new NotificationHandlerInfo(symbol, intf.TypeArguments[0]));
                        break;
                    }
                }
            }
        }
    }

    /// <summary>
    /// 通知处理器信息
    /// </summary>
    private class NotificationHandlerInfo(INamedTypeSymbol handlerType, ITypeSymbol notificationType)
    {
        /// <summary>
        /// 处理器类型
        /// </summary>
        public INamedTypeSymbol HandlerType { get; set; } = handlerType;

        /// <summary>
        /// 通知类型
        /// </summary>
        public ITypeSymbol NotificationType { get; set; } = notificationType;
    }
}
