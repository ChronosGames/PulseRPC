using DistributedGameApp.Shared.Hubs;
using DistributedGameApp.Shared.Messages;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Hubs;
using PulseRPC.Server.Services;
using PulseRPC.Server.Services.Management;

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
/// PulseServiceManager.Get(userId) → 返回 PlayerService 实例
/// </code>
/// </remarks>
public class PlayerHub : PulseHubBase, IPlayerHub
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

    // ════════════════════════════════════════════════════════════════════════
    // IPlayerHub 实现
    // 所有方法使用 ExecuteForCurrentUser 从 PulseContext 获取用户 ID
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 获取玩家信息
    /// </summary>
    public async Task<PlayerInfo?> GetPlayerInfoAsync()
    {
        try
        {
            return await _playerService.ExecuteForCurrentUser(
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
            return await _playerService.ExecuteForCurrentUser(
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
            return await _playerService.ExecuteForCurrentUser(
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
            return await _playerService.ExecuteForCurrentUser(
                service => service.AddExpAsync(exp));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "User not authenticated");
            return null;
        }
    }
}
