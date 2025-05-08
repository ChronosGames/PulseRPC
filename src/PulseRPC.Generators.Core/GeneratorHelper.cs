using System.Text;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace PulseRPC.Generators.Core;

/// <summary>
/// 代码生成器辅助类
/// </summary>
public static class GeneratorHelper
{
    /// <summary>
    /// 获取类型的完全限定名
    /// </summary>
    /// <param name="typeSymbol">类型符号</param>
    /// <returns>完全限定名</returns>
    public static string GetFullyQualifiedName(ITypeSymbol typeSymbol)
    {
        return typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    /// <summary>
    /// 添加源代码到生成器上下文
    /// </summary>
    /// <param name="context">生成器上下文</param>
    /// <param name="fileName">文件名</param>
    /// <param name="sourceCode">源代码</param>
    public static void AddSourceCode(GeneratorExecutionContext context, string fileName, string sourceCode)
    {
        context.AddSource(fileName, SourceText.From(sourceCode, Encoding.UTF8));
    }

    /// <summary>
    /// 生成共享的消息注册表代码
    /// </summary>
    /// <param name="messageTypes">消息类型信息列表</param>
    /// <returns>生成的源代码</returns>
    public static string GenerateMessageRegistryCode<T>(List<T> messageTypes) where T : IMessageTypeInfo
    {
        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine();
        sb.AppendLine("namespace PulseRPC.Protocol.Serialization");
        sb.AppendLine("{");
        sb.AppendLine("    // 自动生成的消息注册表");
        sb.AppendLine("    public static partial class MessageRegistry");
        sb.AppendLine("    {");
        sb.AppendLine("        // 静态构造函数，初始化注册表");
        sb.AppendLine("        static MessageRegistry()");
        sb.AppendLine("        {");

        // 注册所有消息类型
        foreach (var messageType in messageTypes)
        {
            var fullyQualifiedName = GetFullyQualifiedName(messageType.TypeSymbol);
            sb.AppendLine($"            RegisterMessageType<{fullyQualifiedName}>({messageType.MessageId});");
        }

        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }
}
