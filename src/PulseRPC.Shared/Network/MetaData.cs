using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace PulseRPC.Network;

/// <summary>
/// 通用的会话元数据存储器，用于在Session中存储Context信息
/// </summary>
/// <typeparam name="TKey">索引键类型</typeparam>
internal class MetaData<TKey> where TKey : notnull
{
    // 内部使用线程安全的ConcurrentDictionary存储元数据
    private readonly ConcurrentDictionary<TKey, object> _metaData = new();

    // 读写锁，用于某些需要原子性操作的场景
    private readonly ReaderWriterLockSlim _rwLock = new();

    /// <summary>
    /// 获取元数据
    /// </summary>
    /// <typeparam name="TValue">值类型</typeparam>
    /// <param name="key">键</param>
    /// <returns>对应类型的值，如不存在则返回默认值</returns>
    public TValue? Get<TValue>(TKey key)
    {
        if (!_metaData.TryGetValue(key, out var value))
        {
            return default;
        }

        if (value is TValue typedValue)
        {
            return typedValue;
        }

        return default;
    }

    /// <summary>
    /// 尝试获取元数据
    /// </summary>
    /// <typeparam name="TValue">值类型</typeparam>
    /// <param name="key">键</param>
    /// <param name="value">输出值</param>
    /// <returns>是否成功获取</returns>
    public bool TryGet<TValue>(TKey key, out TValue? value)
    {
        if (_metaData.TryGetValue(key, out var objValue) && objValue is TValue typedValue)
        {
            value = typedValue;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
        /// 设置元数据
        /// </summary>
        /// <typeparam name="TValue">值类型</typeparam>
        /// <param name="key">键</param>
        /// <param name="value">值</param>
        public void Set<TValue>(TKey key, TValue value)
        {
            _metaData[key] = value!;
        }

        /// <summary>
        /// 设置元数据，如果键不存在
        /// </summary>
        /// <typeparam name="TValue">值类型</typeparam>
        /// <param name="key">键</param>
        /// <param name="valueFactory">值工厂方法</param>
        /// <returns>设置的值，可能是新创建的或已存在的</returns>
        public TValue GetOrAdd<TValue>(TKey key, Func<TKey, TValue> valueFactory)
        {
            var result = _metaData.GetOrAdd(key, k => valueFactory(k)!);
            return (TValue)result;
        }

        /// <summary>
        /// 原子性地获取或添加值
        /// </summary>
        /// <typeparam name="TValue">值类型</typeparam>
        /// <param name="key">键</param>
        /// <param name="valueFactory">值工厂方法</param>
        /// <returns>设置的值，可能是新创建的或已存在的</returns>
        public TValue GetOrAddAtomic<TValue>(TKey key, Func<TKey, TValue> valueFactory)
        {
            // 首先尝试直接获取
            if (_metaData.TryGetValue(key, out var existingValue) && existingValue is TValue typedValue)
            {
                return typedValue;
            }

            // 如果不存在，使用锁确保原子性操作
            _rwLock.EnterWriteLock();
            try
            {
                return (TValue)_metaData.GetOrAdd(key, k => valueFactory(k)!);
            }
            finally
            {
                _rwLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// 更新元数据
        /// </summary>
        /// <typeparam name="TValue">值类型</typeparam>
        /// <param name="key">键</param>
        /// <param name="addFactory">添加工厂方法，接收当前值并返回新值</param>
        /// <param name="updateFactory">更新工厂方法，接收当前值并返回新值</param>
        /// <returns>更新后的值</returns>
        public TValue AddOrUpdate<TValue>(TKey key, Func<TKey, TValue> addFactory, Func<TKey, TValue, TValue> updateFactory)
        {
            var result = _metaData.AddOrUpdate(
                key,
                k => addFactory(k)!,
                (k, oldValue) => updateFactory(k, (TValue)oldValue)!
            );

            return (TValue)result;
        }

        /// <summary>
        /// 删除元数据
        /// </summary>
        /// <param name="key">键</param>
        /// <returns>是否成功删除</returns>
        public bool Remove(TKey key)
        {
            return _metaData.TryRemove(key, out _);
        }

        /// <summary>
        /// 清空所有元数据
        /// </summary>
        public void Clear()
        {
            _metaData.Clear();
        }

        /// <summary>
        /// 判断是否包含指定键
        /// </summary>
        /// <param name="key">键</param>
        /// <returns>是否包含</returns>
        public bool Contains(TKey key)
        {
            return _metaData.ContainsKey(key);
        }

        /// <summary>
        /// 获取所有键的集合
        /// </summary>
        /// <returns>键集合</returns>
        public ICollection<TKey> GetKeys()
        {
            return _metaData.Keys;
        }

        /// <summary>
        /// 获取元数据数量
        /// </summary>
        public int Count => _metaData.Count;
}
