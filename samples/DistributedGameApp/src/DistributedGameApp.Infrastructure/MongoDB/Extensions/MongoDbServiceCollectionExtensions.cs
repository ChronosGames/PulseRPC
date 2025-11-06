using DistributedGameApp.Infrastructure.MongoDB.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DistributedGameApp.Infrastructure.MongoDB.Extensions;

/// <summary>
/// MongoDB 依赖注入扩展
/// </summary>
public static class MongoDbServiceCollectionExtensions
{
    /// <summary>
    /// 添加 MongoDB 服务
    /// </summary>
    public static IServiceCollection AddMongoDb(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 配置选项
        services.Configure<MongoDbOptions>(
            configuration.GetSection(MongoDbOptions.SectionName));

        // 注册 DbContext
        services.AddSingleton<MongoDbContext>();

        // 注册所有 Repository
        services.AddSingleton<AccountRepository>();
        services.AddSingleton<CharacterRepository>();
        services.AddSingleton<GuildRepository>();
        services.AddSingleton<GuildMemberRepository>();
        services.AddSingleton<FriendRepository>();
        services.AddSingleton<ChatMessageRepository>();
        services.AddSingleton<LeaderboardRepository>();
        services.AddSingleton<BattleRecordRepository>();
        services.AddSingleton<MailRepository>();

        return services;
    }

    /// <summary>
    /// 添加 MongoDB 服务（使用自定义配置）
    /// </summary>
    public static IServiceCollection AddMongoDb(
        this IServiceCollection services,
        Action<MongoDbOptions> configureOptions)
    {
        // 配置选项
        services.Configure(configureOptions);

        // 注册 DbContext
        services.AddSingleton<MongoDbContext>();

        // 注册所有 Repository
        services.AddSingleton<AccountRepository>();
        services.AddSingleton<CharacterRepository>();
        services.AddSingleton<GuildRepository>();
        services.AddSingleton<GuildMemberRepository>();
        services.AddSingleton<FriendRepository>();
        services.AddSingleton<ChatMessageRepository>();
        services.AddSingleton<LeaderboardRepository>();
        services.AddSingleton<BattleRecordRepository>();
        services.AddSingleton<MailRepository>();

        return services;
    }
}
