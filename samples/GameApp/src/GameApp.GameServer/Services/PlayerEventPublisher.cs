using Microsoft.Extensions.Logging;
using PulseRPC.Server.Events;
using GameApp.Shared.Services;

namespace GameApp.GameServer.Services;

/// <summary>
/// 玩家事件发布器实现
/// </summary>
public class PlayerEventPublisher : IPlayerEventPublisher
{
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<PlayerEventPublisher> _logger;

    public PlayerEventPublisher(IEventPublisher eventPublisher, ILogger<PlayerEventPublisher> logger)
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
            // 发送给所有客户端
            await _eventPublisher.PublishEventAsync("PlayerStatusUpdate", eventData);

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
            // 发送给所有客户端
            await _eventPublisher.PublishEventAsync("PlayerLevelUp", eventData);

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
            // 发送给所有客户端
            await _eventPublisher.PublishEventAsync("PlayerMoved", eventData);

            _logger.LogDebug("Published player moved event: {PlayerId} in {WorldId}",
                eventData.PlayerId, worldId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing player moved event: {PlayerId}", eventData.PlayerId);
        }
    }
}
