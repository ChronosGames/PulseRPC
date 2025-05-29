using PulseRPC.Server.Transport;
using Microsoft.Extensions.Logging;
using PulseRPC.Serialization;

namespace PulseRPC.Server.Events;

/// <summary>
/// 事件发布器接口
/// </summary>
public interface IEventPublisher
{
    Task PublishEventAsync<T>(string eventName, T eventData) where T : IEventData;
}

/// <summary>
/// 事件发布器实现 - 负责向所有连接的客户端发布事件
/// </summary>
public class EventPublisher : IEventPublisher
{
    private readonly IServerChannelManager _channelManager;
    private readonly ISerializerProvider _serializerProvider;
    private readonly ILogger<EventPublisher> _logger;

    public EventPublisher(
        IServerChannelManager channelManager,
        ISerializerProvider serializerProvider,
        ILogger<EventPublisher> logger)
    {
        _channelManager = channelManager ?? throw new ArgumentNullException(nameof(channelManager));
        _serializerProvider = serializerProvider ?? throw new ArgumentNullException(nameof(serializerProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 发布事件到所有连接的客户端
    /// </summary>
    public async Task PublishEventAsync<T>(string eventName, T eventData) where T : IEventData
    {
        if (string.IsNullOrEmpty(eventName))
            throw new ArgumentNullException(nameof(eventName));

        if (eventData == null)
            throw new ArgumentNullException(nameof(eventData));

        try
        {
            _logger.LogDebug("开始发布事件: {EventName}, 数据类型: {DataType}", eventName, typeof(T).Name);

            // 获取所有活跃的传输通道（只向已认证的客户端发送）
            var channels = _channelManager.GetAuthenticatedChannels();

            if (channels == null || !channels.Any())
            {
                _logger.LogDebug("没有已认证的活跃连接，跳过事件发布: {EventName}", eventName);
                return;
            }

            // 序列化事件数据
            var serializer = _serializerProvider.Create(MethodType.Unary, null);

            var writer = new System.Buffers.ArrayBufferWriter<byte>();
            serializer.Serialize(writer, in eventData);
            var eventDataBytes = writer.WrittenMemory.ToArray();

            _logger.LogDebug("事件数据已序列化: {EventName}, 大小: {Size} bytes", eventName, eventDataBytes.Length);

            // 并行发送给所有已认证的连接
            var tasks = channels.Select(async channel =>
            {
                try
                {
                    // 发送事件数据到该通道
                    await channel.SendAsync(eventDataBytes, CancellationToken.None);
                    _logger.LogTrace("事件已发送到连接: {ConnectionId}, 事件: {EventName}", channel.ConnectionId, eventName);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "向连接 {ConnectionId} 发送事件 {EventName} 时失败", channel.ConnectionId, eventName);
                    return false;
                }
            });

            var results = await Task.WhenAll(tasks);
            var successCount = results.Count(r => r);

            _logger.LogDebug("事件发布完成: {EventName}, 成功发送到 {SuccessCount}/{TotalCount} 个连接",
                eventName, successCount, channels.Count());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发布事件 {EventName} 时发生异常", eventName);
            throw;
        }
    }
}
