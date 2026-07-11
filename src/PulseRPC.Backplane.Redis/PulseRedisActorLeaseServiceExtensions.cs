using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PulseRPC.Clustering;

namespace PulseRPC.Backplane.Redis;

/// <summary>
/// Redis Actor 租约存储的依赖注入扩展。
/// </summary>
public static class PulseRedisActorLeaseServiceExtensions
{
    /// <summary>
    /// 使用共享的 <see cref="StackExchange.Redis.IConnectionMultiplexer"/> 注册 Redis Actor 租约存储，
    /// 并覆盖默认的进程内 <see cref="IActorLeaseStore"/> 注册。
    /// </summary>
    /// <remarks>
    /// 本方法不创建也不接管 Redis 连接生命周期。调用方应先注册一个单例
    /// <see cref="StackExchange.Redis.IConnectionMultiplexer"/>；可与 <see cref="RedisPulseBackplane"/>
    /// 共用同一实例。本方法可在 <c>AddPulseClustering</c> 前后调用。
    /// </remarks>
    public static IServiceCollection AddRedisActorLeases(
        this IServiceCollection services,
        Action<RedisActorLeaseStoreOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.Configure<RedisActorLeaseStoreOptions>(configureOptions ?? (_ => { }));
        services.RemoveAll<IActorLeaseStore>();
        services.AddSingleton<IActorLeaseStore, RedisActorLeaseStore>();
        return services;
    }
}
