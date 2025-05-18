using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace PulseRPC.Client.SourceGenerator;

/// <summary>
/// 临时对象MemoryPack格式化器生成器 - 专门为增量生成过程中创建的临时对象生成序列化实现
/// </summary>
[Generator]
public class TemporaryObjectMemoryPackFormatter : IIncrementalGenerator
{
    // 临时对象的命名模式
    private static readonly string[] TemporaryObjectPatterns = new[]
    {
        "_Request",
        "_Response"
    };

    private const string EnableMemoryPackGeneratorPropertyName = "PulseRPC_EnableMemoryPackGenerator";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 获取编译选项，检查是否启用了生成器
        IncrementalValueProvider<bool> isGeneratorEnabled = context.AnalyzerConfigOptionsProvider
            .Select((provider, _) =>
            {
                provider.GlobalOptions.TryGetValue($"build_property.{EnableMemoryPackGeneratorPropertyName}", out var enabledValue);
                return string.Equals(enabledValue, "true", StringComparison.OrdinalIgnoreCase);
            });

        // 创建一个管道用于监控代码生成过程中创建的新类型
        IncrementalValuesProvider<INamedTypeSymbol> temporaryObjects = context.CompilationProvider
            .SelectMany((compilation, _) => FindTemporaryObjects(compilation));

        // 将配置与临时对象结合
        IncrementalValuesProvider<INamedTypeSymbol> filteredObjects = isGeneratorEnabled.Combine(temporaryObjects.Collect())
            .SelectMany<(bool Left, ImmutableArray<INamedTypeSymbol> Right), INamedTypeSymbol>((pair, _) =>
            {
                if (!pair.Left)
                    return Array.Empty<INamedTypeSymbol>();

                return pair.Right;
            });

