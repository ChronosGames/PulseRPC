using DistributedGameApp.Shared.Hubs;
using DistributedGameApp.Shared.Messages;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Abstractions;
using PulseRPC.Server.ServiceManagement;

namespace DistributedGameApp.GameServer.Services.Player;

/// <summary>
/// 玩家 Hub - 无状态单例，全局复用
/// </summary>
/// <remarks>
/// <para><strong>关键设计</strong>：</para>
/// <list type="bullet">
/// <item><description>✅ 无状态，注册为 Singleton</description></item>
/// <item><description>✅ 所有状态存放在 PlayerService 中</description></item>
/// <item><description>✅ 通过 PulseContext.Current.UserId 获取用户 ID</description></item>
/// </list>
/// <para><strong>工作原理</strong>：</para>
/// <code>
/// 请求到达 → 框架设置 PulseContext.Current (包含 UserId)
///     ↓
/// PlayerHub.Method() → _playerService.ExecuteAsync(userId, ...)
///     ↓
/// UnifiedServiceManager.Get(userId) → 返回 PlayerService 实例
/// </code>
/// </remarks>
public class PlayerHub : IPlayerHub
{
    private readonly IServiceAccessor<PlayerService> _playerService;
    private readonly ILogger<PlayerHub> _logger;

    public PlayerHub(
        IServiceAccessor<PlayerService> playerService,
        ILogger<PlayerHub> logger)
    {
        _playerService = playerService ?? throw new ArgumentNullException(nameof(playerService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 获取当前用户 ID
    /// </summary>
    private static string GetCurrentUserId()
    {
        var userId = PulseContext.Current?.UserId;
        if (string.IsNullOrEmpty(userId))
        {
            throw new InvalidOperationException("User not authenticated");
        }
        return userId;
    }

    // ════════════════════════════════════════════════════════════════════════
    // IPlayerHub 实现
    // 所有方法从 PulseContext.Current.UserId 获取用户 ID 定位 PlayerService
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 获取玩家信息
    /// </summary>
    public async Task<PlayerInfo?> GetPlayerInfoAsync()
    {
        try
        {
            var userId = GetCurrentUserId();
            return await _playerService.ExecuteAsync(
                userId,
                service => service.GetPlayerInfoAsync());
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "User not authenticated");
            return null;
        }
    }

    /// <summary>
    /// 移动玩家
    /// </summary>
    public async Task<MoveResult> MoveAsync(MoveRequest request)
    {
        if (request == null)
        {
            return new MoveResult { Success = false, ErrorMessage = "Request is null" };
        }

        try
        {
            var userId = GetCurrentUserId();
            return await _playerService.ExecuteAsync(
                userId,
                service => service.MoveAsync(request));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "User not authenticated");
            return new MoveResult { Success = false, ErrorMessage = "Not authenticated" };
        }
    }

    /// <summary>
    /// 升级
    /// </summary>
    public async Task<PlayerInfo?> LevelUpAsync()
    {
        try
        {
            var userId = GetCurrentUserId();
            return await _playerService.ExecuteAsync(
                userId,
                service => service.LevelUpAsync());
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "User not authenticated");
            return null;
        }
    }

    /// <summary>
    /// 增加经验
    /// </summary>
    public async Task<PlayerInfo?> AddExpAsync(long exp)
    {
        if (exp <= 0) return null;

        try
        {
            var userId = GetCurrentUserId();
            return await _playerService.ExecuteAsync(
                userId,
                service => service.AddExpAsync(exp));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "User not authenticated");
            return null;
        }
    }
}
