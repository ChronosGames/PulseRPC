using System;
using System.Threading;
using System.Collections.Concurrent;
using Microsoft.Extensions.ObjectPool;

namespace PulseRPC.Protocol.Serialization;

/// <summary>
/// 消息池，用于重用消息对象
/// </summary>
public static class MessagePool
{
    private static readonly ConcurrentDictionary<Type, ObjectPool<IMessage>> _pools = new();

    /// <summary>
    /// 获取消息对象
    /// </summary>
    public static T Get<T>() where T : class, IMessage, new()
    {
        var pool = _pools.GetOrAdd(typeof(T), type =>
        {
            // 创建一个适配器策略，将 T 类型的策略转换为 IMessage 类型的策略
            var policy = new MessagePoolPolicy<T>();
            return new DefaultObjectPool<IMessage>(policy);
        });

        return (T)pool.Get();
    }

    /// <summary>
    /// 返回消息对象到池中
    /// </summary>
    public static void Return<T>(T message) where T : class, IMessage
    {
        if (message == null) return;

        if (_pools.TryGetValue(typeof(T), out var pool))
        {
            pool.Return(message);
        }
    }

    /// <summary>
    /// 清理消息池
    /// </summary>
    public static void Clear()
    {
        _pools.Clear();
    }
}

/// <summary>
/// 消息池策略适配器
/// </summary>
internal class MessagePoolPolicy<T> : IPooledObjectPolicy<IMessage> where T : class, IMessage, new()
{
    /// <summary>
    /// 最大保留对象数量
    /// </summary>
    public int MaximumRetained { get; set; } = 1024;

    private int _count;

    public IMessage Create()
    {
        return new T();
    }

    public bool Return(IMessage obj)
    {
        if (obj is not T)
        {
            return false;
        }

        if (Interlocked.Increment(ref _count) <= MaximumRetained)
        {
            // 重置对象状态
            if (obj is IResettable resettable)
            {
                resettable.Reset();
            }
            return true;
        }

        Interlocked.Decrement(ref _count);
        return false;
    }
}

/// <summary>
/// 可重置接口
/// </summary>
public interface IResettable
{
    /// <summary>
    /// 重置对象状态
    /// </summary>
    void Reset();
}
