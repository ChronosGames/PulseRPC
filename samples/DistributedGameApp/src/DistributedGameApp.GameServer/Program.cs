using DistributedGameApp.GameServer.Services;
using DistributedGameApp.Infrastructure.Hosting;
using DistributedGameApp.Shared.Hubs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// 使用统一的 ServerBootstrapper 配置服务器
builder.Services.AddPulseRpcServer(builder.Configuration, new ServerBootstrapperOptions
{
    EnableExternalListener = true,      // GameServer 需要外网监听（玩家连接）
    EnableInternalRpcChannel = true,    // 内网 RPC 通道（服务器间通信）
    EnableServiceDiscovery = true,      // Consul 服务注册与发现
    EnableMongoDb = true,               // MongoDB 数据库
    EnableSentry = true,                // Sentry 错误追踪（根据配置启用）
    ConfigureServices = services =>
    {
        // 添加应用特定服务
        services.AddSingleton<CharacterService>();
        services.AddSingleton<MailService>();
        services.AddSingleton<BackendServerClient>();

        // 注册 Hub 服务（必须使用接口 + 实现的方式）
        services.AddSingleton<IGameHub, GameHub>();

        // 添加后台服务：BackendServerClient 初始化
        services.AddHostedService<BackendServerClientInitializationService>();
    }
});

var app = builder.Build();

await app.RunAsync();

/// <summary>
/// BackendServerClient 初始化后台服务
/// </summary>
public class BackendServerClientInitializationService : BackgroundService
{
    private readonly BackendServerClient _backendServerClient;
    private readonly ILogger<BackendServerClientInitializationService> _logger;

    public BackendServerClientInitializationService(
        BackendServerClient backendServerClient,
        ILogger<BackendServerClientInitializationService> logger)
    {
        _backendServerClient = backendServerClient;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // 等待一段时间确保服务器已启动
            await Task.Delay(2000, stoppingToken);

            _logger.LogInformation("正在初始化 BackendServerClient...");

            // 初始化客户端连接
            await _backendServerClient.InitializeAsync(stoppingToken);

            _logger.LogInformation("BackendServerClient 初始化完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BackendServerClient 初始化失败");
        }

        // 保持运行
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
