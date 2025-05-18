using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MemoryPack;

namespace PulseRPC.Client.SourceGenerator;

[Generator]
public class StreamingHubClientGenerator : IIncrementalGenerator
{
    private const string IStreamingHubFullName = "PulseRPC.IStreamingHub";
    private const string PulseClientGenerationAttributeName = "PulseClientGenerationAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 方法一：扫描接口声明
        IncrementalValuesProvider<INamedTypeSymbol> fromInterfaces = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => s is InterfaceDeclarationSyntax { AttributeLists.Count: > 0 },
                transform: static (ctx, _) => GetServiceTypeFromInterface(ctx))
            .Where(static m => m is not null)!;

        // 方法二：扫描带有 PulseClientGenerationAttribute 的类
        // 注册语法提供器：查找所有带有特性的类
        IncrementalValuesProvider<ClassDeclarationSyntax> classDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => s is ClassDeclarationSyntax { AttributeLists.Count: > 0 },
                transform: static (ctx, _) => (ClassDeclarationSyntax)ctx.Node)
            .Where(static m => m is not null);

        // 创建编译供应商
        IncrementalValueProvider<Compilation> compilationProvider = context.CompilationProvider;

        // 结合语法和编译信息
        IncrementalValuesProvider<(ClassDeclarationSyntax Syntax, Compilation Compilation)> syntaxWithCompilation =
            classDeclarations.Combine(compilationProvider);

        // 获取标记类中指定的服务类型
        IncrementalValuesProvider<INamedTypeSymbol> fromAttributes = syntaxWithCompilation
            .SelectMany((pair, ct) => GetServiceTypesFromAttribute(pair.Syntax, pair.Compilation, ct))
            .Where(static symbol => symbol is not null)!;

        // 合并两种方式获取的服务类型
        var allServiceTypes = fromInterfaces
            .Collect()
            .Combine(fromAttributes.Collect())
            .SelectMany((pair, _) =>
            {
                var result = new List<INamedTypeSymbol>();
                result.AddRange(pair.Left);
                result.AddRange(pair.Right);
                // 使用 SymbolEqualityComparer 去重
                return result.Distinct(SymbolEqualityComparer.Default);
            });

        // 注册源代码输出
        context.RegisterSourceOutput(allServiceTypes, static (spc, serviceType) =>
        {
            // 检查类型是否为 INamedTypeSymbol
            if (serviceType is INamedTypeSymbol namedType)
            {
                GenerateServiceClient(spc, namedType);
            }
            else
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "PRPC101",
                        "无效的服务类型",
                        $"无法为类型 {serviceType} 生成客户端代理，因为它不是 INamedTypeSymbol",
                        "PulseRPC",
                        DiagnosticSeverity.Error,
                        true),
                    Location.None));
            }
        });
    }

    private static INamedTypeSymbol? GetServiceTypeFromInterface(GeneratorSyntaxContext context)
    {
        var typeDeclaration = (TypeDeclarationSyntax)context.Node;

        // 添加更多的验证
        if (!typeDeclaration.AttributeLists.Any())
            return null;

        var symbol = context.SemanticModel.GetDeclaredSymbol(typeDeclaration);
        if (symbol is not INamedTypeSymbol typeSymbol)
            return null;

        // 验证是否实现了正确的接口
        if (!IsStreamingHub(typeSymbol))
            return null;

        return typeSymbol;
    }

    private static IEnumerable<INamedTypeSymbol> GetServiceTypesFromAttribute(
        ClassDeclarationSyntax syntax,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            yield break;

        var model = compilation.GetSemanticModel(syntax.SyntaxTree);
        var symbol = model.GetDeclaredSymbol(syntax, cancellationToken) as INamedTypeSymbol;

        if (symbol is null)
            yield break;

        // 检查类上的 PulseClientGenerationAttribute 属性
        foreach (var attr in symbol.GetAttributes())
        {
            if (attr.AttributeClass?.Name != PulseClientGenerationAttributeName)
                continue;

            if (attr.ConstructorArguments.Length == 0)
                continue;

            if (attr.ConstructorArguments[0].Value is not INamedTypeSymbol markerType)
                continue;

            // 验证是否是 IStreamingHub 接口
            if (!IsStreamingHub(markerType))
                continue;

            yield return markerType;
        }
    }

    private static bool IsStreamingHub(INamedTypeSymbol symbol)
    {
        // 检查是否实现了 IStreamingHub<T> 接口
        foreach (var intf in symbol.AllInterfaces)
        {
            if (intf.IsGenericType && intf.ConstructedFrom.ToDisplayString().StartsWith("PulseRPC.IStreamingHub<"))
            {
                return true;
            }
        }

        // 检查是否自身就是 IStreamingHub<T>
        if (symbol.IsGenericType && symbol.ConstructedFrom.ToDisplayString().StartsWith("PulseRPC.IStreamingHub<"))
        {
            return true;
        }

        // 检查接口名称是否以 StreamingHub 结尾
        if (symbol.TypeKind == TypeKind.Interface && 
            (symbol.Name.EndsWith("StreamingHub") || symbol.Name.EndsWith("Hub")))
        {
            return true;
        }

        return false;
    }

    private static void GenerateServiceClient(SourceProductionContext context, INamedTypeSymbol serviceType)
    {
        try
        {
            // 添加诊断信息，帮助调试
            context.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor(
                    "PRPC200",
                    "生成StreamingHub客户端",
                    $"正在为类型 {serviceType.ToDisplayString()} 生成客户端代理",
                    "PulseRPC",
                    DiagnosticSeverity.Info,
                    true),
                Location.None));

            var source = GenerateStreamingHubClient(serviceType);
            context.AddSource($"{serviceType.Name}Client.g.cs", source);
        }
        catch (Exception ex)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor(
                    "PRPC100",
                    "生成StreamingHub客户端代理失败",
                    $"生成类型 {serviceType.Name} 的代理时发生错误: {ex.Message}",
                    "PulseRPC",
                    DiagnosticSeverity.Error,
                    true),
                Location.None));
        }
    }

    private static string GenerateStreamingHubClient(INamedTypeSymbol hubType)
    {
        var namespaceName = hubType.ContainingNamespace.ToDisplayString();
        var hubName = hubType.Name;
        var clientClassName = $"{hubName}Client";

        var sb = new StringBuilder();

        // 添加命名空间
        sb.AppendLine("// <auto-generated>");
        sb.AppendLine("// 此代码由PulseRPC.Client.SourceGenerator生成，请勿手动修改");
        sb.AppendLine("// </auto-generated>");
        sb.AppendLine();

        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using PulseRPC;");
        sb.AppendLine("using PulseRPC.Client;");
        sb.AppendLine("using MemoryPack;");
        sb.AppendLine("using static MemoryPack.MemoryPackSerializerOptions;");
        sb.AppendLine();

        sb.AppendLine($"namespace {namespaceName}");
        sb.AppendLine("{");

        // 生成客户端代理类
        sb.AppendLine($"    public class {clientClassName} : {hubType.ToDisplayString()}");
        sb.AppendLine("    {");

        // 添加字段
        sb.AppendLine("        private readonly NetworkClient _client;");
        sb.AppendLine("        private readonly CancellationToken _defaultCancellationToken;");
        sb.AppendLine("        private readonly DateTime _defaultDeadline;");
        sb.AppendLine("        private readonly string _host;");
        sb.AppendLine();

        // 添加构造函数
        sb.AppendLine($"        public {clientClassName}(NetworkClient client)");
        sb.AppendLine("        {");
        sb.AppendLine("            _client = client;");
        sb.AppendLine("            _defaultCancellationToken = CancellationToken.None;");
        sb.AppendLine("            _defaultDeadline = DateTime.MaxValue;");
        sb.AppendLine("            _host = string.Empty;");
        sb.AppendLine("        }");
        sb.AppendLine();

        // 为每个方法生成空请求和单参数请求类的声明
        var requestClassDefinitions = new HashSet<string>();

        // 收集所有需要实现的方法（包括接口中定义的方法）
        var methodsToImplement = new List<IMethodSymbol>();
        
        // 如果本身是接口，收集所有方法
        if (hubType.TypeKind == TypeKind.Interface)
        {
            // 直接在接口上定义的方法
            methodsToImplement.AddRange(hubType.GetMembers().OfType<IMethodSymbol>()
                .Where(m => m.Name != "WithDeadline" && m.Name != "WithCancellationToken" && m.Name != "WithHost"));
            
            // 包括所有继承接口的方法
            foreach (var baseInterface in hubType.AllInterfaces)
            {
                if (baseInterface.Name == "IStreamingHub")
                    continue; // 跳过框架接口方法

                methodsToImplement.AddRange(baseInterface.GetMembers().OfType<IMethodSymbol>());
            }
        }
        else
        {
            // 非接口类型，获取自身的方法
            methodsToImplement.AddRange(hubType.GetMembers().OfType<IMethodSymbol>()
                .Where(m => !m.IsStatic && m.Name != "WithDeadline" && m.Name != "WithCancellationToken" && m.Name != "WithHost"));
        }

        // 去重
        var distinctMethods = methodsToImplement.Distinct(SymbolEqualityComparer.Default).ToList();

        // 实现接口方法
        foreach (var method in distinctMethods)
        {
            // 确保是方法符号
            if (method is not IMethodSymbol methodSymbol)
                continue;
                
            var returnType = methodSymbol.ReturnType.ToString();
            var parameters = string.Join(", ", methodSymbol.Parameters.Select(p => $"{p.Type} {p.Name}"));
            var parameterNames = string.Join(", ", methodSymbol.Parameters.Select(p => p.Name));

            // 为0或1个参数的方法准备请求类名
            string requestClassName = $"{methodSymbol.ContainingType.Name}_{methodSymbol.Name}_Request";
            requestClassDefinitions.Add(requestClassName);

            sb.AppendLine($"        public {returnType} {methodSymbol.Name}({parameters})");
            sb.AppendLine("        {");

            if (methodSymbol.Parameters.Length == 1 && methodSymbol.Parameters[0].Type.GetMembers().OfType<IPropertySymbol>().Any())
            {
                // 有一个参数，直接用SendRequestAsync
                sb.AppendLine(
                    $"            return _client.SendRequestAsync<{methodSymbol.Parameters[0].Type}, {methodSymbol.ReturnType.ToString()?.Replace("System.Threading.Tasks.Task<", "").Replace(">", "")}>(");
                sb.AppendLine($"                {methodSymbol.Parameters[0].Name}, _defaultCancellationToken);");
            }
            else if (methodSymbol.Parameters.Length > 0)
            {
                // 多个参数，先创建请求对象
                var responseType = methodSymbol.ReturnType.ToString()
                    ?.Replace("System.Threading.Tasks.Task<", "")
                    .Replace(">", "");
                sb.AppendLine($"            // 创建请求参数包装");
                sb.AppendLine($"            var request = new {requestClassName}");
                sb.AppendLine("            {");

                foreach (var param in methodSymbol.Parameters)
                {
                    sb.AppendLine($"                {param.Name.ToPascalCase()} = {param.Name},");
                }

                sb.AppendLine("            };");
                sb.AppendLine();
                sb.AppendLine(
                    $"            return _client.SendRequestAsync<{requestClassName}, {responseType}>(");
                sb.AppendLine("                request, _defaultCancellationToken);");
            }
            else
            {
                // 没有参数，创建空请求
                var responseType = methodSymbol.ReturnType.ToString()
                    ?.Replace("System.Threading.Tasks.Task<", "")
                    .Replace(">", "");
                sb.AppendLine($"            // 创建空请求");
                sb.AppendLine($"            var request = new {requestClassName}();");
                sb.AppendLine();
                sb.AppendLine(
                    $"            return _client.SendRequestAsync<{requestClassName}, {responseType}>(");
                sb.AppendLine("                request, _defaultCancellationToken);");
            }

            sb.AppendLine("        }");
            sb.AppendLine();
        }

        // 实现IStreamingHub接口方法
        sb.AppendLine($"        public {hubType.ToDisplayString()} WithDeadline(DateTime deadline)");
        sb.AppendLine("        {");
        sb.AppendLine("            return this;");
        sb.AppendLine("        }");
        sb.AppendLine();

        sb.AppendLine(
            $"        public {hubType.ToDisplayString()} WithCancellationToken(CancellationToken cancellationToken)");
        sb.AppendLine("        {");
        sb.AppendLine("            return this;");
        sb.AppendLine("        }");
        sb.AppendLine();

        sb.AppendLine($"        public {hubType.ToDisplayString()} WithHost(string host)");
        sb.AppendLine("        {");
        sb.AppendLine("            return this;");
        sb.AppendLine("        }");

        sb.AppendLine("    }");

        // 为每个方法生成请求类
        foreach (var method in distinctMethods)
        {
            // 确保是方法符号
            if (method is not IMethodSymbol methodSymbol)
                continue;
                
            if (methodSymbol.Name == "WithDeadline" || methodSymbol.Name == "WithCancellationToken" || methodSymbol.Name == "WithHost")
                continue;

            var requestClassName = $"{methodSymbol.ContainingType.Name}_{methodSymbol.Name}_Request";

            if (methodSymbol.Parameters.Length == 0)
            {
                // 生成空请求类
                sb.AppendLine();
                sb.AppendLine($"    [MemoryPackable]");
                sb.AppendLine($"    public partial class {requestClassName}");
                sb.AppendLine("    {");
                sb.AppendLine("        // 空请求");
                sb.AppendLine("    }");
            }
            else if (methodSymbol.Parameters.Length == 1 && methodSymbol.Parameters[0].Type.GetMembers().OfType<IPropertySymbol>().Any())
            {
                // 对于单参数方法，如果原始参数类型不是IMemoryPackable，生成封装类
                var paramType = methodSymbol.Parameters[0].Type;
                var isMemoryPackable = false;

                // 检查参数类型是否实现IMemoryPackable
                foreach (var iface in paramType.AllInterfaces)
                {
                    if (iface.IsGenericType &&
                        iface.ConstructedFrom.ToDisplayString() == "MemoryPack.IMemoryPackable<T>" &&
                        iface.TypeArguments[0].Equals(paramType))
                    {
                        isMemoryPackable = true;
                        break;
                    }
                }

                if (!isMemoryPackable)
                {
                    // 生成包装类
                    sb.AppendLine();
                    sb.AppendLine($"    [MemoryPackable]");
                    sb.AppendLine($"    public partial class {requestClassName}");
                    sb.AppendLine("    {");
                    sb.AppendLine($"        public {paramType} Value {{ get; set; }}");
                    sb.AppendLine("    }");
                }
            }
            else if (methodSymbol.Parameters.Length > 1)
            {
                // 多参数方法的请求类
                sb.AppendLine();
                sb.AppendLine($"    [MemoryPackable]");
                sb.AppendLine($"    public partial class {requestClassName}");
                sb.AppendLine("    {");

                foreach (var param in methodSymbol.Parameters)
                {
                    var propertyName = param.Name.ToPascalCase();
                    sb.AppendLine($"        public {param.Type} {propertyName} {{ get; set; }}");
                }

                sb.AppendLine("    }");
            }
        }

        sb.AppendLine("}");

        return sb.ToString();
    }
}

public static class StringExtensions
{
    public static string ToPascalCase(this string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        return char.ToUpperInvariant(value[0]) + value.Substring(1);
    }
}
