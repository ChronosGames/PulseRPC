using PulseServiceDiscovery.Abstractions.Models;

namespace PulseServiceDiscovery.Client.Caching;

/// <summary>
/// 服务缓存接口
/// </summary>
public interface IServiceCache : IDisposable
{
    /// <summary>
    /// 从缓存获取服务端点
    /// </summary>
    /// <param name="serviceName">服务名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>服务端点列表</returns>
    Task<IReadOnlyList<ServiceEndpoint>?> GetAsync(string serviceName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 设置服务端点到缓存
    /// </summary>
    /// <param name="serviceName">服务名称</param>
    /// <param name="endpoints">服务端点列表</param>
    /// <param name="ttl">生存时间</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>设置任务</returns>
    Task SetAsync(string serviceName, IReadOnlyList<ServiceEndpoint> endpoints, TimeSpan ttl, CancellationToken cancellationToken = default);

    /// <summary>
    /// 从缓存移除服务端点
    /// </summary>
    /// <param name="serviceName">服务名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>移除任务</returns>
    Task RemoveAsync(string serviceName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 清空所有缓存
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>清空任务</returns>
    Task ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取缓存统计信息
    /// </summary>
    /// <returns>统计信息</returns>
    CacheStatistics GetStatistics() => new(0, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// 释放资源
    /// </summary>
    void IDisposable.Dispose()
    {
        // 默认空实现，子类可以重写
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// 缓存统计信息
/// </summary>
public record CacheStatistics(
    long HitCount,
    long MissCount,
    long EvictionCount,
    int CurrentSize,
    TimeSpan AverageLoadTime)
{
    /// <summary>
    /// 命中率
    /// </summary>
    public double HitRate => (HitCount + MissCount) > 0 ? (double)HitCount / (HitCount + MissCount) : 0.0;
}
