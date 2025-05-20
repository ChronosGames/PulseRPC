using System.Reflection;

namespace PulseRPC.Server;

/// <summary>
/// 事件发布器接口
/// </summary>
public interface IEventPublisher
{
    Task PublishEventAsync<T>(string eventName, T eventData) where T : IEventData;
    Task PublishEventAsync<T>(T eventData) where T : IEventData;
}

/// <summary>
/// 事件发布器实现
/// </summary>
public class EventPublisher(IServerChannelManager channelManager) : IEventPublisher
{
    private readonly Dictionary<Type, EventInfo> _eventCache = new();

    /// <summary>
    /// 发布事件
    /// </summary>
    public async Task PublishEventAsync<T>(string eventName, T eventData) where T : IEventData
    {
        var eventInfo = GetEventInfo(typeof(T));

        if (eventInfo.EventName != eventName)
        {
            throw new InvalidOperationException(
                $"cannot publish event {eventName} for type {typeof(T).Name}, expected {eventInfo.EventName}");
        }

        if (eventInfo != null)
        {
            await channelManager.BroadcastEventAsync(
                eventInfo.ChannelName,
                eventName,
                eventData);
        }
    }

    /// <summary>
    /// 发布事件
    /// </summary>
    public async Task PublishEventAsync<T>(T eventData) where T : IEventData
    {
        var eventInfo = GetEventInfo(typeof(T));

        if (eventInfo != null)
        {
            await channelManager.BroadcastEventAsync(
                eventInfo.ChannelName,
                eventInfo.EventName,
                eventData);
        }
    }

    /// <summary>
    /// 获取事件信息
    /// </summary>
    private EventInfo GetEventInfo(Type eventType)
    {
        // 检查缓存
        if (_eventCache.TryGetValue(eventType, out var eventInfo))
            return eventInfo;

        // 查找所有继承了IEventSubscriber的接口
        var subscriberInterfaces = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .Where(t => t.IsInterface && typeof(IEventSubscriber).IsAssignableFrom(t))
            .ToList();

        foreach (var interfaceType in subscriberInterfaces)
        {
            // 检查接口上的通道属性
            string channelName = interfaceType.GetCustomAttribute<ChannelAttribute>()?.ChannelName ?? "default";

            // 查找带有Event特性的方法
            foreach (var method in interfaceType.GetMethods())
            {
                if (method.GetCustomAttribute<EventAttribute>() == null)
                    continue;

                // 检查参数
                var parameters = method.GetParameters();
                if (parameters.Length != 1)
                    continue;

                // 检查参数类型
                var parameterType = parameters[0].ParameterType;
                if (parameterType == eventType)
                {
                    // 方法上的通道属性可以覆盖接口上的
                    string methodChannelName =
                        method.GetCustomAttribute<ChannelAttribute>()?.ChannelName ?? channelName;

                    // 创建事件信息
                    var info = new EventInfo { EventName = method.Name, ChannelName = methodChannelName };

                    // 缓存并返回
                    _eventCache[eventType] = info;
                    return info;
                }
            }
        }

        // 如果找不到，使用默认命名
        var defaultInfo = new EventInfo { EventName = eventType.Name, ChannelName = "default" };

        _eventCache[eventType] = defaultInfo;
        return defaultInfo;
    }

    // 内部类
    private class EventInfo
    {
        public required string EventName { get; set; }
        public required string ChannelName { get; set; }
    }
}
