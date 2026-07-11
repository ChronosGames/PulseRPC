namespace PulseRPC.Server.Clustering;

/// <summary>
/// Actor 放置策略：把 <c>(Hub, Key)</c> 映射为候选属主节点。
/// </summary>
public interface IActorPlacementStrategy
{
    /// <summary>
    /// 选择给定 Actor identity 的候选属主节点。
    /// </summary>
    string SelectOwner(string hub, string key);
}
