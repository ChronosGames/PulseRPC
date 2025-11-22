using DistributedGameApp.BattleServer.Services;
using DistributedGameApp.Infrastructure.Authentication;
using DistributedGameApp.Infrastructure.Hosting;
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
    EnableExternalListener = true,      // BattleServer 需要外网监听（客户端直接连接）
    EnableInternalRpcChannel = true,    // 内网 RPC 通道
    EnableServiceDiscovery = true,      // Consul 服务注册与发现
    EnableMongoDb = false,              // BattleServer 不直接访问数据库
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
