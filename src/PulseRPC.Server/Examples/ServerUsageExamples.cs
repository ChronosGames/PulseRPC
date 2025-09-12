using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Builder;
using PulseRPC.Server.Extensions;
using PulseRPC.Server.Sessions;

namespace PulseRPC.Server.Examples;

/// <summary>
/// PulseRPC Server 使用示例 - 三层抽象架构增强版
/// 演示如何使用新的IClientSession接口和会话管理功能
/// </summary>
public static class ServerUsageExamples
{
    /// <summary>
    /// 示例1: 基本的增强服务端设置
    /// </summary>
    public static void BasicEnhancedServerSetup()
    {
        var services = new ServiceCollection();

        // 配置日志
        services.AddLogging(builder => builder.AddConsole());

        // 使用增强的PulseRPC服务端（三层抽象架构）
        services.AddEnhancedPulseRPCServer(options =>
        {
            options.EnableSessionManagement = true;
            options.SessionTimeoutMs = 300000; // 5分钟
            options.HealthCheckIntervalMs = 30000; // 30秒
            options.EnableAutoSessionCleanup = true;
        });

        // 添加会话管理功能
        services.AddClientSessionManagement(options =>
        {
            options.MaxConcurrentSessions = 1000;
            options.EnableSessionStatistics = true;
            options.EnableSessionEvents = true;
        });

        // 添加健康检查
        services.AddSessionHealthChecks(options =>
        {
            options.CheckIntervalMs = 30000;
            options.MaxUnhealthyDurationMs = 120000;
            options.EnableAutoCleanup = true;
        });

        // 添加广播服务
        services.AddSessionBroadcast();

        var serviceProvider = services.BuildServiceProvider();
        var sessionManager = serviceProvider.GetRequiredService<IServerSessionManager>();

        Console.WriteLine("增强PulseRPC服务端已配置完成");
        Console.WriteLine($"当前活动会话数: {sessionManager.SessionCount}");
    }

    /// <summary>
    /// 示例2: 会话管理和广播功能演示
    /// </summary>
    public static async Task SessionManagementAndBroadcastExample()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddEnhancedPulseRPCServer();
        services.AddSessionBroadcast();

        var serviceProvider = services.BuildServiceProvider();
        var sessionManager = serviceProvider.GetRequiredService<IServerSessionManager>();
        var broadcastService = serviceProvider.GetRequiredService<ISessionBroadcastService>();

        // 模拟会话操作
        Console.WriteLine("=== 会话管理示例 ===");

        // 获取所有可用会话
        var availableSessions = sessionManager.GetAvailableSessions();
        Console.WriteLine($"可用会话数: {availableSessions.Count()}");

        // 按用户分组获取会话
        var userSessions = sessionManager.GetSessionsByUser("testuser");
        Console.WriteLine($"用户 'testuser' 的会话数: {userSessions.Count()}");

        // 按组获取会话
        var adminSessions = sessionManager.GetSessionsByGroup("admin");
        Console.WriteLine($"管理员组会话数: {adminSessions.Count()}");

