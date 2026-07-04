using System;

namespace PulseRPC;

/// <summary>
/// 消息投递保证级别。
/// </summary>
/// <remarks>
/// 通过 <see cref="DeliveryAttribute"/> 在方法/接口上声明，或在调用时按需选择。默认
/// <see cref="AtMostOnce"/>，与既有单节点行为一致。
/// </remarks>
public enum DeliveryMode : byte
{
    /// <summary>
    /// 至多一次：发送后不重试，失败即上抛。零额外开销，适用于可容忍丢弃过期消息的实时场景（默认）。
    /// </summary>
    AtMostOnce = 0,

    /// <summary>
    /// 至少一次：失败/超时重试直至确认；接收方可能重复收到，需自行幂等或容忍重复。
    /// </summary>
    AtLeastOnce = 1,

    /// <summary>
    /// 精确一次（有效一次）：至少一次投递 + 接收方基于 <c>MessageId</c> 去重（有界窗口）达成"效果幂等"。
    /// 非分布式事务；跨越持久化状态的强一致需业务自行保证。
    /// </summary>
    ExactlyOnce = 2,
}

/// <summary>
/// 声明某个 Hub 方法（或接口默认）的投递保证级别。
/// </summary>
/// <remarks>
/// 方法级标注覆盖接口级默认；未标注时使用 <see cref="DeliveryMode.AtMostOnce"/>。
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Interface, AllowMultiple = false)]
public sealed class DeliveryAttribute : Attribute
{
    /// <summary>投递保证级别。</summary>
    public DeliveryMode Mode { get; }

    /// <summary>创建投递保证特性。</summary>
    /// <param name="mode">投递保证级别，默认 <see cref="DeliveryMode.AtMostOnce"/>。</param>
    public DeliveryAttribute(DeliveryMode mode = DeliveryMode.AtMostOnce)
    {
        Mode = mode;
    }
}

/// <summary>
/// 显式声明统一 <see cref="IPulseHub"/> 接口在"本编译侧"要生成哪一类代码，用于覆盖默认的方向推断。
/// </summary>
/// <remarks>
/// <para>
/// 绝大多数接口<strong>无需标注</strong> —— 生成器依据 <see cref="ChannelAttribute"/>（谁提供）与编译上下文
/// （客户端/服务端源生成器）即可推断应生成"调用方代理"还是"被调方骨架"。
/// </para>
/// <para>
/// 仅在<strong>歧义场景</strong>（如服务端 Actor 接口既被调用又要调用同类其它实例，需"两者都要"；
/// 或纯 Shared 双向契约）下用本特性显式覆盖：
/// </para>
/// <list type="bullet">
/// <item><description><see cref="Provide"/> —— 本侧是否生成"被调方骨架"（提供实现，处理入站调用）；</description></item>
/// <item><description><see cref="Consume"/> —— 本侧是否生成"调用方代理"（发起出站调用）。</description></item>
/// </list>
/// </remarks>
[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false)]
public sealed class PulseHubAttribute : Attribute
{
    /// <summary>本编译侧是否为该 Hub 生成"被调方骨架"（提供实现）。默认 <c>true</c>。</summary>
    public bool Provide { get; set; } = true;

    /// <summary>本编译侧是否为该 Hub 生成"调用方代理"（发起调用）。默认 <c>true</c>。</summary>
    public bool Consume { get; set; } = true;
}
