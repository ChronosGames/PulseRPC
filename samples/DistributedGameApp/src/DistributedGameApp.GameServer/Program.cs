using DistributedGameApp.GameServer;
using DistributedGameApp.GameServer.Authentication;
using DistributedGameApp.GameServer.Hubs;
using DistributedGameApp.GameServer.Services;
using DistributedGameApp.Infrastructure.Consul;
using DistributedGameApp.Infrastructure.Hosting;
using DistributedGameApp.Infrastructure.Hosting.Bootstrap;
using DistributedGameApp.Infrastructure.MongoDB.Repositories;
using DistributedGameApp.Infrastructure.ServiceClient;
using DistributedGameApp.Shared.Hubs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseRPC.Server;
using PulseRPC.Server.Extensions;

var builder = Host.CreateApplicationBuilder(args);

// 配置 JWT 选项
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));

// 配置服务类型（GameServer 调用 BackendServer 和 BattleServer）
// 服务可以按任意顺序启动，运行时按需建立连接
builder.Services.Configure<ServiceDependencyOptions>(options =>
{
    options.ServerTypes = [ServerType.Backend, ServerType.Battle];
    options.RoutingStrategy = RoutingStrategy.ConsistentHash;
    options.RequestTimeout = TimeSpan.FromSeconds(10);      // 请求时等待连接的超时
    options.RequestRetryInterval = TimeSpan.FromMilliseconds(500);
});

// 使用统一的 ServerBootstrapper 配置服务器
builder.Services.AddPulseRpcServer(builder.Configuration, new ServerBootstrapperOptions
{
    EnableExternalListener = true,      // GameServer 需要外网监听（客户端连接）
    EnableInternalRpcChannel = true,    // 内网 RPC 通道
    EnableServiceDiscovery = true,      // Consul 服务注册与发现
    EnableMongoDb = true,               // MongoDB 数据库
    EnableSentry = true,                // Sentry 错误追踪（根据配置启用）
    // ✅ EnablePushServices 默认为 true，自动检测并配置推送服务
    // External 优先，因此 IHubContext<TReceiver> 会向客户端推送
    ConfigureServices = services =>
    {
        // 注册 JWT 认证服务（使用真实的 JWT 实现）
        services.AddSingleton<IJwtTokenService, JwtService>();
        services.AddSingleton<IAuthenticationService, AuthenticationService>();
        services.AddSingleton<PermissionValidator>();

        // 注册外部连接的 JWT 认证验证器（使用 PulseRPC.Server 提供的实现）
        services.AddSingleton<PulseRPC.Authentication.IAuthenticationValidator>(sp =>
        {
            var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
                ?? throw new InvalidOperationException("JWT configuration is missing");
            var logger = sp.GetRequiredService<ILogger<PulseRPC.Server.Security.JwtAuthenticationProvider>>();

            return new PulseRPC.Server.Security.JwtAuthenticationProvider(
                jwtOptions.SecretKey,
                jwtOptions.Issuer,
                jwtOptions.Audience,
                logger);
        });

        // 注册服务发现和本地服务注册表
        services.AddSingleton<ConsulServiceDiscovery>();
        services.AddSingleton<LocalServiceRegistry>();

        // 注册统一服务客户端管理器（强类型 Hub 代理的核心）
        services.AddSingleton<ServiceClientManager>();

        // ✅ 使用 AddPulseService 注册 Singleton 服务
        // 这样 Hub 可以通过 IServiceAccessor<TService> 访问服务，确保队列调度和线程安全
        services.AddPulseService<CharacterService>((sp, _) =>
            new CharacterService(
                sp.GetRequiredService<CharacterRepository>(),
                sp.GetRequiredService<ILogger<CharacterService>>()));

        services.AddPulseService<MailService>((sp, _) =>
            new MailService(
                sp.GetRequiredService<MailRepository>(),
                sp.GetRequiredService<CharacterRepository>(),
                sp.GetRequiredService<ILogger<MailService>>()));

        // 注册 Hub 服务（必须使用接口 + 实现的方式）
        // Hub 是无状态的，通过 IServiceAccessor 访问有状态的 Service
        services.AddSingleton<IGameHub, GameHub>();

        // 注册 GameServerInternalHub：先注册具体类型，再将接口映射到同一实例
        services.AddSingleton<GameServerInternalHub>();
        services.AddSingleton<IGameServerInternalHub>(sp => sp.GetRequiredService<GameServerInternalHub>());

        // 启动 IPulseService
        services.AddHostedService<PulseRPC.Server.Services.PulseServiceHostedService<GameServerInternalHub>>();

        // 添加后台服务：定期刷新服务列表
        services.AddSingleton<ServiceDiscoveryRefreshService>(sp =>
            new ServiceDiscoveryRefreshService(
                sp.GetRequiredService<ServiceClientManager>(),
                sp.GetRequiredService<ILogger<ServiceDiscoveryRefreshService>>(),
                TimeSpan.FromSeconds(30))); // 每30秒刷新一次
        services.AddHostedService(sp => sp.GetRequiredService<ServiceDiscoveryRefreshService>());
    }
});

var app = builder.Build();

// ✅ 服务客户端初始化已移至 Bootstrap 流程中（Phase 5.5）
// ServiceClientManager 会在 ServerBootstrapOrchestrator 的 Phase5_5 阶段自动初始化
// 这确保了：
// 1. 在 Consul 注册之前初始化，等待依赖服务（BackendServer, BattleServer）就绪
// 2. 只有当所有依赖服务可用时，才注册到 Consul 并开始接受请求
// 3. 避免因服务启动顺序导致的"BackendServer 不可用"错误
app.Services.GetRequiredService<ServiceClientManager>().RegisterHubProxyFactory(new HubProxyFactory());

await app.RunAsync();
