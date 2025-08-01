using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Caching.StackExchangeRedis;
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
        // 配置选项 (暂时简化)
        // services.Configure<InfrastructureOptions>(
        //     configuration.GetSection(InfrastructureOptions.SectionName));

        // 基础设施服务
        services.AddScoped<IInfrastructureService, InfrastructureService>();

        // 缓存配置
        services.AddStackExchangeRedisCache(options =>
        {
            var connectionString = configuration.GetConnectionString("Redis");
            if (!string.IsNullOrEmpty(connectionString))
            {
                options.Configuration = connectionString;
            }
        });

        return services;
    }
}
