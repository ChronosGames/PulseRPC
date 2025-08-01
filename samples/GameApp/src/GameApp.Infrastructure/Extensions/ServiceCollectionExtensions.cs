using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using GameApp.Infrastructure.Services;
using GameApp.Infrastructure.Configuration;

namespace GameApp.Infrastructure.Extensions;

/// <summary>
/// 服务集合扩展方法
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 添加 GameApp 基础设施服务
    /// </summary>
    public static IServiceCollection AddGameAppInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 配置选项
        services.Configure<MongoDbOptions>(configuration.GetSection("MongoDB"));
        services.Configure<RedisOptions>(configuration.GetSection("Redis"));
        services.Configure<ConsulOptions>(configuration.GetSection("Consul"));

        // 基础设施服务
        services.AddSingleton<IMongoDbService, MongoDbService>();
        services.AddSingleton<IRedisService, RedisService>();
        services.AddSingleton<IConsulService, ConsulService>();
        services.AddSingleton<IDistributedLockService, RedisDistributedLockService>();

        // 日志和监控
        services.AddScoped<IStructuredLogger, StructuredLogger>();
        services.AddSingleton<IMetricsCollector, MetricsCollector>();

        // 配置管理
        services.AddSingleton<IConfigurationService, ConfigurationService>();

        return services;
    }
}

/// <summary>
/// MongoDB 配置选项
/// </summary>
public class MongoDbOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = "gameapp_dev";
}

/// <summary>
/// Redis 配置选项
/// </summary>
public class RedisOptions
{
    public string ConnectionString { get; set; } = "localhost:6379";
    public string Password { get; set; } = string.Empty;
    public int Database { get; set; } = 0;
}

/// <summary>
/// Consul 配置选项
/// </summary>
public class ConsulOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 8500;
    public string Datacenter { get; set; } = "dc1";
}
