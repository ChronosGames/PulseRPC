using System;

namespace PulseRPC.Routing;

/// <summary>
/// 统一寻址的目标种类。
/// </summary>
/// <remarks>
/// 描述一次 <see cref="PulseAddress"/> 指向的目标拓扑，供 <see cref="IPulseRouter"/> 解析为实际投递动作。
/// </remarks>
public enum AddressKind : byte
{
    /// <summary>单个连接（按 connectionId）。</summary>
    Connection = 0,

    /// <summary>某个接收 Hub 的全部已认证连接（广播）。</summary>
    AllClients = 1,

    /// <summary>某个组（groupName 存放在 <see cref="PulseAddress.Key"/>）。</summary>
    Group = 2,

    /// <summary>某个用户的全部连接（userId 存放在 <see cref="PulseAddress.Key"/>）。</summary>
    User = 3,

    /// <summary>除某连接外的全部已认证连接（被排除的 connectionId 存放在 <see cref="PulseAddress.Key"/>）。</summary>
    Except = 4,

    /// <summary>某个 keyed Actor 实例（实例键存放在 <see cref="PulseAddress.Key"/>）。</summary>
    Actor = 5,

    /// <summary>某个具体节点（nodeId 存放在 <see cref="PulseAddress.NodeId"/>）。</summary>
    Node = 6,
}

/// <summary>
/// 统一寻址值类型 —— 描述"把一次协议号调用投递到哪里"。
/// </summary>
/// <remarks>
/// <para>
/// 由调用方代理（ClientStub / Fan-out 代理 / Actor 代理）构造，交给 <see cref="IPulseRouter"/> 解析。
/// 与线上信封头三元组一致：<see cref="Hub"/> 对应 <c>ServiceName</c>、<see cref="Key"/> 对应
/// <c>ServiceKey</c>（Actor 实例键 / groupName / userId / connectionId）、协议号在调用时单独传入。
/// </para>
/// <para>
/// 该结构手写 <see cref="IEquatable{T}"/> 而不使用 <c>record struct</c>，以保持与 Unity（C# 9 基线）的兼容性。
/// </para>
/// </remarks>
public readonly struct PulseAddress : IEquatable<PulseAddress>
{
    /// <summary>目标种类。</summary>
    public AddressKind Kind { get; }

    /// <summary>目标 Hub 名称（对应线上 <c>ServiceName</c>）。</summary>
    public string Hub { get; }

    /// <summary>
    /// 目标键，语义随 <see cref="Kind"/> 变化：Actor 实例键 / groupName / userId / 被排除或目标 connectionId。
    /// 对 <see cref="AddressKind.AllClients"/> / <see cref="AddressKind.Node"/> 为空。
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// 可选的显式目标节点标识；为 <c>null</c> 时由 placement / 目录解析（见 <c>IActorDirectory</c>）。
    /// </summary>
    public string? NodeId { get; }

    /// <summary>
    /// 构造一个寻址值。
    /// </summary>
    public PulseAddress(AddressKind kind, string hub, string key = "", string? nodeId = null)
    {
        Kind = kind;
        Hub = hub ?? string.Empty;
        Key = key ?? string.Empty;
        NodeId = nodeId;
    }

    /// <summary>指向单个连接。</summary>
    public static PulseAddress Connection(string hub, string connectionId)
        => new(AddressKind.Connection, hub, connectionId);

    /// <summary>指向某接收 Hub 的全部已认证连接。</summary>
    public static PulseAddress AllClients(string hub)
        => new(AddressKind.AllClients, hub);

    /// <summary>指向某个组。</summary>
    public static PulseAddress Group(string hub, string groupName)
        => new(AddressKind.Group, hub, groupName);

    /// <summary>指向某个用户的全部连接。</summary>
    public static PulseAddress User(string hub, string userId)
        => new(AddressKind.User, hub, userId);

    /// <summary>指向除某连接外的全部已认证连接。</summary>
    public static PulseAddress Except(string hub, string excludedConnectionId)
        => new(AddressKind.Except, hub, excludedConnectionId);

    /// <summary>指向某个 keyed Actor 实例（可选显式节点）。</summary>
    public static PulseAddress Actor(string hub, string key, string? nodeId = null)
        => new(AddressKind.Actor, hub, key, nodeId);

    /// <summary>指向某个具体节点上的某 Hub/实例。</summary>
    public static PulseAddress Node(string nodeId, string hub, string key = "")
        => new(AddressKind.Node, hub, key, nodeId);

    /// <summary>返回替换了目标节点的新地址（其余字段不变）。</summary>
    public PulseAddress WithNode(string? nodeId) => new(Kind, Hub, Key, nodeId);

    /// <inheritdoc/>
    public bool Equals(PulseAddress other)
        => Kind == other.Kind
           && string.Equals(Hub, other.Hub, StringComparison.Ordinal)
           && string.Equals(Key, other.Key, StringComparison.Ordinal)
           && string.Equals(NodeId, other.NodeId, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is PulseAddress other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        hash.Add(Hub, StringComparer.Ordinal);
        hash.Add(Key, StringComparer.Ordinal);
        hash.Add(NodeId, StringComparer.Ordinal);
        return hash.ToHashCode();
    }

    /// <inheritdoc/>
    public override string ToString()
        => NodeId is null
            ? $"PulseAddress({Kind}, Hub='{Hub}', Key='{Key}')"
            : $"PulseAddress({Kind}, Hub='{Hub}', Key='{Key}', Node='{NodeId}')";
}
