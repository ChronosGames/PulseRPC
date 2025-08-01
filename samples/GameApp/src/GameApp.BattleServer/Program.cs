using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using StackExchange.Redis;
using PulseRPC.Server;
using GameApp.BattleServer.Services;
using GameApp.BattleServer.Configuration;
using GameApp.Infrastructure.Extensions;
using GameApp.Shared.Services;

var builder = Host.CreateApplicationBuilder(args);

// 配置日志
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// 配置选项
builder.Services.Configure<BattleServerOptions>(
    builder.Configuration.GetSection(BattleServerOptions.SectionName));

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

// 战斗服务
builder.Services.AddScoped<IBattleServiceImpl, BattleServiceImpl>();
builder.Services.AddScoped<ISkillServiceImpl, SkillServiceImpl>();
builder.Services.AddScoped<IBattleEventPublisher, BattleEventPublisher>();

// 数据访问服务
builder.Services.AddScoped<IBattleRepository, BattleRepository>();
builder.Services.AddScoped<ISkillRepository, SkillRepository>();
builder.Services.AddScoped<IBattleCacheService, BattleCacheService>();

// 战斗系统核心服务
builder.Services.AddScoped<IBattleEngine, BattleEngine>();
builder.Services.AddScoped<ISkillSystem, SkillSystem>();
builder.Services.AddScoped<IDamageCalculator, DamageCalculator>();
builder.Services.AddScoped<IBuffSystem, BuffSystem>();

// PulseRPC 服务器配置
var battleServerOptions = builder.Configuration.GetSection(BattleServerOptions.SectionName).Get<BattleServerOptions>() ??
    new BattleServerOptions();

builder.Services.AddPulseRpcServer(builder =>
{
    // 主要使用 KCP 通道用于低延迟战斗
    builder.AddKcp("KcpChannel", battleServerOptions.KcpPort);

    // 添加 TCP 通道用于可靠传输
    builder.AddTcp("TcpChannel", battleServerOptions.TcpPort);
});

// 注册 PulseRPC 服务
builder.Services.AddSingleton<IBattleService, BattleServiceImpl>();
builder.Services.AddSingleton<ISkillService, SkillServiceImpl>();

// 构建应用
var host = builder.Build();

// 获取日志记录器
var logger = host.Services.GetRequiredService<ILogger<Program>>();

try
{
    logger.LogInformation("GameApp BattleServer 正在启动...");

    // 测试 MongoDB 连接
    var mongoClient = host.Services.GetRequiredService<IMongoClient>();
    await mongoClient.ListDatabaseNamesAsync();
    logger.LogInformation("MongoDB 连接成功");

    // 测试 Redis 连接
    var redis = host.Services.GetRequiredService<IConnectionMultiplexer>();
    var db = redis.GetDatabase();
    await db.PingAsync();
    logger.LogInformation("Redis 连接成功");

    // 初始化战斗系统
    var battleEngine = host.Services.GetRequiredService<IBattleEngine>();
    await battleEngine.InitializeAsync();
    logger.LogInformation("战斗引擎初始化完成");

    logger.LogInformation("GameApp BattleServer 启动完成");
    logger.LogInformation("TCP 端口: {TcpPort}", battleServerOptions.TcpPort);
    logger.LogInformation("KCP 端口: {KcpPort}", battleServerOptions.KcpPort);
    logger.LogInformation("服务器ID: {ServerId}", battleServerOptions.ServerId);

    // 运行服务器
    await host.RunAsync();
}
catch (Exception ex)
{
    logger.LogError(ex, "GameApp BattleServer 启动失败");
    throw;
}
