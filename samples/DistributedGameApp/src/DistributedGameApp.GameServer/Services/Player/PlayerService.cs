using DistributedGameApp.Infrastructure.MongoDB.Repositories;
using DistributedGameApp.Shared.Domain.Characters;
using DistributedGameApp.Shared.Hubs;
using DistributedGameApp.Shared.Messages;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Services;
using PulseRPC.Server.Services.Management;

namespace DistributedGameApp.GameServer.Services.Player;

/// <summary>
/// 玩家服务 - 有状态的服务实例
/// </summary>
/// <remarks>
/// <para><strong>设计要点</strong>:</para>
/// <list type="bullet">
/// <item><description>每个玩家一个 Service 实例（MultiInstance）</description></item>
/// <item><description>按需创建（OnDemand），玩家上线时创建</description></item>
/// <item><description>使用 StatefulIO 场景，启用 Yielding 机制</description></item>
/// <item><description>所有状态操作都在队列中执行，天然线程安全</description></item>
/// </list>
/// <para><strong>文件组织</strong>:</para>
/// <code>
/// Services/Player/
/// ├── PlayerService.cs           // 主文件：基类、构造函数、状态
/// ├── PlayerService.Character.cs // partial: 角色相关操作
/// ├── PlayerService.Inventory.cs // partial: 背包相关操作
/// └── PlayerService.Combat.cs    // partial: 战斗相关操作
/// </code>
/// </remarks>
[PulseService(
    Scenario = ServiceScenario.StatefulIO,  // 有状态 + IO 密集，自动启用 Yielding
    StartupType = ServiceStartupType.OnDemand,
    InstanceScope = ServiceInstanceScope.MultiInstance,
    DisplayName = "Player",
    IdleTimeoutSeconds = 600,  // 10分钟空闲后回收
    EnableHealthCheck = true)]
[ImplementsHub(typeof(IPlayerHub), FileSuffix = "Player")]
[ImplementsHub(typeof(IGameHub), FileSuffix = "Game")]
public partial class PlayerService : UnifiedPulseServiceBase, IUnifiedServiceLifecycle
{
    private readonly ILogger<PlayerService> _logger;
    private readonly CharacterRepository _characterRepository;

    // ========================================
    // 玩家状态（只在队列线程中访问，无需加锁）
    // ========================================

    /// <summary>
    /// 玩家 ID（即 ServiceId）
    /// </summary>
    public string PlayerId => ServiceId;

    /// <summary>
    /// 当前选中的角色
    /// </summary>
    private Character? _currentCharacter;

    /// <summary>
    /// 玩家位置
    /// </summary>
    private Position _position = new() { MapId = "default", X = 0, Y = 0, Z = 0 };

    /// <summary>
    /// 在线状态
    /// </summary>
    private OnlineStatus _onlineStatus = OnlineStatus.Offline;

    /// <summary>
    /// 最后活跃时间
    /// </summary>
    private DateTime _lastActiveTime = DateTime.UtcNow;

    /// <summary>
    /// 连接 ID（用于推送消息）
    /// </summary>
    private string? _connectionId;

    public PlayerService(
        string playerId,
        ILogger<PlayerService> logger,
        CharacterRepository characterRepository)
        : base("Player", playerId, logger)
    {
        _logger = logger;
        _characterRepository = characterRepository;
    }

    // ========================================
    // 生命周期
    // ========================================

    public override async Task OnStartingAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("PlayerService starting: {PlayerId}", PlayerId);

        // 加载玩家数据
        var characters = await _characterRepository.GetByUserIdAsync(PlayerId);
        if (characters.Count > 0)
        {
            _currentCharacter = characters[0]; // 默认选第一个角色
        }

        _onlineStatus = OnlineStatus.Online;
        _lastActiveTime = DateTime.UtcNow;

        _logger.LogInformation("PlayerService started: {PlayerId}, Character: {CharacterName}",
            PlayerId, _currentCharacter?.Name ?? "None");
    }

    public override async Task OnStoppingAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("PlayerService stopping: {PlayerId}", PlayerId);

        // 保存玩家数据
        if (_currentCharacter != null)
        {
            _currentCharacter.LastOnlineAt = DateTime.UtcNow;
            await _characterRepository.UpdateAsync(_currentCharacter);
        }

        _onlineStatus = OnlineStatus.Offline;

        _logger.LogInformation("PlayerService stopped: {PlayerId}", PlayerId);
    }

    public override Task<ServiceHealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        // 检查是否长时间未活动
        var idleTime = DateTime.UtcNow - _lastActiveTime;
        if (idleTime > TimeSpan.FromMinutes(30))
        {
            return Task.FromResult(ServiceHealthCheckResult.Unhealthy(
                $"Player idle for {idleTime.TotalMinutes:F1} minutes"));
        }

        return Task.FromResult(ServiceHealthCheckResult.Healthy());
    }

    // ========================================
    // 连接管理
    // ========================================

    /// <summary>
    /// 设置连接 ID（玩家登录时调用）
    /// </summary>
    public void SetConnectionId(string connectionId)
    {
        _connectionId = connectionId;
        _lastActiveTime = DateTime.UtcNow;
    }

    /// <summary>
    /// 清除连接 ID（玩家断开时调用）
    /// </summary>
    public void ClearConnectionId()
    {
        _connectionId = null;
    }

    // ========================================
    // 内部辅助方法
    // ========================================

    /// <summary>
    /// 更新活跃时间
    /// </summary>
    protected void UpdateActiveTime()
    {
        _lastActiveTime = DateTime.UtcNow;
    }

    /// <summary>
    /// 获取当前角色（确保已登录）
    /// </summary>
    protected Character? GetCurrentCharacter()
    {
        UpdateActiveTime();
        return _currentCharacter;
    }
}

