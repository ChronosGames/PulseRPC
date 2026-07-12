using System;
using System.Runtime.CompilerServices;
using System.Threading;
using PulseRPC.Server.Contexts;

namespace PulseRPC.Server.Security;

/// <summary>
/// P-6：facet 级客户端可见性门闸——一道独立于业务鉴权（<see cref="IAuthorizationService"/> /
/// <see cref="AuthorizeAttribute"/>）的「协议框架」级白名单检查。
/// </summary>
/// <remarks>
/// <para>
/// 源生成器会为每一个协议号方法生成一次
/// <see cref="Enforce(IServiceProvider, bool, ushort, string)"/> 调用，调用位于
/// <c>ServiceRoutingTable</c> 的协议号路由方法中——这是所有基于协议号的调度共同经过的唯一
/// 入口，因此该检查对业务代码而言是「强制」且不可绕过的：一旦通过宿主策略
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
/// <b>向后兼容</b>：宿主策略默认关闭；<see cref="EnforcementEnabled"/> 继续供旧生成代码与
/// 直接调用方使用，但服务器宿主不再依赖进程级静态值表达自身配置。
/// </para>
/// </remarks>
public static class ClientFacingGate
{
    private static readonly AsyncLocal<HostPolicyScope?> CurrentHostPolicy = new();

    /// <summary>
    /// 旧生成代码和直接调用路径使用的进程级兼容开关。
    /// </summary>
    /// <remarks>
    /// 默认 <c>false</c>。当前生成代码通过宿主服务提供者读取
    /// <see cref="PulseRPC.Server.Configuration.PulseServerOptions.EnableClientFacingGate"/>，不会写入本属性；
    /// 本属性仅保留给旧生成程序集、自定义容器回退和直接测试调用。
    /// </remarks>
    public static bool EnforcementEnabled { get; set; }

    /// <summary>
    /// 使用当前路由所属宿主的服务提供者执行客户端可见性检查。
    /// </summary>
    /// <remarks>
    /// 当前 <c>MessageEngine</c> dispatch 的宿主策略优先，使使用根 provider 或其它共享 provider 的
    /// 嵌套路由不能覆盖宿主安全边界；没有活动 dispatch 时才读取 <paramref name="serviceProvider"/>
    /// 上的策略，最后回退到 <see cref="EnforcementEnabled"/>。
    /// </remarks>
    /// <param name="serviceProvider">当前协议路由所属宿主的服务提供者。</param>
    /// <param name="isClientFacing">目标方法是否被标记为客户端可见。</param>
    /// <param name="protocolId">目标方法的协议号。</param>
    /// <param name="methodDisplayName">用于拒绝异常的接口与方法展示名。</param>
    /// <exception cref="ArgumentNullException"><paramref name="serviceProvider"/> 为 <c>null</c>。</exception>
    /// <exception cref="ClientFacingAccessDeniedException">
    /// 当前宿主启用门闸、调用来自外部客户端且目标方法不可见。
    /// </exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Enforce(
        IServiceProvider serviceProvider,
        bool isClientFacing,
        ushort protocolId,
        string methodDisplayName)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        var policy = ResolveAmbientPolicy() ?? ResolveProviderPolicy(serviceProvider);
        EnforceCore(
            policy?.EnforcementEnabled ?? EnforcementEnabled,
            isClientFacing,
            protocolId,
            methodDisplayName);
    }

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
        EnforceCore(
            ResolveAmbientPolicy()?.EnforcementEnabled ?? EnforcementEnabled,
            isClientFacing,
            protocolId,
            methodDisplayName);
    }

    internal static IDisposable? EnterHostPolicyScope(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        var policy = ResolveProviderPolicy(serviceProvider);
        return policy is null ? null : new HostPolicyScope(policy);
    }

    private static IClientFacingGatePolicy? ResolveProviderPolicy(IServiceProvider serviceProvider)
        => serviceProvider.GetService(typeof(IClientFacingGatePolicy)) as IClientFacingGatePolicy;

    private static IClientFacingGatePolicy? ResolveAmbientPolicy()
    {
        var scope = CurrentHostPolicy.Value;
        while (scope is not null && !scope.IsActive)
        {
            scope = scope.Previous;
        }

        return scope?.Policy;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void EnforceCore(
        bool enforcementEnabled,
        bool isClientFacing,
        ushort protocolId,
        string methodDisplayName)
    {
        if (!enforcementEnabled || isClientFacing)
        {
            return;
        }

        if (PulseContext.Current?.SourceType == CallSourceType.ExternalUser)
        {
            throw new ClientFacingAccessDeniedException(protocolId, methodDisplayName);
        }
    }

    private sealed class HostPolicyScope : IDisposable
    {
        private int _active = 1;

        public HostPolicyScope(IClientFacingGatePolicy policy)
        {
            Policy = policy;
            Previous = CurrentHostPolicy.Value;
            CurrentHostPolicy.Value = this;
        }

        public IClientFacingGatePolicy Policy { get; }

        public HostPolicyScope? Previous { get; }

        public bool IsActive => Volatile.Read(ref _active) != 0;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _active, 0) == 0)
            {
                return;
            }

            if (!ReferenceEquals(CurrentHostPolicy.Value, this))
            {
                return;
            }

            var previous = Previous;
            while (previous is not null && !previous.IsActive)
            {
                previous = previous.Previous;
            }

            CurrentHostPolicy.Value = previous;
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
