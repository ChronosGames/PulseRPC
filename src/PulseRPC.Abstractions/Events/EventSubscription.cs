using System;
using System.Buffers;
using System.Diagnostics;
using PulseRPC.Serialization;

namespace PulseRPC;

/// <summary>
/// 事件订阅基类
/// </summary>
public abstract class EventSubscription
{
    /// <summary>
    /// 获取订阅标识
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// 获取事件名称
    /// </summary>
    public string EventName { get; }

    /// <summary>
    /// 获取事件数据类型
    /// </summary>
    public Type EventType { get; }

    /// <summary>
    /// 创建事件订阅
    /// </summary>
    protected EventSubscription(string eventName, Type eventType)
    {
        Id = Guid.NewGuid();
        EventName = eventName ?? throw new ArgumentNullException(nameof(eventName));
        EventType = eventType ?? throw new ArgumentNullException(nameof(eventType));
    }

    /// <summary>
    /// 触发事件处理
    /// </summary>
    public abstract void Invoke(object sender, byte[] data, ISerializerProvider serializerProvider);

    /// <summary>
    /// 获取事件处理器的动态调用包装
    /// </summary>
    public virtual Action<object, object>? GetDynamicInvoker()
    {
        return null; // 基类不实现，由子类提供
    }
}

/// <summary>
/// 泛型事件订阅
/// </summary>
public class EventSubscription<T> : EventSubscription
{
    private readonly EventHandler<T> _handler;

    /// <summary>
    /// 创建泛型事件订阅
    /// </summary>
    public EventSubscription(string eventName, EventHandler<T> handler)
        : base(eventName, typeof(T))
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    /// <summary>
    /// 触发事件处理
    /// </summary>
    public override void Invoke(object sender, byte[] data, ISerializerProvider serializerProvider)
    {
        if (data == null || serializerProvider == null)
            return;

        try
        {
            // 反序列化事件数据
            var eventData = serializerProvider.Create(MethodType.Unary, null).Deserialize<T>(new ReadOnlySequence<byte>(data));

            // 调用处理器
            _handler(sender, eventData);
        }
        catch (Exception ex)
        {
            // 记录异常但不抛出，避免影响其他订阅者
            Debug.WriteLine($"Event handler exception: {ex}");
        }
    }

    /// <summary>
    /// 获取事件处理器的动态调用包装
    /// </summary>
    public override Action<object, object> GetDynamicInvoker()
    {
        return (sender, data) => _handler(sender, (T)data);
    }
}
