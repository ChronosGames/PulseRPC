using System;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC.Clustering;

/// <summary>
/// 节点间互信鉴权的结果。
/// </summary>
public readonly struct NodeAuthResult
{
    /// <summary>是否通过鉴权。</summary>
    public bool IsAuthenticated { get; }

    /// <summary>失败原因（通过时为 <c>null</c>）。</summary>
    public string? FailureReason { get; }

    private NodeAuthResult(bool isAuthenticated, string? failureReason)
    {
        IsAuthenticated = isAuthenticated;
        FailureReason = failureReason;
    }

    /// <summary>创建"通过"结果。</summary>
    public static NodeAuthResult Success() => new(true, null);

    /// <summary>创建"失败"结果。</summary>
    public static NodeAuthResult Failure(string reason) => new(false, reason);
}

/// <summary>
/// 节点↔节点互信鉴权抽象 —— 防止外部/客户端伪造成内部节点。
/// </summary>
/// <remarks>
/// <para>
/// 首版实现使用<strong>共享密钥（预共享 token / HMAC）</strong>：内网受控通信下最简、依赖最少；
/// 生产环境使用 <strong>mTLS</strong>（双向证书，配合服务发现落地）。两种实现经本接口切换。
/// </para>
/// <para>
/// 与 Gateway 的来源钳制配合：Gateway 强制把外部客户端标记为 External，禁止其自称 Internal；
/// backend 仅信任通过本鉴权的节点链路所携带的来源标记。
/// </para>
/// </remarks>
public interface INodeAuthenticator
{
    /// <summary>
    /// 为本节点生成用于向对端证明身份的凭据（如 HMAC 签名的令牌）。
    /// </summary>
    ValueTask<ReadOnlyMemory<byte>> CreateCredentialAsync(string localNodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 校验来自远端节点的凭据。
    /// </summary>
    ValueTask<NodeAuthResult> ValidateAsync(string remoteNodeId, ReadOnlyMemory<byte> credential, CancellationToken cancellationToken = default);
}
