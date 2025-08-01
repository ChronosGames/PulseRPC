using Microsoft.Extensions.Logging;
using PulseRPC.Server;
using GameApp.Shared.Services;

namespace GameApp.GameServer.Services;

/// <summary>
/// 玩家事件发布器实现
/// </summary>
public class PlayerEventPublisher : IPlayerEventPublisher
{
    private readonly IPulseEventPublisher _eventPublisher;
    private readonly ILogger<PlayerEventPublisher> _logger;

    public PlayerEventPublisher(IPulseEventPublisher eventPublisher, ILogger<PlayerEventPublisher> logger)
    {
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    /// <summary>
    /// 发布玩家状态更新事件
    /// </summary>
    public async Task PublishPlayerStatusUpdateAsync(int playerId, PlayerStatusUpdateEvent eventData)
    {
        try
        {
            // 发送给特定玩家
            await _eventPublisher.PublishAsync<IPlayerEvents>(
                targetId: playerId,
                eventHandler: handler => handler.OnPlayerStatusUpdate(eventData));

            _logger.LogDebug("Published player status update event: {PlayerId}", playerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing player status update event: {PlayerId}", playerId);
        }
    }

    /// <summary>
    /// 发布玩家升级事件
    /// </summary>
    public async Task PublishPlayerLevelUpAsync(int playerId, PlayerLevelUpEvent eventData)
    {
        try
        {
            // 发送给特定玩家
            await _eventPublisher.PublishAsync<IPlayerEvents>(
                targetId: playerId,
                eventHandler: handler => handler.OnPlayerLevelUp(eventData));

            _logger.LogInformation("Published player level up event: {PlayerId} -> Level {NewLevel}",
                playerId, eventData.NewLevel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing player level up event: {PlayerId}", playerId);
        }
    }

    /// <summary>
    /// 发布玩家移动事件
    /// </summary>
    public async Task PublishPlayerMovedAsync(string worldId, PlayerMovedEvent eventData)
    {
        try
        {
            // 发送给世界中的所有玩家（排除自己）
            await _eventPublisher.PublishAsync<IPlayerEvents>(
                filter: client => client.WorldId == worldId && client.PlayerId != eventData.PlayerId,
                eventHandler: handler => handler.OnPlayerMoved(eventData));

            _logger.LogDebug("Published player moved event: {PlayerId} in {WorldId}",
                eventData.PlayerId, worldId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing player moved event: {PlayerId}", eventData.PlayerId);
        }
    }
}
