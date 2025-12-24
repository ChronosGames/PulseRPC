using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC.Events;
using PulseRPC.Messaging;
using PulseRPC.Serialization;

namespace PulseRPC.Client.Events;

/// <summary>
/// 事件总线工厂
/// </summary>
public class EventBusFactory
{
    private readonly ISerializerProvider _serializerProvider;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly Dictionary<string, IEventBus> _eventBuses = new();

    /// <summary>
    /// 创建事件总线工厂
    /// </summary>
    public EventBusFactory(ISerializerProvider serializerProvider, ILoggerFactory? loggerFactory = null)
    {
        _serializerProvider = serializerProvider ?? throw new ArgumentNullException(nameof(serializerProvider));
        _loggerFactory = loggerFactory;
    }

    /// <summary>
    /// 创建内存通道事件总线
    /// </summary>
    public IEventBus CreateInMemory(string name = "memory")
    {
        var key = $"memory:{name}";

        if (_eventBuses.TryGetValue(key, out var existingBus))
            return existingBus;

        // 创建内存通道事件总线
        var logger = _loggerFactory?.CreateLogger<MemoryChannelEventBus>() ??
                     NullLogger<MemoryChannelEventBus>.Instance;
        var eventBus = new MemoryChannelEventBus(_serializerProvider, logger);

        _eventBuses[key] = eventBus;
        return eventBus;
    }
}
