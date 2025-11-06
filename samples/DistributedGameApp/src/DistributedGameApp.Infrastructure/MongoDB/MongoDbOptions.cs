namespace DistributedGameApp.Infrastructure.MongoDB;

/// <summary>
/// MongoDB 配置选项
/// </summary>
public class MongoDbOptions
{
    /// <summary>
    /// 配置节名称
    /// </summary>
    public const string SectionName = "MongoDB";

    /// <summary>
    /// 连接字符串
    /// </summary>
    public string ConnectionString { get; set; } = "mongodb://localhost:27017";

    /// <summary>
    /// 数据库名称（可以被各个 Repository 覆盖）
    /// </summary>
    public string DefaultDatabaseName { get; set; } = "game_accounts";

    /// <summary>
    /// 账户数据库名称
    /// </summary>
    public string AccountsDatabase { get; set; } = "game_accounts";

    /// <summary>
    /// 角色数据库名称
    /// </summary>
    public string CharactersDatabase { get; set; } = "game_characters";

    /// <summary>
    /// 社交数据库名称
    /// </summary>
    public string SocialDatabase { get; set; } = "game_social";

    /// <summary>
    /// 帮派数据库名称
    /// </summary>
    public string GuildsDatabase { get; set; } = "game_guilds";

    /// <summary>
    /// 战斗数据库名称
    /// </summary>
    public string BattlesDatabase { get; set; } = "game_battles";

    /// <summary>
    /// 排行榜数据库名称
    /// </summary>
    public string LeaderboardsDatabase { get; set; } = "game_leaderboards";
}
