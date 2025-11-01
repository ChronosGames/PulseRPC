namespace PulseRPC.Server;

/// <summary>
/// 服务实例管理策略
/// </summary>
public enum ServiceInstanceStrategy
{
    /// <summary>进程内唯一实例</summary>
    Singleton,

    /// <summary>每次请求创建新实例</summary>
    Transient,

    /// <summary>对象池管理</summary>
    Pooled,

    /// <summary>集群内唯一实例</summary>
    Global
}
