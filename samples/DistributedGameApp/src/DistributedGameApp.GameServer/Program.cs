using DistributedGameApp.GameServer;
using DistributedGameApp.GameServer.Authentication;
using DistributedGameApp.GameServer.Services;
using DistributedGameApp.Infrastructure.Consul;
using DistributedGameApp.Infrastructure.Hosting;
using DistributedGameApp.Infrastructure.ServiceClient;
using DistributedGameApp.Shared.Hubs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseRPC.Server;

var builder = Host.CreateApplicationBuilder(args);

// 配置 JWT 选项
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));

// 使用统一的 ServerBootstrapper 配置服务器
builder.Services.AddPulseRpcServer(builder.Configuration, new ServerBootstrapperOptions
{
    EnableExternalListener = true,      // GameServer 需要外网监听（客户端连接）
    EnableInternalRpcChannel = true,    // 内网 RPC 通道
    EnableServiceDiscovery = true,      // Consul 服务注册与发现
    EnableMongoDb = true,               // MongoDB 数据库
    EnableSentry = true,                // Sentry 错误追踪（根据配置启用）
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
            var logger = sp.GetRequiredService<ILogger<PulseRPC.Server.Authentication.JwtAuthenticationProvider>>();

            return new PulseRPC.Server.Authentication.JwtAuthenticationProvider(
                jwtOptions.SecretKey,
                jwtOptions.Issuer,
                jwtOptions.Audience,
                logger);
        });

        // 注册服务发现和本地服务注册表
        services.AddSingleton<ConsulServiceDiscovery>();
        services.AddSingleton<LocalServiceRegistry>();

        // 注册统一服务客户端管理器（强类型 Hub 代理的核心）
        services.AddSingleton<UnifiedServiceClientManager>();

        // 添加应用服务
        services.AddSingleton<CharacterService>();
        services.AddSingleton<MailService>();

        // 注册 Hub 服务（必须使用接口 + 实现的方式）
        services.AddSingleton<IGameHub, GameHub>();
        services.AddSingleton<IGameServerInternalHub, GameServerInternalHub>();

        // 启动 IPulseService
        services.AddHostedService<PulseRPC.Server.Services.PulseServiceHostedService<GameServerInternalHub>>();

        // 添加后台服务：定期刷新服务列表
        services.AddSingleton<ServiceDiscoveryRefreshService>(sp =>
            new ServiceDiscoveryRefreshService(
                sp.GetRequiredService<UnifiedServiceClientManager>(),
                sp.GetRequiredService<ILogger<ServiceDiscoveryRefreshService>>(),
                TimeSpan.FromSeconds(30))); // 每30秒刷新一次
        services.AddHostedService(sp => sp.GetRequiredService<ServiceDiscoveryRefreshService>());
    }
});

var app = builder.Build();

// 初始化 UnifiedServiceClientManager（通用版）
var serviceClientManager = app.Services.GetRequiredService<UnifiedServiceClientManager>();
await serviceClientManager.InitializeAsync(
    new[] { ServerType.Battle, ServerType.Backend },  // BackendServer 主要连接 BattleServer
    RoutingStrategy.ConsistentHash);

await app.RunAsync();
