using DistributedGameApp.BackendServer.Hubs;
using DistributedGameApp.BackendServer.Repositories;
using DistributedGameApp.BackendServer.Services;
using DistributedGameApp.Infrastructure.Hosting;
using DistributedGameApp.Infrastructure.ServiceClient;
using DistributedGameApp.Shared.Hubs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseRPC.Server;
using PulseRPC.Server.Extensions;

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

        // ✅ 使用 AddPulseService 注册 ProcessSingleton 服务
        // 这样 Hub 可以通过 IServiceAccessor<TService> 访问服务，确保队列调度和线程安全
        services.AddPulseService<SocialService>((sp, _) =>
            new SocialService(sp.GetRequiredService<ILogger<SocialService>>()));

        services.AddPulseService<GuildService>((sp, _) =>
            new GuildService(
                sp.GetRequiredService<ILogger<GuildService>>(),
                sp.GetRequiredService<GuildRepository>()));

        services.AddPulseService<LeaderboardService>((sp, _) =>
            new LeaderboardService(sp.GetRequiredService<ILogger<LeaderboardService>>()));

        services.AddPulseService<MatchmakingService>((sp, _) =>
            new MatchmakingService(
                sp.GetRequiredService<ILogger<MatchmakingService>>(),
                sp.GetRequiredService<UnifiedServiceClientManager>()));

        // 注册 Hub 服务（必须使用接口 + 实现的方式）
        // Hub 是无状态的，通过 IServiceAccessor 访问有状态的 Service
        services.AddSingleton<IBackendHub, BackendHub>();
    }
});

// ✅ 匹配服务任务现在由 MatchmakingService 通过 BaseService.ScheduleRecurring 自动管理
// 不再需要单独的 BackgroundService

var app = builder.Build();

// 初始化 UnifiedServiceClientManager（通用版）
var serviceClientManager = app.Services.GetRequiredService<UnifiedServiceClientManager>();

// 注册 HubProxyFactory（编译时类型安全，无反射）
serviceClientManager.RegisterHubProxyFactory(
    (hubType, channel) => PulseRPC.Generated.HubProxyFactory.Instance.Create(hubType, channel));

await serviceClientManager.InitializeAsync(
    new[] { ServerType.Battle, ServerType.Game, },  // BackendServer 主要连接 BattleServer
    RoutingStrategy.ConsistentHash);

await app.RunAsync();