        // 注册源代码输出
        context.RegisterSourceOutput(
            filteredObjects,
            static (spc, type) => GenerateMemoryPackFormatter(spc, type));
    }

    /// <summary>
    /// 在编译中查找所有临时对象
    /// </summary>
    private static IEnumerable<INamedTypeSymbol> FindTemporaryObjects(Compilation compilation)
    {
        var allTypes = new List<INamedTypeSymbol>();

        // 递归查找所有类型
        void CollectTypes(INamespaceSymbol ns)
        {
            foreach (var member in ns.GetMembers())
            {
                if (member is INamespaceSymbol nestedNs)
                {
                    CollectTypes(nestedNs);
                }
                else if (member is INamedTypeSymbol type)
                {
                    // 检查类型名称是否符合临时对象模式
                    if (IsTemporaryObject(type))
                    {
                        allTypes.Add(type);
                    }

                    // 递归查找嵌套类型
                    foreach (var nestedType in type.GetTypeMembers())
                    {
                        if (IsTemporaryObject(nestedType))
                        {
                            allTypes.Add(nestedType);
                        }
                    }
                }
            }
        }

        // 从全局命名空间开始查找
        CollectTypes(compilation.GlobalNamespace);
        return allTypes;
    }

    /// <summary>
    /// 判断类型是否为临时对象
    /// </summary>
    private static bool IsTemporaryObject(INamedTypeSymbol type)
    {
        // 检查类型名称是否符合临时对象模式
        bool matchesPattern = TemporaryObjectPatterns.Any(pattern => type.Name.EndsWith(pattern));
        if (!matchesPattern)
            return false;

        // 检查是否有[MemoryPackable]特性或已经实现了IMemoryPackable接口
        bool hasMemoryPackableAttribute = type.GetAttributes().Any(attr =>
            attr.AttributeClass?.Name == "MemoryPackableAttribute" ||
            attr.AttributeClass?.ToDisplayString().Contains("MemoryPack.MemoryPackableAttribute") == true);

        // 检查是否实现了IMemoryPackable接口
        bool implementsIMemoryPackable = type.AllInterfaces.Any(i =>
            i.Name == "IMemoryPackable" ||
            i.ToDisplayString().Contains("MemoryPack.IMemoryPackable"));

        // 只处理标记了[MemoryPackable]但没有实现IMemoryPackable的临时对象
        return hasMemoryPackableAttribute && !implementsIMemoryPackable;
    }

    /// <summary>
    /// 生成MemoryPack格式化器
    /// </summary>
    private static void GenerateMemoryPackFormatter(SourceProductionContext context, INamedTypeSymbol typeSymbol)
    {
        try
        {
            var typeName = typeSymbol.Name;
            var typeNamespace = typeSymbol.ContainingNamespace.ToDisplayString();
            var typeKind = typeSymbol.TypeKind == TypeKind.Class ? "class" : "struct";
            var fullTypeName = typeSymbol.ToDisplayString();

            // 获取所有公共属性
            var properties = GetSerializableProperties(typeSymbol);

            // 生成格式化器代码
            var formatterCode = GenerateFormatterCode(typeNamespace, typeName, typeKind, fullTypeName, properties);

            // 添加生成的源码
            context.AddSource($"{typeName}Formatter.g.cs", SourceText.From(formatterCode, Encoding.UTF8));

            // 添加诊断信息，方便调试
            context.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor(
                    "PRPC500",
                    "临时对象MemoryPack序列化器生成",
                    $"已为临时对象 {fullTypeName} 生成MemoryPack序列化器",
                    "PulseRPC",
                    DiagnosticSeverity.Info,
                    true),
                Location.None));
        }
        catch (Exception ex)
        {
            // 生成失败时报告诊断信息
            context.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor(
                    "PRPC501",
                    "临时对象MemoryPack序列化器生成失败",
                    $"为临时对象 {typeSymbol.ToDisplayString()} 生成序列化器时发生错误: {ex.Message}",
                    "PulseRPC",
                    DiagnosticSeverity.Error,
                    true),
                Location.None));
        }
    }

    /// <summary>
    /// 获取可序列化的属性
    /// </summary>
    private static List<IPropertySymbol> GetSerializableProperties(INamedTypeSymbol typeSymbol)
    {
        // 收集所有公共属性，且有getter和setter
        return typeSymbol.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(prop =>
                prop.DeclaredAccessibility == Accessibility.Public &&
                prop.GetMethod != null &&
                prop.SetMethod != null &&
                !prop.IsStatic)
            .ToList();
    }

    /// <summary>
    /// 生成格式化器代码
    /// </summary>
    private static string GenerateFormatterCode(
        string typeNamespace,
        string typeName,
        string typeKind,
        string fullTypeName,
        List<IPropertySymbol> properties)
    {
        var sb = new StringBuilder();

        sb.AppendLine("// <auto-generated>");
        sb.AppendLine("// 此代码由PulseRPC.Client.SourceGenerator自动生成");
        sb.AppendLine("// </auto-generated>");
        sb.AppendLine();

        sb.AppendLine("using System;");
        sb.AppendLine("using System.Buffers;");
        sb.AppendLine("using MemoryPack;");
        sb.AppendLine("using MemoryPack.Formatters;");
        sb.AppendLine("using MemoryPack.Internal;");
        sb.AppendLine();

        // 原始类型的命名空间
        sb.AppendLine($"namespace {typeNamespace}");
        sb.AppendLine("{");

        // 为了避免部分类冲突，我们生成一个接口实现
        sb.AppendLine($"    // 为临时对象 {typeName} 实现IMemoryPackable接口");
        sb.AppendLine($"    public partial {typeKind} {typeName} : IMemoryPackable<{typeName}>");
        sb.AppendLine("    {");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine();

        // 格式化器命名空间
        sb.AppendLine("namespace MemoryPack.Formatters");
        sb.AppendLine("{");

        // 生成格式化器类
        sb.AppendLine($"    public sealed class {typeName}Formatter : MemoryPackFormatter<{fullTypeName}>");
        sb.AppendLine("    {");

        // Serialize方法
        sb.AppendLine($"        public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref {fullTypeName}? value)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (value == null)");
        sb.AppendLine("            {");
        sb.AppendLine("                writer.WriteNullObjectHeader();");
        sb.AppendLine("                return;");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine($"            // 写入总共 {properties.Count} 个成员的头部");
        sb.AppendLine($"            writer.WriteObjectHeader({properties.Count});");
        sb.AppendLine();

        // 写入所有属性
        foreach (var property in properties)
        {
            var propertyType = property.Type.ToDisplayString();
            var propertyName = property.Name;

            sb.AppendLine($"            // 序列化 {propertyName}");
            sb.AppendLine($"            writer.Write(value.{propertyName});");
        }

        sb.AppendLine("        }");
        sb.AppendLine();

        // Deserialize方法
        sb.AppendLine($"        public override void Deserialize(ref MemoryPackReader reader, scoped ref {fullTypeName}? value)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (reader.TryReadNil())");
        sb.AppendLine("            {");
        sb.AppendLine("                value = null;");
        sb.AppendLine("                return;");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine($"            // 读取对象头部，预期有 {properties.Count} 个成员");
        sb.AppendLine($"            var count = reader.ReadObjectHeader();");
        sb.AppendLine($"            if (count != {properties.Count})");
        sb.AppendLine("            {");
        sb.AppendLine($"                throw new MemoryPackSerializationException($\"标记了 {properties.Count} 个成员，但读取到 {{count}} 个成员\");");
        sb.AppendLine("            }");
        sb.AppendLine();

        // 创建新实例
        sb.AppendLine("            // 创建实例");
        if (typeKind == "class")
        {
            sb.AppendLine($"            value ??= new {fullTypeName}();");
        }
        else
        {
            sb.AppendLine($"            var result = new {fullTypeName}();");
        }
        sb.AppendLine();

        // 读取所有属性
        foreach (var property in properties)
        {
            var propertyType = property.Type.ToDisplayString();
            var propertyName = property.Name;

            sb.AppendLine($"            // 反序列化 {propertyName}");
            if (typeKind == "class")
            {
                sb.AppendLine($"            value.{propertyName} = reader.Read<{propertyType}>();");
            }
            else
            {
                sb.AppendLine($"            result.{propertyName} = reader.Read<{propertyType}>();");
            }
        }

        // 如果是结构体，需要额外的赋值
        if (typeKind == "struct")
        {
            sb.AppendLine();
            sb.AppendLine("            // 赋值结构体");
            sb.AppendLine("            value = result;");
        }

        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }
}
