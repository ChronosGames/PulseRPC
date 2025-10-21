using PulseRPC.Scheduling;

namespace PulseRPC.Server.Models;

/// <summary>
/// 线程亲和性映射记录
/// </summary>
/// <remarks>
/// 记录服务实例到工作线程的映射关系，包含创建时间和最后访问时间，
/// 用于空闲实例清理和线程负载监控。
/// </remarks>
public sealed class ThreadAffinity
{
    /// <summary>
    /// 服务调度键（ServiceName + ServiceId）
    /// </summary>
    public required ServiceSchedulingKey Key { get; init; }

    /// <summary>
    /// 分配的工作线程 ID
    /// </summary>
    public required int AssignedThreadId { get; init; }

    /// <summary>
    /// 创建时间（UTC）
    /// </summary>
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 最后访问时间（UTC）
    /// </summary>
    public DateTime LastAccessUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 计算自最后访问以来的空闲时长
    /// </summary>
    /// <param name="currentUtc">当前 UTC 时间</param>
    /// <returns>空闲时长</returns>
    public TimeSpan IdleDuration(DateTime currentUtc)
    {
        return currentUtc - LastAccessUtc;
    }

    /// <summary>
    /// 判断服务实例是否空闲（超过指定超时时间未访问）
    /// </summary>
    /// <param name="currentUtc">当前 UTC 时间</param>
    /// <param name="idleTimeout">空闲超时阈值</param>
    /// <returns>如果空闲时长超过阈值返回 true，否则返回 false</returns>
    public bool IsIdle(DateTime currentUtc, TimeSpan idleTimeout)
    {
        return IdleDuration(currentUtc) > idleTimeout;
    }
}
