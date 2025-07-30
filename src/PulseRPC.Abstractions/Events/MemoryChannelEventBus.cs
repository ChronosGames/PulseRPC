using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC.Serialization;

namespace PulseRPC.Events;

/// <summary>
/// 基于内存通道的事件总线实现
/// </summary>
public class MemoryChannelEventBus : IEventBus
{
    private readonly Dictionary<string, List<EventSubscription>> _subscriptions = new();
    private readonly ISerializerProvider _serializerProvider;
    private readonly object _syncLock = new object();
    private readonly ILogger<MemoryChannelEventBus> _logger;

    /// <summary>
    /// 创建内存通道事件总线
    /// </summary>
    public MemoryChannelEventBus(ISerializerProvider serializerProvider, ILogger<MemoryChannelEventBus>? logger = null)
    {
        _serializerProvider = serializerProvider ?? throw new ArgumentNullException(nameof(serializerProvider));
        _logger = logger ?? NullLogger<MemoryChannelEventBus>.Instance;
    }

    /// <summary>
    /// 发布事件
    /// </summary>
    public Task PublishAsync<T>(string eventName, T eventData, CancellationToken cancellationToken = default)
    {
        List<EventSubscription>? subscriptions;

        lock (_syncLock)
        {
            // 查找事件名称的所有订阅
            if (!_subscriptions.TryGetValue(eventName, out subscriptions) || subscriptions.Count == 0)
            {
                return Task.CompletedTask; // 没有订阅者
            }

            // 复制列表，避免回调期间修改集合
            subscriptions = new List<EventSubscription>(subscriptions);
        }

        try
        {
            var writer = new ArrayBufferWriter<byte>();
            var data = new { Name = "John", Age = 30 };

            _serializerProvider.Create(MethodType.Unary, null).Serialize(writer, in data);

            // 序列化事件数据
            var eventBytes = writer.WrittenMemory.ToArray();

            // 通知所有订阅者
            foreach (var subscription in subscriptions)
            {
                try
                {
                    // 触发事件处理
                    subscription.Invoke(this, eventBytes, _serializerProvider);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "事件处理异常: {EventName}, {SubscriptionId}", eventName, subscription.Id);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "事件发布异常: {EventName}", eventName);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 订阅事件
    /// </summary>
    public ISubscriptionToken Subscribe<T>(string eventName, EventHandler<T> handler)
    {
        if (string.IsNullOrEmpty(eventName))
            throw new ArgumentNullException(nameof(eventName));

        if (handler == null)
            throw new ArgumentNullException(nameof(handler));

        lock (_syncLock)
        {
            // 创建订阅
            var subscription = new EventSubscription<T>(eventName, handler);

            // 添加到订阅集合
            if (!_subscriptions.TryGetValue(eventName, out var subscriptions))
            {
                subscriptions = new List<EventSubscription>();
                _subscriptions[eventName] = subscriptions;
            }

            subscriptions.Add(subscription);

            // 创建订阅令牌
            return new SubscriptionToken(
                subscription.Id,
                eventName,
                typeof(T),
                () => RemoveSubscription(subscription.Id, eventName));
        }
    }

    /// <summary>
    /// 取消订阅
    /// </summary>
    public void Unsubscribe(ISubscriptionToken token)
    {
        if (token == null)
            throw new ArgumentNullException(nameof(token));

        if (token is SubscriptionToken subscriptionToken)
        {
            RemoveSubscription(subscriptionToken.Id, subscriptionToken.EventName);
        }
    }

    /// <summary>
    /// 移除订阅
    /// </summary>
    private void RemoveSubscription(Guid subscriptionId, string eventName)
    {
        lock (_syncLock)
        {
            if (_subscriptions.TryGetValue(eventName, out var subscriptions))
            {
                var subscription = subscriptions.FirstOrDefault(s => s.Id == subscriptionId);
                if (subscription != null)
                {
                    subscriptions.Remove(subscription);

                    // 如果没有更多订阅，移除事件
                    if (subscriptions.Count == 0)
                    {
                        _subscriptions.Remove(eventName);
                    }
                }
            }
        }
    }
}
