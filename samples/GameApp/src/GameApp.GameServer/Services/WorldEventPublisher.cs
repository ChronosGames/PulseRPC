using Microsoft.Extensions.Logging;
using PulseRPC.Server.Events;
using GameApp.Shared.Services;

namespace GameApp.GameServer.Services;

/// <summary>
/// 世界事件发布器实现
/// </summary>
public class WorldEventPublisher : IWorldEventPublisher
{
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<WorldEventPublisher> _logger;

    public WorldEventPublisher(IEventPublisher eventPublisher, ILogger<WorldEventPublisher> logger)
    {
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    /// <summary>
    /// 发布世界更新事件
    /// </summary>
    public async Task PublishWorldUpdateAsync(string worldId, WorldUpdateEvent eventData)
    {
        try
        {
            // 发送给所有客户端
            await _eventPublisher.PublishEventAsync("WorldUpdate", eventData);

            _logger.LogDebug("Published world update event: {WorldId} -> {UpdateType}",
                worldId, eventData.UpdateType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing world update event: {WorldId}", worldId);
        }
    }

    /// <summary>
    /// 发布玩家加入世界事件
    /// </summary>
    public async Task PublishPlayerJoinedAsync(string worldId, PlayerJoinedEvent eventData)
    {
        try
        {
            // 发送给世界中的其他玩家（排除刚加入的玩家）
            await _eventPublisher.PublishEventAsync("PlayerJoinedWorld", eventData);

            _logger.LogInformation("Published player joined event: {PlayerName} joined {WorldId}",
                eventData.Player.CharacterName, worldId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing player joined event: {WorldId}", worldId);
        }
    }

    /// <summary>
    /// 发布玩家离开世界事件
    /// </summary>
    public async Task PublishPlayerLeftAsync(string worldId, PlayerLeftEvent eventData)
    {
        try
        {
            // 发送给世界中的其他玩家
            await _eventPublisher.PublishEventAsync("PlayerLeftWorld", eventData);

            _logger.LogInformation("Published player left event: {PlayerName} left {WorldId}",
                eventData.PlayerName, worldId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing player left event: {WorldId}", worldId);
        }
    }

    /// <summary>
    /// 发布世界聊天消息事件
    /// </summary>
    public async Task PublishWorldChatMessageAsync(string worldId, WorldChatMessageEvent eventData)
    {
        try
        {
            // 发送给世界中的所有玩家
            await _eventPublisher.PublishEventAsync("WorldChatMessage", eventData);

            _logger.LogDebug("Published world chat message: [{PlayerName}] {Message} in {WorldId}",
                eventData.PlayerName, eventData.Message, worldId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing world chat message: {WorldId}", worldId);
        }
    }

    /// <summary>
    /// 发布世界事件通知
    /// </summary>
    public async Task PublishWorldEventNotificationAsync(string worldId, WorldEventNotificationEvent eventData)
    {
        try
        {
            // 发送给世界中的所有玩家
            await _eventPublisher.PublishEventAsync("WorldEventNotification", eventData);

            _logger.LogInformation("Published world event notification: {EventName} in {WorldId}",
                eventData.Event.Name, worldId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing world event notification: {WorldId}", worldId);
        }
    }
}
