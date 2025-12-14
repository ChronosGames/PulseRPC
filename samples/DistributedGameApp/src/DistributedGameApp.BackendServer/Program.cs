using DistributedGameApp.BackendServer;
using DistributedGameApp.BackendServer.Hubs;
using DistributedGameApp.BackendServer.Repositories;
using DistributedGameApp.BackendServer.Services;
using DistributedGameApp.Infrastructure.Hosting;
using DistributedGameApp.Infrastructure.Hosting.Bootstrap;
using DistributedGameApp.Infrastructure.ServiceClient;
using DistributedGameApp.Shared.Hubs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseRPC.Server;
using PulseRPC.Server.Extensions;

var builder = Host.CreateApplicationBuilder(args);

// 配置服务类型（BackendServer 调用 BattleServer 和 GameServer）
// 服务可以按任意顺序启动，运行时按需建立连接
builder.Services.Configure<ServiceDependencyOptions>(options =>
{
    options.ServerTypes = [ServerType.Battle, ServerType.Game];
    options.RoutingStrategy = RoutingStrategy.ConsistentHash;
    options.RequestTimeout = TimeSpan.FromSeconds(10);
    options.RequestRetryInterval = TimeSpan.FromMilliseconds(500);
});

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

// ✅ 服务客户端初始化已移至 Bootstrap 流程中（Phase 5.5）
// UnifiedServiceClientManager 会在 ServerBootstrapOrchestrator 的 Phase5_5 阶段自动初始化
// 这确保了：
// 1. 在 Consul 注册之前初始化，等待依赖服务（BattleServer, GameServer）就绪
// 2. 只有当所有依赖服务可用时，才注册到 Consul 并开始接受请求
// 3. 避免因服务启动顺序导致的连接失败问题
app.Services.GetRequiredService<UnifiedServiceClientManager>().RegisterHubProxyFactory(new HubProxyFactory());

await app.RunAsync();
