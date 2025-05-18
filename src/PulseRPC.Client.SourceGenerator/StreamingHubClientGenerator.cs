using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MemoryPack;
using static MemoryPack.GenerateType;

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
        sb.AppendLine("using static MemoryPack.GenerateType;");
        sb.AppendLine();

        sb.AppendLine($"namespace {namespaceName}");
        sb.AppendLine("{");

        // 生成客户端代理类
        sb.AppendLine($"    public class {clientClassName} : {hubType.ToDisplayString()}");
        sb.AppendLine("    {");

        // 添加字段
        sb.AppendLine("        private readonly NetworkClient _client;");
        sb.AppendLine("        private readonly CancellationToken _cancellationToken;");
        sb.AppendLine("        private readonly DateTime _deadline;");
        sb.AppendLine("        private readonly string _host;");
        sb.AppendLine();

        // 添加构造函数
        sb.AppendLine($"        public {clientClassName}(NetworkClient client)");
        sb.AppendLine("        {");
        sb.AppendLine("            _client = client;");
        sb.AppendLine("            _cancellationToken = CancellationToken.None;");
        sb.AppendLine("            _deadline = DateTime.MaxValue;");
        sb.AppendLine("            _host = string.Empty;");
        sb.AppendLine("        }");
        sb.AppendLine();

        // 添加带参数的私有构造函数
        sb.AppendLine($"        private {clientClassName}(NetworkClient client, CancellationToken cancellationToken, DateTime deadline, string host)");
        sb.AppendLine("        {");
        sb.AppendLine("            _client = client;");
        sb.AppendLine("            _cancellationToken = cancellationToken;");
        sb.AppendLine("            _deadline = deadline;");
        sb.AppendLine("            _host = host;");
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
                        sb.AppendLine("                request, _cancellationToken);");
                        sb.AppendLine($"            return response.Value;");
                    }
                    else
                    {
                        sb.AppendLine($"            return await _client.SendRequestAsync<{requestClassName}, {returnTypeSymbol.ToDisplayString()}>(");
                        sb.AppendLine("                request, _cancellationToken);");
                    }
                }
                else
                {
                    // 有一个MemoryPackable参数，直接用SendRequestAsync
                    if (responseNeedsWrapper)
                    {
                        sb.AppendLine($"            var response = await _client.SendRequestAsync<{paramType.ToDisplayString()}, {responseClassName}>(");
                        sb.AppendLine($"                {methodSymbol.Parameters[0].Name}, _cancellationToken);");
                        sb.AppendLine($"            return response.Value;");
                    }
                    else
                    {
                        sb.AppendLine($"            return await _client.SendRequestAsync<{paramType.ToDisplayString()}, {returnTypeSymbol.ToDisplayString()}>(");
                        sb.AppendLine($"                {methodSymbol.Parameters[0].Name}, _cancellationToken);");
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
                    sb.AppendLine("                request, _cancellationToken);");
                }
                else
                {
                    sb.AppendLine($"            var response = await _client.SendRequestAsync<{requestClassName}, {responseClassName}>(");
                    sb.AppendLine("                request, _cancellationToken);");
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
                    sb.AppendLine("                request, _cancellationToken);");
                }
                else
                {
                    sb.AppendLine($"            var response = await _client.SendRequestAsync<{requestClassName}, {responseClassName}>(");
                    sb.AppendLine("                request, _cancellationToken);");
                    sb.AppendLine($"            return response.Value;");
                }
            }

            sb.AppendLine("        }");
            sb.AppendLine();
        }

        // 实现IStreamingHub接口方法
        sb.AppendLine($"        public {hubType.ToDisplayString()} WithDeadline(DateTime deadline)");
        sb.AppendLine("        {");
        sb.AppendLine("            // 返回带有新截止时间的实例");
        sb.AppendLine($"            return new {clientClassName}(_client, _cancellationToken, deadline, _host);");
        sb.AppendLine("        }");
        sb.AppendLine();

        sb.AppendLine(
            $"        public {hubType.ToDisplayString()} WithCancellationToken(CancellationToken cancellationToken)");
        sb.AppendLine("        {");
        sb.AppendLine("            // 返回带有新取消令牌的实例");
        sb.AppendLine($"            return new {clientClassName}(_client, cancellationToken, _deadline, _host);");
        sb.AppendLine("        }");
        sb.AppendLine();

        sb.AppendLine($"        public {hubType.ToDisplayString()} WithHost(string host)");
        sb.AppendLine("        {");
        sb.AppendLine("            // 返回带有新主机名的实例");
        sb.AppendLine($"            return new {clientClassName}(_client, _cancellationToken, _deadline, host);");
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
                sb.AppendLine($"    public partial class {requestClassName} : IMemoryPackable<{requestClassName}>");
                sb.AppendLine("    {");
                sb.AppendLine("        // 空请求");
                sb.AppendLine("    }");
            }
            else if (methodSymbol.Parameters.Length == 1)
            {
                var paramType = methodSymbol.Parameters[0].Type;
                var needsWrapper = !IsMemoryPackableType(paramType);

                if (needsWrapper)
                {
                    // 生成包装类
                    sb.AppendLine();
                    sb.AppendLine($"    [MemoryPackable]");
                    sb.AppendLine($"    public partial class {requestClassName} : IMemoryPackable<{requestClassName}>");
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
                sb.AppendLine($"    public partial class {requestClassName} : IMemoryPackable<{requestClassName}>");
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
            var extractedReturnType = ExtractTaskResultType(methodSymbol.ReturnType);
            if (!IsMemoryPackableType(returnTypeSymbol) &&
                responseClassDefinitions.Contains(responseClassName))
            {
                sb.AppendLine();
                sb.AppendLine($"    [MemoryPackable]");
                sb.AppendLine($"    public partial class {responseClassName} : IMemoryPackable<{responseClassName}>");
                sb.AppendLine("    {");
                sb.AppendLine($"        public {extractedReturnType} Value {{ get; set; }}");
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

    private static bool IsMemoryPackableType(ITypeSymbol? type)
    {
        // 空值不能作为MemoryPackable类型
        if (type == null)
        {
            return false;
        }

        // 任务类型不能直接作为MemoryPackable类型
        if (type.ToString() == "System.Threading.Tasks.Task")
        {
            return false;
        }

        if (type is INamedTypeSymbol namedReturnType &&
            namedReturnType.IsGenericType &&
            namedReturnType.ConstructedFrom.ToDisplayString() == "System.Threading.Tasks.Task<T>")
        {
            return false;
        }

        // 查找是否有MemoryPackable特性名称，而不是检查特定接口实现
        // 这样可以避免时序问题，因为特性名称在编译时就已确定
        bool hasMemoryPackableAttribute = type.GetAttributes().Any(attr =>
            attr.AttributeClass?.Name == "MemoryPackableAttribute" ||
            attr.AttributeClass?.ToDisplayString().Contains("MemoryPack.MemoryPackableAttribute") == true);

        if (hasMemoryPackableAttribute)
        {
            return true;
        }

        // 以下类型需要包装
        if (type.SpecialType == SpecialType.System_Object ||
            type.SpecialType == SpecialType.System_String ||
            type.TypeKind == TypeKind.Enum ||
            IsSimpleValueType(type))
        {
            return false;
        }

        // 查看类型名称，适用于基本类型和常见.NET类型
        string typeName = type.ToDisplayString();

        // 数组、元组和集合类型需要包装
        if (type is IArrayTypeSymbol ||
            (type is INamedTypeSymbol nt && nt.IsTupleType) ||
            typeName.Contains("List<") ||
            typeName.Contains("Dictionary<") ||
            typeName.Contains("IEnumerable<") ||
            typeName.Contains("ICollection<") ||
            typeName.Contains("HashSet<") ||
            typeName.Contains("ConcurrentDictionary<"))
        {
            return false;
        }

        // 假设复杂引用类型可能会添加[MemoryPackable]特性
        // 对于这类型，在代码生成时生成包装器以确保安全
        return false;
    }

    private static bool IsSimpleValueType(ITypeSymbol type)
    {
        return type.IsValueType && type.SpecialType is SpecialType.System_Boolean or SpecialType.System_Byte or SpecialType.System_SByte or SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal or SpecialType.System_DateTime;
    }

    private static bool ImplementsInterface(INamedTypeSymbol type, string interfaceName)
    {
        return type.AllInterfaces.Any(i => i.IsGenericType && i.ConstructedFrom.ToDisplayString() == interfaceName);
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
