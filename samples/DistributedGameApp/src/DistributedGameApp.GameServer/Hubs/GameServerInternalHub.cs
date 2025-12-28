using DistributedGameApp.Shared.Domain.Matchmaking;
using DistributedGameApp.Shared.Hubs;
using DistributedGameApp.Shared.Receivers;
using Microsoft.Extensions.Logging;
using PulseRPC;
using PulseRPC.Server;
using PulseRPC.Server.Abstractions;
using PulseRPC.Server.Services;
using System.Collections.Concurrent;

namespace DistributedGameApp.GameServer.Hubs;

/// <summary>
/// GameServer 内部 RPC Hub 实现 - 接收其他服务器的回调通知
/// </summary>
/// <remarks>
/// <para><strong>职责</strong>:</para>
/// <list type="bullet">
/// <item><description>接收 BackendServer 的匹配成功回调</description></item>
/// <item><description>维护玩家的连接状态（有状态）</description></item>
/// <item><description>使用 IHubContext&lt;IGameReceiver&gt; 转发通知给客户端</description></item>
/// </list>
/// <para><strong>设计原则</strong>:</para>
/// <list type="bullet">
/// <item><description>✅ 继承 UnifiedPulseServiceBase - 因为需要管理连接映射状态</description></item>
/// <item><description>✅ 全局单例 (ServiceId = "Global")</description></item>
/// <item><description>✅ 线程安全的状态管理 (使用消息队列)</description></item>
/// </list>
/// </remarks>
[PulseService(
    StartupType = ServiceStartupType.AutoStart,
    InstanceScope = ServiceInstanceScope.Singleton,
    SchedulingMode = ServiceSchedulingMode.DedicatedQueue,
    DisplayName = "GameServerInternalHub",
    EnableHealthCheck = true)]
public class GameServerInternalHub : UnifiedPulseServiceBase, IGameServerInternalHub, IUnifiedServiceLifecycle
{
    private readonly IHubContext<IGameReceiver> _gameReceiverContext;
    private readonly ConcurrentDictionary<string, string> _playerToConnectionMap = new();

    public GameServerInternalHub(
        IHubContext<IGameReceiver> gameReceiverContext,
        ILogger<GameServerInternalHub> logger)
        : base("GameServerInternalHub", "Global", logger)
    {
        _gameReceiverContext = gameReceiverContext;
    }

    /// <summary>
    /// 注册玩家连接（由 GameHub 登录时调用）
    /// </summary>
    public void RegisterPlayerConnection(string playerId, string connectionId)
    {
        _playerToConnectionMap[playerId] = connectionId;
        Logger.LogDebug("Player connection registered: {PlayerId} → {ConnectionId}", playerId, connectionId);
    }

    /// <summary>
    /// 注销玩家连接（由 GameHub 断开时调用）
    /// </summary>
    public void UnregisterPlayerConnection(string playerId)
    {
        _playerToConnectionMap.TryRemove(playerId, out _);
        Logger.LogDebug("Player connection unregistered: {PlayerId}", playerId);
    }

    /// <summary>
    /// 匹配成功回调 - BackendServer 调用
    /// </summary>
    public async Task<bool> OnMatchFoundAsync(string playerId, MatchFoundNotification notification)
    {
        try
        {
            // 检查玩家是否在线
            if (!_playerToConnectionMap.ContainsKey(playerId))
            {
                Logger.LogWarning("Player not connected: {PlayerId}", playerId);
                return false;
            }

            // 转换为客户端期望的通知类型
            var clientNotification = new DistributedGameApp.Shared.Messages.MatchFoundNotification
            {
                BattleId = notification.BattleRoomId,
                BattleServerAddress = notification.BattleServerHost,
                BattleServerPort = notification.BattleServerPort,
                Countdown = 10
            };

            // ✅ 使用 IHubContext<IGameReceiver> 推送给客户端
            // 协议号由源生成器自动生成，无需手动计算
            await _gameReceiverContext.Clients.User(playerId).OnMatchFoundAsync(clientNotification);

            Logger.LogInformation(
                "Match found notification sent via IHubContext - PlayerId: {PlayerId}, MatchId: {MatchId}, BattleRoom: {RoomId}",
                playerId, notification.MatchId, notification.BattleRoomId);

            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to send match found notification - PlayerId: {PlayerId}", playerId);
            return false;
        }
    }

    /// <summary>
    /// 匹配取消回调
    /// </summary>
    public async Task<bool> OnMatchCancelledAsync(string playerId, string reason)
    {
        try
        {
            if (!_playerToConnectionMap.ContainsKey(playerId))
            {
                Logger.LogWarning("Player not connected: {PlayerId}", playerId);
                return false;
            }

            // ✅ 使用 IHubContext<IGameReceiver> 推送给客户端
            await _gameReceiverContext.Clients.User(playerId).OnMatchCancelledAsync(reason);

            Logger.LogInformation(
                "Match cancelled notification sent via IHubContext - PlayerId: {PlayerId}, Reason: {Reason}",
                playerId, reason);

            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to send match cancelled notification - PlayerId: {PlayerId}", playerId);
            return false;
        }
    }

    public override Task OnStartingAsync(CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("GameServerInternalHub starting...");
        return Task.CompletedTask;
    }

    public override Task OnStoppingAsync(CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("GameServerInternalHub stopping...");
        _playerToConnectionMap.Clear();
        return Task.CompletedTask;
    }

    public override Task<ServiceHealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        var playerCount = _playerToConnectionMap.Count;
        Logger.LogDebug("GameServerInternalHub health check: {PlayerCount} players connected", playerCount);

        return Task.FromResult(ServiceHealthCheckResult.Healthy($"{playerCount} players connected"));
    }
}
