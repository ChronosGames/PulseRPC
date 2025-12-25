using System;
using System.Threading;
using System.Threading.Tasks;
using PulseRPC.Server.Contexts;
using PulseRPC.Transport;

namespace PulseRPC.Server;

/// <summary>
/// 消息类型枚举
/// </summary>
public enum ActorMessageType
{
    /// <summary>方法调用消息</summary>
    MethodInvocation,

    /// <summary>定时器消息</summary>
    Timer,

    /// <summary>系统消息</summary>
    System
}

/// <summary>
/// 带认证上下文的服务消息基类
/// </summary>
public abstract class ServiceMessage
{
    public Guid MessageId { get; } = Guid.NewGuid();
    public ActorMessageType Type { get; init; }
    public DateTime EnqueueTime { get; } = DateTime.UtcNow;
    public CancellationToken CancellationToken { get; init; }

    /// <summary>认证上下文</summary>
    public IServiceRequestContext? AuthContext { get; set; }

    /// <summary>发送者连接（用于 RequestContext）</summary>
    public IServerTransport? Sender { get; set; }

    /// <summary>消息优先级（默认为 Normal）</summary>
    public PulseRPC.MessagePriority Priority { get; set; } = PulseRPC.MessagePriority.Normal;
}

/// <summary>
/// 方法调用消息（带认证）
/// </summary>
public class MethodInvocationMessage : ServiceMessage
{
    /// <summary>
    /// 协议号 - 用于路由到具体方法
    /// </summary>
    public PulseRPC.Protocol.ProtocolId ProtocolId { get; set; }

    /// <summary>
    /// 方法参数
    /// </summary>
    public object?[] Arguments { get; set; } = Array.Empty<object?>();

    /// <summary>
    /// 返回值类型（可选）
    /// </summary>
    public Type? ReturnType { get; set; }

    /// <summary>
    /// 完成源 - 用于异步返回结果
    /// </summary>
    public TaskCompletionSource<object?> CompletionSource { get; } = new();

    public MethodInvocationMessage()
    {
        Type = ActorMessageType.MethodInvocation;
    }
}
