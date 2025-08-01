using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using StackExchange.Redis;
using PulseRPC.Server;
using GameApp.GameServer.Services;
using GameApp.GameServer.Configuration;
using GameApp.Infrastructure.Extensions;
using GameApp.Shared.Services;

var builder = Host.CreateApplicationBuilder(args);

// 配置日志
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// 配置选项
builder.Services.Configure<GameServerOptions>(
    builder.Configuration.GetSection(GameServerOptions.SectionName));

// MongoDB 配置
builder.Services.AddSingleton<IMongoClient>(sp =>
{
    var connectionString = builder.Configuration.GetConnectionString("MongoDB") ??
        throw new InvalidOperationException("MongoDB connection string not configured");
    return new MongoClient(connectionString);
});

builder.Services.AddSingleton(sp =>
{
    var client = sp.GetRequiredService<IMongoClient>();
    return client.GetDatabase("gameapp_dev");
});

// Redis 配置
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var connectionString = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
    var password = builder.Configuration["Redis:Password"];

    var configuration = ConfigurationOptions.Parse(connectionString);
    if (!string.IsNullOrEmpty(password))
    {
        configuration.Password = password;
    }

    return ConnectionMultiplexer.Connect(configuration);
});

// 基础设施服务
builder.Services.AddGameAppInfrastructure(builder.Configuration);

// 游戏服务
builder.Services.AddScoped<IPlayerServiceImpl, PlayerServiceImpl>();
builder.Services.AddScoped<IWorldServiceImpl, WorldServiceImpl>();
builder.Services.AddScoped<IPlayerEventPublisher, PlayerEventPublisher>();
builder.Services.AddScoped<IWorldEventPublisher, WorldEventPublisher>();

// 数据访问服务
builder.Services.AddScoped<IPlayerRepository, PlayerRepository>();
builder.Services.AddScoped<IWorldRepository, WorldRepository>();
builder.Services.AddScoped<IPlayerCacheService, PlayerCacheService>();
builder.Services.AddScoped<IWorldCacheService, WorldCacheService>();

// PulseRPC 服务器配置
var gameServerOptions = builder.Configuration.GetSection(GameServerOptions.SectionName).Get<GameServerOptions>() ??
    new GameServerOptions();

builder.Services.AddPulseRPC(options =>
{
    // 添加 TCP 通道
    options.AddTcp("TcpChannel", gameServerOptions.TcpPort);

    // 添加 KCP 通道
    options.AddKcp("KcpChannel", gameServerOptions.KcpPort);

    // 注册服务
    options.AddService<IPlayerService, PlayerServiceImpl>();
    options.AddService<IWorldService, WorldServiceImpl>();

    // 配置序列化
    options.UseMemoryPack();
});

// 构建应用
var host = builder.Build();

// 获取日志记录器
var logger = host.Services.GetRequiredService<ILogger<Program>>();

try
{
    logger.LogInformation("GameApp GameServer 正在启动...");

    // 测试 MongoDB 连接
    var mongoClient = host.Services.GetRequiredService<IMongoClient>();
    await mongoClient.ListDatabaseNamesAsync();
    logger.LogInformation("MongoDB 连接成功");

    // 测试 Redis 连接
    var redis = host.Services.GetRequiredService<IConnectionMultiplexer>();
    var db = redis.GetDatabase();
    await db.PingAsync();
    logger.LogInformation("Redis 连接成功");

    logger.LogInformation("GameApp GameServer 启动完成");
    logger.LogInformation("TCP 端口: {TcpPort}", gameServerOptions.TcpPort);
    logger.LogInformation("KCP 端口: {KcpPort}", gameServerOptions.KcpPort);

    // 运行服务器
    await host.RunAsync();
}
catch (Exception ex)
{
    logger.LogError(ex, "GameApp GameServer 启动失败");
    throw;
}
