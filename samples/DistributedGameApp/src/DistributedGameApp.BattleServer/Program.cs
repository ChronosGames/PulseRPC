using DistributedGameApp.BattleServer.Services;
using DistributedGameApp.Infrastructure.Hosting;
using DistributedGameApp.Shared.Hubs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PulseRPC.Server;

var builder = Host.CreateApplicationBuilder(args);

// 使用统一的 ServerBootstrapper 配置服务器
builder.Services.AddPulseRpcServer(builder.Configuration, new ServerBootstrapperOptions
{
    EnableExternalListener = false,     // BattleServer 仅内网监听
    EnableInternalRpcChannel = true,    // 内网 RPC 通道
    EnableServiceDiscovery = true,      // Consul 服务注册与发现
    EnableMongoDb = false,              // BattleServer 不直接访问数据库
    EnableSentry = true,                // Sentry 错误追踪（根据配置启用）
    ConfigureServices = services =>
    {
        // 注册认证和权限服务
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddSingleton<IAuthenticationService, AuthenticationService>();
        services.AddSingleton<PermissionValidator>();

        // 添加连接上下文管理器
        services.AddSingleton<BattleConnectionContext>();

        // 添加应用服务
        services.AddSingleton<BattleRoomManager>();

        // 注册 Hub 服务（必须使用接口 + 实现的方式）
        services.AddSingleton<IBattleHub, BattleHub>();
    }
});

var app = builder.Build();

await app.RunAsync();
