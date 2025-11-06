using MemoryPack;

namespace PulseRPC.Server.Routing;

/// <summary>
/// 哈希环版本快照（仅记录哈希环变化）
/// 发布到Etcd供所有节点订阅
/// </summary>
[MemoryPackable]
public partial class HashRingSnapshot
{
    /// <summary>
    /// 哈希环版本号（使用Unix时间戳毫秒数）
    /// </summary>
    public long Version { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 当前活跃的节点列表
    /// </summary>
    public List<ushort> ActiveNodes { get; set; } = new();

    /// <summary>
    /// 是否启用固定模式（节点变化后为true）
    /// </summary>
    public bool UseFixedMapping { get; set; }

    /// <summary>
    /// 变化原因（用于调试）
    /// </summary>
    public string ChangeReason { get; set; } = string.Empty;

    /// <summary>
    /// 新增的节点列表
    /// </summary>
    public List<ushort> AddedNodes { get; set; } = new();

    /// <summary>
    /// 移除的节点列表
    /// </summary>
    public List<ushort> RemovedNodes { get; set; } = new();
}
