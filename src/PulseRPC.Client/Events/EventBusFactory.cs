using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC.Events;
using PulseRPC.Messaging;
using PulseRPC.Serialization;

namespace PulseRPC.Client.Events
{
    /// <summary>
    /// 事件总线工厂
    /// </summary>
    public class EventBusFactory
    {
        private readonly ISerializer _serializer;
        private readonly ILoggerFactory? _loggerFactory;
        private readonly Dictionary<string, IEventBus> _eventBuses = new();

        /// <summary>
        /// 创建事件总线工厂
        /// </summary>
        public EventBusFactory(ISerializer serializer, ILoggerFactory? loggerFactory = null)
        {
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _loggerFactory = loggerFactory;
        }

        /// <summary>
        /// 为通道创建事件总线
        /// </summary>
        public IEventBus CreateForChannel(IMessageChannel channel)
        {
            if (channel == null)
                throw new ArgumentNullException(nameof(channel));

            // 检查是否已为此通道创建事件总线
            string channelKey = $"channel:{channel.GetType().Name}";

            if (_eventBuses.TryGetValue(channelKey, out var existingBus))
                return existingBus;

            // 创建网络通道事件总线
            var logger = _loggerFactory?.CreateLogger<NetworkChannelEventBus>() ??
                         NullLogger<NetworkChannelEventBus>.Instance;
            var eventBus = new NetworkChannelEventBus(channel, _serializer, logger);

            _eventBuses[channelKey] = eventBus;
            return eventBus;
        }

        /// <summary>
        /// 创建内存通道事件总线
        /// </summary>
        public IEventBus CreateInMemory(string name = "memory")
        {
            string key = $"memory:{name}";

            if (_eventBuses.TryGetValue(key, out var existingBus))
                return existingBus;

            // 创建内存通道事件总线
            var logger = _loggerFactory?.CreateLogger<MemoryChannelEventBus>() ??
                         NullLogger<MemoryChannelEventBus>.Instance;
            var eventBus = new MemoryChannelEventBus(_serializer, logger);

            _eventBuses[key] = eventBus;
            return eventBus;
        }
    }
}
