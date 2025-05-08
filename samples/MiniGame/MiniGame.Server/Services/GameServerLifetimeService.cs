using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseRPC.Samples.Shared;
using PulseRPC.Server;

namespace PulseRPC.Samples.Server.Services;

/// <summary>
/// 游戏服务器生命周期管理服务
/// </summary>
public class GameServerLifetimeService : IHostedService
{
    private readonly ILogger<GameServerLifetimeService> _logger;
    private readonly NotificationService _notificationService;
    private readonly ServerInitializer _serverInitializer;

    /// <summary>
    /// 初始化游戏服务器生命周期管理服务
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="notificationService">通知服务</param>
    /// <param name="serverInitializer">服务器初始化器</param>
    public GameServerLifetimeService(
        ILogger<GameServerLifetimeService> logger,
        NotificationService notificationService,
        ServerInitializer serverInitializer)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _serverInitializer = serverInitializer ?? throw new ArgumentNullException(nameof(serverInitializer));
    }

    /// <summary>
    /// 启动服务
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("正在启动游戏服务器...");

        try
        {
            // 初始化服务器（注册所有处理器）
            _serverInitializer.Initialize();

            // 发送服务器启动通知
            await _notificationService.SendSystemNotificationAsync(
                "系统通知",
                "游戏服务器已启动，欢迎游戏！",
                NotificationType.System,
                DateTimeOffset.Now.AddDays(1).ToUnixTimeMilliseconds());

            _logger.LogInformation("游戏服务器启动完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "游戏服务器启动失败");
            throw;
        }
    }

    /// <summary>
    /// 停止服务
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("正在停止游戏服务器...");

        try
        {
            // 发送服务器关闭通知
            await _notificationService.SendSystemNotificationAsync(
                "系统通知",
                "游戏服务器正在关闭，请稍后重新连接...",
                NotificationType.Maintenance,
                DateTimeOffset.Now.AddMinutes(30).ToUnixTimeMilliseconds());

            _logger.LogInformation("游戏服务器已停止");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "游戏服务器停止过程中发生错误");
        }
    }
}
