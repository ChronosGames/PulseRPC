using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace DistributedGameApp.Infrastructure.MongoDB;

/// <summary>
/// MongoDB 数据库上下文
/// </summary>
public class MongoDbContext
{
    private readonly IMongoClient _client;
    private readonly MongoDbOptions _options;

    public MongoDbContext(IOptions<MongoDbOptions> options)
    {
        _options = options.Value;
        _client = new MongoClient(_options.ConnectionString);
    }

    /// <summary>
    /// 账户数据库
    /// </summary>
    public IMongoDatabase AccountsDatabase => _client.GetDatabase(_options.AccountsDatabase);

    /// <summary>
    /// 角色数据库
    /// </summary>
    public IMongoDatabase CharactersDatabase => _client.GetDatabase(_options.CharactersDatabase);

    /// <summary>
    /// 社交数据库
    /// </summary>
    public IMongoDatabase SocialDatabase => _client.GetDatabase(_options.SocialDatabase);

    /// <summary>
    /// 帮派数据库
    /// </summary>
    public IMongoDatabase GuildsDatabase => _client.GetDatabase(_options.GuildsDatabase);

    /// <summary>
    /// 战斗数据库
    /// </summary>
    public IMongoDatabase BattlesDatabase => _client.GetDatabase(_options.BattlesDatabase);

    /// <summary>
    /// 排行榜数据库
    /// </summary>
    public IMongoDatabase LeaderboardsDatabase => _client.GetDatabase(_options.LeaderboardsDatabase);

    /// <summary>
    /// 获取 MongoDB 客户端
    /// </summary>
    public IMongoClient Client => _client;
}
