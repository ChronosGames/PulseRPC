using Microsoft.CodeAnalysis;

namespace PulseRPC.Client.SourceGenerator;

/// <summary>
/// 抽象消息语法接收器
/// </summary>
public abstract class AbstractMessageSyntaxReceiver : ISyntaxContextReceiver
{
    /// <summary>
    /// 访问语法节点
    /// </summary>
    /// <param name="context">语法上下文</param>
    public abstract void OnVisitSyntaxNode(GeneratorSyntaxContext context);
}
