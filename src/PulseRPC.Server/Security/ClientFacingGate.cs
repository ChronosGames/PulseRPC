using System;
using System.Runtime.CompilerServices;
using PulseRPC.Server.Contexts;

namespace PulseRPC.Server.Security;

/// <summary>
/// P-6：facet 级客户端可见性门闸——一道独立于业务鉴权（<see cref="IAuthorizationService"/> /
/// <see cref="AuthorizeAttribute"/>）的「协议框架」级白名单检查。
/// </summary>
/// <remarks>
/// <para>
/// 源生成器会为每一个协议号方法生成一次 <see cref="Enforce"/> 调用，调用位于
/// <c>ServiceRoutingTable</c> 的协议号路由方法中——这是所有基于协议号的调度共同经过的唯一
/// 入口，因此该检查对业务代码而言是「强制」且不可绕过的：一旦通过 <see cref="EnforcementEnabled"/>
/// 启用门闸，无论方法内部是否实现了 <see cref="AuthorizeAttribute"/> 或
/// <see cref="PermissionValidator"/> 之类的业务鉴权，只要未标注
/// <see cref="PulseRPC.ClientFacingAttribute"/>，就永远无法被外部客户端调用到。
/// </para>
/// <para>
/// 白名单结果（<c>isClientFacing</c>）在编译期由源生成器根据 facet/方法上的
/// <see cref="PulseRPC.ClientFacingAttribute"/> 计算得出，运行时只需判断当前调用是否来自外部
/// 客户端（<see cref="CallSourceType.ExternalUser"/>）；服务间调用、系统调用、管理后台调用均不
/// 受此门闸影响，只由各自的业务鉴权规则约束。
/// </para>
/// <para>
/// <b>向后兼容</b>：<see cref="EnforcementEnabled"/> 默认 <c>false</c>——生成器产出的检查调用始终存在于
/// 生成代码中，但默认不生效，因此现有项目在未主动开启前行为与升级前完全一致。
/// </para>
/// </remarks>
public static class ClientFacingGate
{
    /// <summary>
    /// 是否启用门闸的强制检查。
    /// </summary>
    /// <remarks>
    /// 默认 <c>false</c>，以保持向后兼容——现有项目在未主动开启前，行为与升级前完全一致，
    /// 未标注 <see cref="PulseRPC.ClientFacingAttribute"/> 的方法仍可像以前一样被外部客户端调用。
    /// 通过 <see cref="PulseRPC.Server.Configuration.UnifiedServerOptions.EnableClientFacingGate"/>
    /// 启动服务器即可开启（也可在测试中直接设置本属性）。开启后，只有标注了
    /// <see cref="PulseRPC.ClientFacingAttribute"/> 的方法才允许外部客户端调用。
    /// </remarks>
    public static bool EnforcementEnabled { get; set; }

    /// <summary>
    /// 校验外部客户端是否允许调用目标协议方法。
    /// </summary>
    /// <param name="isClientFacing">
    /// 目标方法是否已被源生成器标记为「客户端可见」（由 facet/方法级 <see cref="PulseRPC.ClientFacingAttribute"/> 解析得出）。
    /// </param>
    /// <param name="protocolId">目标方法的协议号。</param>
    /// <param name="methodDisplayName">用于诊断信息的 "接口.方法" 展示名称。</param>
    /// <exception cref="ClientFacingAccessDeniedException">
    /// 当门闸已启用（<see cref="EnforcementEnabled"/>）、调用来自外部客户端
    /// （<see cref="CallSourceType.ExternalUser"/>）且 <paramref name="isClientFacing"/> 为 <c>false</c> 时抛出。
    /// </exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Enforce(bool isClientFacing, ushort protocolId, string methodDisplayName)
    {
        if (!EnforcementEnabled || isClientFacing)
        {
            return;
        }

        if (PulseContext.Current?.SourceType == CallSourceType.ExternalUser)
        {
            throw new ClientFacingAccessDeniedException(protocolId, methodDisplayName);
        }
    }
}

/// <summary>
/// 当外部客户端尝试调用一个未标注 <see cref="PulseRPC.ClientFacingAttribute"/> 的方法/协议时抛出。
/// </summary>
/// <remarks>
/// 此异常表示「协议框架」级的可见性拒绝，与业务鉴权失败（如权限不足、角色不匹配）语义不同，
/// 调用方应据此区分「这个接口压根不对外」与「你没有权限调用这个接口」两类错误。
/// </remarks>
public sealed class ClientFacingAccessDeniedException : Exception
{
    /// <summary>被拒绝调用的协议号。</summary>
    public ushort ProtocolId { get; }

    /// <summary>被拒绝调用的方法展示名称（"接口.方法"）。</summary>
    public string MethodDisplayName { get; }

    /// <summary>
    /// 创建 <see cref="ClientFacingAccessDeniedException"/>。
    /// </summary>
    /// <param name="protocolId">被拒绝调用的协议号。</param>
    /// <param name="methodDisplayName">被拒绝调用的方法展示名称。</param>
    public ClientFacingAccessDeniedException(ushort protocolId, string methodDisplayName)
        : base(BuildMessage(protocolId, methodDisplayName))
    {
        ProtocolId = protocolId;
        MethodDisplayName = methodDisplayName;
    }

    private static string BuildMessage(ushort protocolId, string methodDisplayName)
    {
        return $"'{methodDisplayName}' (protocol 0x{protocolId:X4}) is not marked [ClientFacing] and cannot be " +
               "invoked by external clients. Mark the facet interface or the method with [ClientFacing] to expose it.";
    }
}
