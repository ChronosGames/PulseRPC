using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC.Messaging;
using PulseRPC.Serialization;
using PulseRPC.Transport;

namespace PulseRPC.Client;

/// <summary>
/// 基于网络通道的事件总线实现
/// </summary>
public class NetworkChannelEventBus : IEventBus
{
    private readonly IClientChannel _channel;
    private readonly Dictionary<string, List<EventSubscription>> _subscriptions = new();
    private readonly ISerializerProvider _serializerProvider;
    private readonly object _syncLock = new object();
    private readonly ILogger<NetworkChannelEventBus> _logger;
    private bool _initialized;

    /// <summary>
    /// 创建网络通道事件总线
    /// </summary>
    public NetworkChannelEventBus(IClientChannel channel, ISerializerProvider serializerProvider,
        ILogger<NetworkChannelEventBus>? logger = null)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _serializerProvider = serializerProvider ?? throw new ArgumentNullException(nameof(serializerProvider));
        _logger = logger ?? NullLogger<NetworkChannelEventBus>.Instance;
    }

    /// <summary>
    /// 初始化事件总线
    /// </summary>
    private void EnsureInitialized()
    {
        if (_initialized)
            return;

        lock (_syncLock)
        {
            if (_initialized)
                return;

            // 订阅通道事件
            _channel.RegisterEventCallback(OnNetworkEventReceived);

            _initialized = true;
        }
    }

    /// <summary>
    /// 发布事件
    /// </summary>
    public async Task PublishAsync<T>(string eventName, T eventData, CancellationToken cancellationToken = default)
    {
        // 确保已初始化
        EnsureInitialized();

        try
        {
            // 通过通道发送事件
            await _channel.SendEventAsync(string.Empty, eventName, eventData, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "通过网络通道发布事件异常: {EventName}", eventName);
            throw;
        }
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

        // 确保已初始化
        EnsureInitialized();

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

    /// <summary>
    /// 处理网络事件接收
    /// </summary>
    private void OnNetworkEventReceived(string eventName, byte[] eventData)
    {
        List<EventSubscription>? subscriptions;

        lock (_syncLock)
        {
            // 查找事件名称的所有订阅
            if (!_subscriptions.TryGetValue(eventName, out subscriptions) || subscriptions.Count == 0)
            {
                return; // 没有订阅者
            }

            // 复制列表，避免回调期间修改集合
            subscriptions = new List<EventSubscription>(subscriptions);
        }

        // 通知所有订阅者
        foreach (var subscription in subscriptions)
        {
            try
            {
                // 触发事件处理
                subscription.Invoke(this, eventData, _serializerProvider);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "网络事件处理异常: {EventName}, {SubscriptionId}",
                    eventName, subscription.Id);
            }
        }
    }
}
