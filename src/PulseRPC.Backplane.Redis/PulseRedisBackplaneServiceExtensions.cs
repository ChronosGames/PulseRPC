using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PulseRPC.Clustering;
using StackExchange.Redis;

namespace PulseRPC.Backplane.Redis;

/// <summary>
/// 把 <see cref="IPulseBackplane"/> 的默认单节点实现（<see cref="InProcessBackplane"/>）替换为
/// Redis 实现（<see cref="RedisPulseBackplane"/>），供多节点集群部署使用（§P6）。
/// </summary>
public static class PulseRedisBackplaneServiceExtensions
{
    /// <summary>
    /// 注册 Redis Backplane：使用调用方提供的 <paramref name="connectionFactory"/> 惰性创建（并缓存为单例）
    /// <see cref="IConnectionMultiplexer"/>，覆盖默认的 <see cref="IPulseBackplane"/> 注册。
    /// </summary>
    /// <remarks>
    /// 应在 <c>AddPulseRouting</c>/<c>AddPulseClustering</c> 之后调用，以确保能正确覆盖其默认注册的
    /// <see cref="InProcessBackplane"/>。不接管 <see cref="IConnectionMultiplexer"/> 的连接生命周期语义
    /// （重连、故障转移等）——这些完全由 StackExchange.Redis 自身处理；调用方可选择传入一个已在别处
    /// 管理生命周期的共享连接工厂。
    /// </remarks>
    public static IServiceCollection AddRedisBackplane(
        this IServiceCollection services,
        Func<IServiceProvider, IConnectionMultiplexer> connectionFactory,
        Action<RedisBackplaneOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(connectionFactory);

        services.Configure<RedisBackplaneOptions>(configureOptions ?? (_ => { }));
        services.TryAddSingleton(connectionFactory);

        services.RemoveAll<IPulseBackplane>();
        services.AddSingleton<IPulseBackplane, RedisPulseBackplane>();

        return services;
    }

    /// <summary>
    /// 注册 Redis Backplane 的便捷重载：直接按连接字符串惰性创建 <see cref="IConnectionMultiplexer"/>。
    /// </summary>
    public static IServiceCollection AddRedisBackplane(
        this IServiceCollection services,
        string connectionString,
        Action<RedisBackplaneOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(connectionString);
        return services.AddRedisBackplane(_ => ConnectionMultiplexer.Connect(connectionString), configureOptions);
    }
}