        // 广播消息到所有已认证用户
        try
        {
            var broadcastCount = await broadcastService.BroadcastToAllAsync<INotificationHub>(
                "SendNotification",
                new object[] { "系统通知", "服务器维护将在10分钟后开始" });

            Console.WriteLine($"成功广播到 {broadcastCount} 个会话");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"广播失败: {ex.Message}");
        }

        // 向特定组广播
        try
        {
            var groupBroadcastCount = await broadcastService.BroadcastToGroupAsync<INotificationHub>(
                "admin",
                "SendAdminAlert",
                new object[] { "管理员警报", "检测到异常活动" });

            Console.WriteLine($"成功向管理员组广播到 {groupBroadcastCount} 个会话");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"组广播失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 示例3: 健康检查和会话清理
    /// </summary>
    public static async Task HealthCheckAndCleanupExample()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddEnhancedPulseRPCServer();
        services.AddSessionHealthChecks();

        var serviceProvider = services.BuildServiceProvider();
        var sessionManager = serviceProvider.GetRequiredService<IServerSessionManager>();
        var healthChecker = serviceProvider.GetRequiredService<ISessionHealthChecker>();

        Console.WriteLine("=== 健康检查和清理示例 ===");

        // 获取统计信息
        var stats = sessionManager.GetSessionManagerStats();
        Console.WriteLine($"活动会话: {stats.ActiveSessions}");
        Console.WriteLine($"健康会话: {stats.HealthySessions}");
        Console.WriteLine($"降级会话: {stats.DegradedSessions}");
        Console.WriteLine($"不健康会话: {stats.UnhealthySessions}");
        Console.WriteLine($"失败会话: {stats.FailedSessions}");

        // 检查所有会话健康状态
        try
        {
            var healthResults = await sessionManager.CheckAllSessionsHealthAsync();

            Console.WriteLine("健康检查结果:");
            foreach (var result in healthResults)
            {
                Console.WriteLine($"  会话 {result.SessionId}: {result.Health} - {result.Message}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"健康检查失败: {ex.Message}");
        }

        // 清理不健康的会话
        try
        {
            var cleanedCount = await sessionManager.CleanupUnhealthySessionsAsync(TimeSpan.FromMinutes(2));
            Console.WriteLine($"清理了 {cleanedCount} 个不健康的会话");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"清理失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 示例4: 完整的服务端应用程序
    /// </summary>
    public static async Task CompleteServerApplicationExample()
    {
        Console.WriteLine("=== 完整服务端应用示例 ===");

        // 创建Host Builder
        var builder = Host.CreateApplicationBuilder();

        // 配置增强的PulseRPC服务端
        builder.Services.AddEnhancedPulseRPCServer(options =>
        {
            options.EnableSessionManagement = true;
            options.SessionTimeoutMs = 300000; // 5分钟
            options.HealthCheckIntervalMs = 30000; // 30秒
            options.EnableAutoSessionCleanup = true;
        });

        // 添加业务服务
        builder.Services.AddClientSessionManagement();
        builder.Services.AddSessionHealthChecks();
        builder.Services.AddSessionBroadcast();

        // 注册示例Hub服务
        builder.Services.AddTransient<INotificationHub, NotificationHubService>();
        builder.Services.AddTransient<IChatHub, ChatHubService>();

        // 添加托管服务
        builder.Services.AddHostedService<ServerBackgroundService>();

        var app = builder.Build();

        // 启动应用
        Console.WriteLine("启动增强PulseRPC服务端...");

        // 运行应用（在实际应用中，这会是一个长时间运行的服务）
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        try
        {
            await app.RunAsync(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("应用程序已正常关闭");
        }
    }

    /// <summary>
    /// 示例5: 会话事件处理
    /// </summary>
    public static void SessionEventHandlingExample()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddEnhancedPulseRPCServer();

        var serviceProvider = services.BuildServiceProvider();
        var sessionManager = serviceProvider.GetRequiredService<IServerSessionManager>();

        Console.WriteLine("=== 会话事件处理示例 ===");

        // 订阅会话事件
        sessionManager.SessionCreated += (sender, e) =>
        {
            Console.WriteLine($"会话已创建: {e.Session.Descriptor.Id} (连接ID: {e.Session.ConnectionId})");

            // 可以在这里进行初始化操作，如设置默认组或标签
            e.Session.SetGroups(new[] { "default" });
            e.Session.SetTag("created_at", DateTime.UtcNow.ToString("O"));
        };

        sessionManager.SessionRemoved += (sender, e) =>
        {
            Console.WriteLine($"会话已移除: {e.Session.Descriptor.Id}");

            // 可以在这里进行清理操作
            var duration = DateTime.UtcNow - e.Session.Statistics.CreatedAt;
            Console.WriteLine($"  会话持续时间: {duration.TotalMinutes:F1} 分钟");
            Console.WriteLine($"  发送消息数: {e.Session.Statistics.MessagesSent}");
            Console.WriteLine($"  接收消息数: {e.Session.Statistics.MessagesReceived}");
        };

        sessionManager.SessionAuthenticated += (sender, e) =>
        {
            Console.WriteLine($"会话已认证: {e.Session.Descriptor.Id} - 用户: {e.AuthenticationContext.Name}");

            // 根据用户角色设置组
            if (e.AuthenticationContext.Name?.Contains("admin") == true)
            {
                e.Session.SetGroups(new[] { "admin", "authenticated" });
            }
            else
            {
                e.Session.SetGroups(new[] { "authenticated" });
            }
        };

        Console.WriteLine("会话事件监听器已注册");
    }
}

#region 示例Hub接口和实现

/// <summary>
/// 通知Hub接口 - 用于服务端向客户端推送通知
/// </summary>
public interface INotificationHub : IPulseHub
{
    /// <summary>
    /// 发送通知消息
    /// </summary>
    /// <param name="title">标题</param>
    /// <param name="message">消息内容</param>
    Task SendNotification(string title, string message);

    /// <summary>
    /// 发送管理员警报
    /// </summary>
    /// <param name="title">警报标题</param>
    /// <param name="message">警报内容</param>
    Task SendAdminAlert(string title, string message);
}

/// <summary>
/// 通知Hub服务实现
/// </summary>
internal class NotificationHubService : INotificationHub
{
    private readonly ILogger<NotificationHubService> _logger;

    public NotificationHubService(ILogger<NotificationHubService> logger)
    {
        _logger = logger;
    }

    public Task SendNotification(string title, string message)
    {
        _logger.LogInformation("发送通知: {Title} - {Message}", title, message);
        return Task.CompletedTask;
    }

    public Task SendAdminAlert(string title, string message)
    {
        _logger.LogWarning("管理员警报: {Title} - {Message}", title, message);
        return Task.CompletedTask;
    }
}

/// <summary>
/// 聊天Hub接口
/// </summary>
public interface IChatHub : IPulseHub
{
    /// <summary>
    /// 发送聊天消息
    /// </summary>
    /// <param name="username">用户名</param>
    /// <param name="message">消息内容</param>
    Task SendChatMessage(string username, string message);

    /// <summary>
    /// 用户加入聊天室
    /// </summary>
    /// <param name="username">用户名</param>
    Task UserJoinedChat(string username);

    /// <summary>
    /// 用户离开聊天室
    /// </summary>
    /// <param name="username">用户名</param>
    Task UserLeftChat(string username);
}

/// <summary>
/// 聊天Hub服务实现
/// </summary>
internal class ChatHubService : IChatHub
{
    private readonly ILogger<ChatHubService> _logger;

    public ChatHubService(ILogger<ChatHubService> logger)
    {
        _logger = logger;
    }

    public Task SendChatMessage(string username, string message)
    {
        _logger.LogInformation("聊天消息 - {Username}: {Message}", username, message);
        return Task.CompletedTask;
    }

    public Task UserJoinedChat(string username)
    {
        _logger.LogInformation("用户加入聊天: {Username}", username);
        return Task.CompletedTask;
    }

    public Task UserLeftChat(string username)
    {
        _logger.LogInformation("用户离开聊天: {Username}", username);
        return Task.CompletedTask;
    }
}

/// <summary>
/// 服务端后台服务 - 演示周期性任务
/// </summary>
internal class ServerBackgroundService : BackgroundService
{
    private readonly IServerSessionManager _sessionManager;
    private readonly ISessionBroadcastService _broadcastService;
    private readonly ILogger<ServerBackgroundService> _logger;

    public ServerBackgroundService(
        IServerSessionManager sessionManager,
        ISessionBroadcastService broadcastService,
        ILogger<ServerBackgroundService> logger)
    {
        _sessionManager = sessionManager;
        _broadcastService = broadcastService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("服务端后台服务已启动");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // 每分钟输出统计信息
                var stats = _sessionManager.GetSessionManagerStats();
                _logger.LogInformation("会话统计 - 活动: {Active}, 健康: {Healthy}, 不健康: {Unhealthy}",
                    stats.ActiveSessions, stats.HealthySessions, stats.UnhealthySessions);

                // 如果有活动会话，发送心跳通知
                if (stats.ActiveSessions > 0)
                {
                    var heartbeatCount = await _broadcastService.BroadcastToAllAsync<INotificationHub>(
                        "SendNotification",
                        new object[] { "心跳", $"服务器时间: {DateTime.Now:HH:mm:ss}" });

                    _logger.LogDebug("心跳通知已发送到 {Count} 个会话", heartbeatCount);
                }

                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "后台服务执行异常");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }

        _logger.LogInformation("服务端后台服务已停止");
    }
}

#endregion