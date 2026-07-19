using PulseRPC.Server.Contexts;

namespace PulseRPC.Server.Gateway;

/// <summary>Gateway Actor 调用类型。</summary>
public enum GatewayActorInvocationKind : byte
{
    /// <summary>请求/响应调用。</summary>
    Ask = 0,

    /// <summary>单向发送调用。</summary>
    Send = 1,
}

/// <summary>Gateway 在把外部客户端调用转发到目标 Actor 前提供给准入策略的只读上下文。</summary>
public sealed class GatewayActorInvocationContext
{
    /// <summary>创建一次 Gateway Actor 调用上下文。</summary>
    public GatewayActorInvocationContext(
        string hub,
        string key,
        ushort protocolId,
        GatewayActorInvocationKind invocationKind,
        IPulseContext callerContext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hub);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(callerContext);

        Hub = hub;
        Key = key;
        ProtocolId = protocolId;
        InvocationKind = invocationKind;
        CallerContext = callerContext;
    }

    /// <summary>目标 Hub 的 canonical 名称。</summary>
    public string Hub { get; }

    /// <summary>目标 Actor 实例键。</summary>
    public string Key { get; }

    /// <summary>目标方法协议号。</summary>
    public ushort ProtocolId { get; }

    /// <summary>调用类型。</summary>
    public GatewayActorInvocationKind InvocationKind { get; }

    /// <summary>当前外部调用者上下文。</summary>
    public IPulseContext CallerContext { get; }
}

/// <summary>
/// Gateway Actor 调用准入策略。在外部客户端调用进入集群路由前执行，可用于资源所有权、会话及限流校验。
/// </summary>
/// <remarks>策略按依赖注入注册顺序执行；拒绝调用时应抛出异常。</remarks>
public interface IGatewayActorInvocationPolicy
{
    /// <summary>验证是否允许当前调用进入 Actor 路由。</summary>
    ValueTask EvaluateAsync(
        GatewayActorInvocationContext context,
        CancellationToken cancellationToken = default);
}
