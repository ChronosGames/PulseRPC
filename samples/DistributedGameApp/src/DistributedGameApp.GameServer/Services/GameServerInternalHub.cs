using DistributedGameApp.Shared.Domain.Matchmaking;
using DistributedGameApp.Shared.Hubs;
using DistributedGameApp.Shared.Receivers;
using Microsoft.Extensions.Logging;
using PulseRPC;
using PulseRPC.Server;
using PulseRPC.Server.Abstractions;
using System.Collections.Concurrent;

namespace DistributedGameApp.GameServer.Services;

/// <summary>
/// GameServer 内部 RPC Hub 实现 - 接收其他服务器的回调通知
/// </summary>
/// <remarks>
/// 职责：
/// - 接收 BackendServer 的匹配成功回调
/// - 维护玩家的连接状态
/// - 使用 IHubContext&lt;IGameReceiver&gt; 转发通知给客户端（协议号由源生成器自动生成）
/// </remarks>
public class GameServerInternalHub : BaseService, IGameServerInternalHub, IPulseService
{
    private readonly IHubContext<IGameReceiver> _gameReceiverContext;
    private readonly ConcurrentDictionary<string, string> _playerToConnectionMap = new();

    public string ServiceName => "GameServerInternalHub";
    public string ServiceId => "GameServerInternalHub:Global";

    public GameServerInternalHub(
        IHubContext<IGameReceiver> gameReceiverContext,
        ILogger<GameServerInternalHub> logger,
        IAuthenticationService authenticationService,
        PermissionValidator permissionValidator)
        : base(logger, authenticationService, permissionValidator)
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
}
