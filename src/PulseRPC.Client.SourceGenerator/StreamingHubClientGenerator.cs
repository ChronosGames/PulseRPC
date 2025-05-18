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
        sb.AppendLine("using static MemoryPack.MemoryPackFormatterProvider;");
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
        var responseClassDefinitions = new HashSet<string>();

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

            // 为不同类型的参数和返回值准备请求和响应类名
            string requestClassName = $"{methodSymbol.ContainingType.Name}_{methodSymbol.Name}_Request";
            string responseClassName = $"{methodSymbol.ContainingType.Name}_{methodSymbol.Name}_Response";
            requestClassDefinitions.Add(requestClassName);

            // 如果返回值类型需要包装器，添加响应类定义
            var returnTypeSymbol = ExtractTaskResultTypeSymbol(methodSymbol.ReturnType);
            if (!IsMemoryPackableType(returnTypeSymbol))
            {
                responseClassDefinitions.Add(responseClassName);
            }

            // 生成方法实现
            sb.AppendLine($"        public async {returnType} {methodSymbol.Name}({parameters})");
            sb.AppendLine("        {");

            if (methodSymbol.Parameters.Length == 1)
            {
                var paramType = methodSymbol.Parameters[0].Type;
                bool needsWrapper = !IsMemoryPackableType(paramType);

                bool responseNeedsWrapper = !IsMemoryPackableType(returnTypeSymbol);

                if (needsWrapper)
                {
                    // 有一个参数，使用包装类发送
                    sb.AppendLine($"            // 创建包装请求");
                    sb.AppendLine($"            var request = new {requestClassName}");
                    sb.AppendLine("            {");
                    sb.AppendLine($"                Value = {methodSymbol.Parameters[0].Name}");
                    sb.AppendLine("            };");
                    sb.AppendLine();

                    if (responseNeedsWrapper)
                    {
                        // 生成结果解包逻辑
                        sb.AppendLine($"            var response = await _client.SendRequestAsync<{requestClassName}, {responseClassName}>(");
                        sb.AppendLine("                request, _defaultCancellationToken);");
                        sb.AppendLine($"            return response.Value;");
                    }
                    else
                    {
                        sb.AppendLine($"            return await _client.SendRequestAsync<{requestClassName}, {returnTypeSymbol.ToDisplayString()}>(");
                        sb.AppendLine("                request, _defaultCancellationToken);");
                    }
                }
                else
                {
                    // 有一个MemoryPackable参数，直接用SendRequestAsync
                    if (responseNeedsWrapper)
                    {
                        sb.AppendLine($"            var response = await _client.SendRequestAsync<{paramType.ToDisplayString()}, {responseClassName}>(");
                        sb.AppendLine($"                {methodSymbol.Parameters[0].Name}, _defaultCancellationToken);");
                        sb.AppendLine($"            return response.Value;");
                    }
                    else
                    {
                        sb.AppendLine($"            return await _client.SendRequestAsync<{paramType.ToDisplayString()}, {returnTypeSymbol.ToDisplayString()}>(");
                        sb.AppendLine($"                {methodSymbol.Parameters[0].Name}, _defaultCancellationToken);");
                    }
                }
            }
            else if (methodSymbol.Parameters.Length > 0)
            {
                // 多个参数，先创建请求对象
                bool responseNeedsWrapper = !IsMemoryPackableType(returnTypeSymbol);

                sb.AppendLine($"            // 创建请求参数包装");
                sb.AppendLine($"            var request = new {requestClassName}");
                sb.AppendLine("            {");

                foreach (var param in methodSymbol.Parameters)
                {
                    sb.AppendLine($"                {param.Name.ToPascalCase()} = {param.Name},");
                }

                sb.AppendLine("            };");
                sb.AppendLine();

                if (!responseNeedsWrapper)
                {
                    sb.AppendLine($"            return await _client.SendRequestAsync<{requestClassName}, {returnTypeSymbol.ToDisplayString()}>(");
                    sb.AppendLine("                request, _defaultCancellationToken);");
                }
                else
                {
                    sb.AppendLine($"            var response = await _client.SendRequestAsync<{requestClassName}, {responseClassName}>(");
                    sb.AppendLine("                request, _defaultCancellationToken);");
                    sb.AppendLine($"            return response.Value;");
                }
            }
            else
            {
                // 没有参数，创建空请求
                bool responseNeedsWrapper = !IsMemoryPackableType(returnTypeSymbol);

                sb.AppendLine($"            // 创建空请求");
                sb.AppendLine($"            var request = new {requestClassName}();");
                sb.AppendLine();

                if (!responseNeedsWrapper)
                {
                    sb.AppendLine($"            return await _client.SendRequestAsync<{requestClassName}, {returnTypeSymbol.ToDisplayString()}>(");
                    sb.AppendLine("                request, _defaultCancellationToken);");
                }
                else
                {
                    sb.AppendLine($"            var response = await _client.SendRequestAsync<{requestClassName}, {responseClassName}>(");
                    sb.AppendLine("                request, _defaultCancellationToken);");
                    sb.AppendLine($"            return response.Value;");
                }
            }

            sb.AppendLine("        }");
            sb.AppendLine();
        }

        // 实现IStreamingHub接口方法
        sb.AppendLine($"        public {hubType.ToDisplayString()} WithDeadline(DateTime deadline)");
        sb.AppendLine("        {");
        sb.AppendLine("            // 保存新的截止时间");
        sb.AppendLine("            var client = new " + clientClassName + "(_client);");
        sb.AppendLine("            client._defaultDeadline = deadline;");
        sb.AppendLine("            return client;");
        sb.AppendLine("        }");
        sb.AppendLine();

        sb.AppendLine(
            $"        public {hubType.ToDisplayString()} WithCancellationToken(CancellationToken cancellationToken)");
        sb.AppendLine("        {");
        sb.AppendLine("            // 保存新的取消令牌");
        sb.AppendLine("            var client = new " + clientClassName + "(_client);");
        sb.AppendLine("            client._defaultCancellationToken = cancellationToken;");
        sb.AppendLine("            return client;");
        sb.AppendLine("        }");
        sb.AppendLine();

        sb.AppendLine($"        public {hubType.ToDisplayString()} WithHost(string host)");
        sb.AppendLine("        {");
        sb.AppendLine("            // 保存新的主机名");
        sb.AppendLine("            var client = new " + clientClassName + "(_client);");
        sb.AppendLine("            client._host = host;");
        sb.AppendLine("            return client;");
        sb.AppendLine("        }");
        sb.AppendLine();

        sb.AppendLine("    }");

        // 为每个方法生成请求和响应类
        foreach (var method in distinctMethods)
        {
            // 确保是方法符号
            if (method is not IMethodSymbol methodSymbol)
                continue;

            if (methodSymbol.Name == "WithDeadline" || methodSymbol.Name == "WithCancellationToken" || methodSymbol.Name == "WithHost")
                continue;

            var requestClassName = $"{methodSymbol.ContainingType.Name}_{methodSymbol.Name}_Request";
            var responseClassName = $"{methodSymbol.ContainingType.Name}_{methodSymbol.Name}_Response";

            // 1. 生成请求类
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
            else if (methodSymbol.Parameters.Length == 1)
            {
                var paramType = methodSymbol.Parameters[0].Type;
                bool needsWrapper = !IsMemoryPackableType(paramType);

                if (needsWrapper)
                {
                    // 生成包装类
                    sb.AppendLine();
                    sb.AppendLine($"    [MemoryPackable]");
                    sb.AppendLine($"    public partial class {requestClassName}");
                    sb.AppendLine("    {");
                    sb.AppendLine($"        public {paramType.ToDisplayString()} Value {{ get; set; }}");
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
                    sb.AppendLine($"        public {param.Type.ToDisplayString()} {propertyName} {{ get; set; }}");
                }

                sb.AppendLine("    }");
            }

            // 2. 生成响应类（如果需要）
            var returnTypeSymbol = ExtractTaskResultTypeSymbol(methodSymbol.ReturnType);
            if (!IsMemoryPackableType(returnTypeSymbol) &&
                responseClassDefinitions.Contains(responseClassName))
            {
                sb.AppendLine();
                sb.AppendLine($"    [MemoryPackable]");
                sb.AppendLine($"    public partial class {responseClassName}");
                sb.AppendLine("    {");
                sb.AppendLine($"        public {returnTypeSymbol.ToDisplayString()} Value {{ get; set; }}");
                sb.AppendLine("    }");
            }
        }

        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string ExtractTaskResultType(ITypeSymbol returnType)
    {
        // 从Task<T>中提取T的类型
        if (returnType is INamedTypeSymbol namedType &&
            namedType.IsGenericType &&
            namedType.ConstructedFrom.ToDisplayString() == "System.Threading.Tasks.Task<T>")
        {
            return namedType.TypeArguments[0].ToDisplayString();
        }

        // 对于非Task类型，直接返回类型名
        return returnType.ToDisplayString();
    }

    private static ITypeSymbol ExtractTaskResultTypeSymbol(ITypeSymbol returnType)
    {
        // 从Task<T>中提取T的类型
        if (returnType is INamedTypeSymbol namedType &&
            namedType.IsGenericType &&
            namedType.ConstructedFrom.ToDisplayString() == "System.Threading.Tasks.Task<T>")
        {
            return namedType.TypeArguments[0];
        }

        // 对于非Task类型，直接返回类型
        return returnType;
    }

    private static bool IsMemoryPackableType(ITypeSymbol type)
    {
        // 任务类型不能直接作为MemoryPackable类型
        if (type is INamedTypeSymbol namedReturnType &&
            namedReturnType.IsGenericType &&
            namedReturnType.ConstructedFrom.ToDisplayString() == "System.Threading.Tasks.Task<T>")
        {
            return false;
        }

        // 检查是否已经实现IMemoryPackable<T>
        foreach (var iface in type.AllInterfaces)
        {
            if (iface.IsGenericType &&
                iface.ConstructedFrom.ToDisplayString() == "MemoryPack.IMemoryPackable<T>" &&
                iface.TypeArguments[0].Equals(type))
            {
                return true;
            }
        }

        // 是否有[MemoryPackable]特性
        if (type.GetAttributes().Any(attr => attr.AttributeClass?.Name == "MemoryPackableAttribute"))
        {
            return true;
        }

        // 基本类型需要包装
        if (type.IsValueType || type.SpecialType == SpecialType.System_String)
        {
            return false;
        }

        // 是否是元组（需要包装）
        if (type is INamedTypeSymbol namedType && namedType.IsTupleType)
        {
            return false;
        }

        // 集合类型需要包装
        if (type is IArrayTypeSymbol ||
            (type is INamedTypeSymbol collectionType &&
             (ImplementsInterface(collectionType, "System.Collections.Generic.IEnumerable`1") ||
              ImplementsInterface(collectionType, "System.Collections.Generic.ICollection`1") ||
              ImplementsInterface(collectionType, "System.Collections.Generic.IList`1") ||
              ImplementsInterface(collectionType, "System.Collections.Generic.IDictionary`2"))))
        {
            return false;
        }

        // 默认假设其他复杂类型需要包装
        return false;
    }

    private static bool ImplementsInterface(INamedTypeSymbol type, string interfaceName)
    {
        return type.AllInterfaces.Any(i => i.IsGenericType && i.ConstructedFrom.ToDisplayString() == interfaceName);
    }

    private static bool IsMemoryPackableType(string typeName)
    {
        // 这个简化版本是为了在我们只有类型名而没有ITypeSymbol时使用
        return !(
            typeName == "int" ||
            typeName == "long" ||
            typeName == "float" ||
            typeName == "double" ||
            typeName == "bool" ||
            typeName == "string" ||
            typeName.Contains("ValueTuple<") ||
            (typeName.StartsWith("(") && typeName.Contains(","))
        );
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
