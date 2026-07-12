using DistributedGameApp.Shared.Domain.Matchmaking;
using DistributedGameApp.Shared.Hubs;
using DistributedGameApp.Shared.Receivers;
using Microsoft.Extensions.Logging;
using PulseRPC.Server;

namespace DistributedGameApp.GameServer.Hubs;

/// <summary>
/// GameServer 内部 RPC Hub 实现 - 接收其他服务器的回调通知
/// </summary>
/// <remarks>
/// <para><strong>职责</strong>:</para>
/// <list type="bullet">
/// <item><description>接收 BackendServer 的匹配成功回调</description></item>
/// <item><description>使用 IHubContext&lt;IGameReceiver&gt; 转发通知给客户端</description></item>
/// </list>
/// 该 Hub 自身无状态；共享连接状态由 <see cref="IUserConnectionMapping"/> 管理，
/// 因此使用标准 DI 单例，不创建平行的 <c>IPulseService</c> 生命周期。
/// </remarks>
public sealed class GameServerInternalHub : IGameServerInternalHub
{
    private readonly IHubContext<IGameReceiver> _gameReceiverContext;
    private readonly IUserConnectionMapping _userConnectionMapping;
    private readonly ILogger<GameServerInternalHub> _logger;

    public GameServerInternalHub(
        IHubContext<IGameReceiver> gameReceiverContext,
        IUserConnectionMapping userConnectionMapping,
        ILogger<GameServerInternalHub> logger)
    {
        _gameReceiverContext = gameReceiverContext;
        _userConnectionMapping = userConnectionMapping;
        _logger = logger;
    }

    /// <summary>
    /// 匹配成功回调 - BackendServer 调用
    /// </summary>
    public async Task<bool> OnMatchFoundAsync(string playerId, MatchFoundNotification notification)
    {
        try
        {
            // 使用框架提供的 IUserConnectionMapping 检查玩家是否在线
            if (!_userConnectionMapping.IsUserOnline(playerId))
            {
                _logger.LogWarning("Player not connected: {PlayerId}", playerId);
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

            _logger.LogInformation(
                "Match found notification sent via IHubContext - PlayerId: {PlayerId}, MatchId: {MatchId}, BattleRoom: {RoomId}",
                playerId, notification.MatchId, notification.BattleRoomId);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send match found notification - PlayerId: {PlayerId}", playerId);
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
            if (!_userConnectionMapping.IsUserOnline(playerId))
            {
                _logger.LogWarning("Player not connected: {PlayerId}", playerId);
                return false;
            }

            // ✅ 使用 IHubContext<IGameReceiver> 推送给客户端
            await _gameReceiverContext.Clients.User(playerId).OnMatchCancelledAsync(reason);

            _logger.LogInformation(
                "Match cancelled notification sent via IHubContext - PlayerId: {PlayerId}, Reason: {Reason}",
                playerId, reason);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send match cancelled notification - PlayerId: {PlayerId}", playerId);
            return false;
        }
    }
}
