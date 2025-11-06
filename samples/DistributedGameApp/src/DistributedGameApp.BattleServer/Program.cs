using DistributedGameApp.BattleServer.Services;
using DistributedGameApp.Infrastructure.Hosting;
using DistributedGameApp.Shared.Hubs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

// 使用统一的 ServerBootstrapper 配置服务器
builder.Services.AddPulseRpcServer(builder.Configuration, new ServerBootstrapperOptions
{
    EnableExternalListener = true,      // BattleServer 需要外网监听（玩家连接）
    EnableInternalRpcChannel = true,    // 内网 RPC 通道（服务器间通信）
    EnableServiceDiscovery = true,      // Consul 服务注册与发现
    EnableMongoDb = false,              // BattleServer 无数据库（内存状态）
    EnableSentry = true,                // Sentry 错误追踪（根据配置启用）
    ConfigureServices = services =>
    {
        // 添加应用特定服务
        services.AddSingleton<BattleRoomManager>();

        // 注册 Hub 服务（必须使用接口 + 实现的方式）
        services.AddSingleton<IBattleHub, BattleHub>();
    }
});

var app = builder.Build();

await app.RunAsync();
