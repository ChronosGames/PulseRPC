using System;
using System.Threading;
using System.Threading.Tasks;
using PulseRPC.Server.Contexts;
using PulseRPC.Shared;

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
