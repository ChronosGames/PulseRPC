namespace PulseRPC.Server.Clustering;

/// <summary>
/// 节点 wire 的兼容与反序列化安全限制。
/// </summary>
public sealed class ClusterNodeWireOptions
{
    /// <summary>节点认证凭据允许的最大字节数。默认 64 KiB。</summary>
    public int MaxNodeCredentialSize { get; set; } = 64 * 1024;

    /// <summary>
    /// 是否接受没有版本/能力/lease/caller 的历史 Actor 协议。默认关闭；仅用于滚动升级窗口。
    /// </summary>
    public bool AllowLegacyActorProtocol { get; set; }

    /// <summary>Caller 最多包含的 identity 数量。</summary>
    public int MaxIdentityCount { get; set; } = 8;

    /// <summary>Caller 所有 identity 合计最多包含的 claim 数量。</summary>
    public int MaxClaimCount { get; set; } = 256;

    /// <summary>单个 claim 最多包含的 properties 数量。</summary>
    public int MaxClaimPropertyCount { get; set; } = 32;

    /// <summary>Permissions 或 Roles 各自允许的最大元素数。</summary>
    public int MaxAuthorizationValueCount { get; set; } = 256;

    /// <summary>Caller/identity/claim 中单个字符串字段允许的最大字符数。</summary>
    public int MaxFieldLength { get; set; } = 4096;
}
