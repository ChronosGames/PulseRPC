using DistributedGameApp.BackendServer.Repositories;
using DistributedGameApp.BackendServer.Services;
using DistributedGameApp.Infrastructure.Hosting;
using DistributedGameApp.Infrastructure.ServiceClient;
using DistributedGameApp.Shared.Hubs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PulseRPC.Server;

var builder = Host.CreateApplicationBuilder(args);

// 使用统一的 ServerBootstrapper 配置服务器
builder.Services.AddPulseRpcServer(builder.Configuration, new ServerBootstrapperOptions
{
    EnableExternalListener = false,     // BackendServer 仅内网监听
    EnableInternalRpcChannel = true,    // 内网 RPC 通道
    EnableServiceDiscovery = true,      // Consul 服务注册与发现
    EnableMongoDb = true,               // MongoDB 数据库
    EnableSentry = true,                // Sentry 错误追踪（根据配置启用）
    ConfigureServices = services =>
    {
        // 注册认证和权限服务
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddSingleton<IAuthenticationService, AuthenticationService>();
        services.AddSingleton<PermissionValidator>();

        // 注册仓储
        services.AddSingleton<GuildRepository>();

        // 注册服务间通信（通用版）
        services.AddSingleton<LocalServiceRegistry>();
        services.AddSingleton<UnifiedServiceClientManager>();

        // 添加应用特定服务
        services.AddSingleton<SocialService>();
        services.AddSingleton<GuildService>();
        services.AddSingleton<LeaderboardService>();
        services.AddSingleton<MatchmakingService>();

        // ✅ 使用通用的 PulseServiceHostedService 来启动 IPulseService
        // 这将自动初始化 MatchmakingService 并调用其 OnStartAsync 方法
        services.AddHostedService<PulseRPC.Server.Services.PulseServiceHostedService<MatchmakingService>>();

        // 注册 Hub 服务（必须使用接口 + 实现的方式）
        services.AddSingleton<IBackendHub, BackendHub>();
        // services.AddSingleton<PulseRPC.Server.Authentication.IAuthenticationHub, PulseRPC.Server.Authentication.AuthenticationHub>();
    }
});

// ✅ 匹配服务任务现在由 MatchmakingService 通过 BaseService.ScheduleRecurring 自动管理
// 不再需要单独的 BackgroundService

var app = builder.Build();

// 初始化 UnifiedServiceClientManager（通用版）
var serviceClientManager = app.Services.GetRequiredService<UnifiedServiceClientManager>();
await serviceClientManager.InitializeAsync(
    new[] { ServerType.Battle, ServerType.Game, },  // BackendServer 主要连接 BattleServer
    RoutingStrategy.ConsistentHash);

await app.RunAsync();
