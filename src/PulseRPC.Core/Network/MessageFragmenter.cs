using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;

namespace PulseRPC.Protocol.Network;

/// <summary>
/// 消息分片处理器
/// </summary>
public class MessageFragmenter
{
    private readonly MessageBatcherOptions _options;
    private readonly ArrayPool<byte> _arrayPool;
    private readonly ConcurrentDictionary<long, FragmentCollection> _fragmentCollections;

    public MessageFragmenter(MessageBatcherOptions? options = null)
    {
        _options = options ?? new MessageBatcherOptions();
        _arrayPool = ArrayPool<byte>.Shared;
        _fragmentCollections = new ConcurrentDictionary<long, FragmentCollection>();
    }

    /// <summary>
    /// 将消息分片
    /// </summary>
    public IEnumerable<MessageBatch> Fragment(byte[] data, long messageId)
    {
        if (data.Length <= _options.MaxFragmentSize)
        {
            yield return new MessageBatch
            {
                Data = data,
                MessageId = messageId,
                FragmentIndex = 0,
                TotalFragments = 1
            };
            yield break;
        }

        var totalFragments = (data.Length + _options.MaxFragmentSize - 1) / _options.MaxFragmentSize;
        var buffer = _arrayPool.Rent(_options.MaxFragmentSize);

        try
        {
            for (var i = 0; i < totalFragments; i++)
            {
                var offset = i * _options.MaxFragmentSize;
                var size = Math.Min(_options.MaxFragmentSize, data.Length - offset);
                var fragment = new byte[size];
                Buffer.BlockCopy(data, offset, fragment, 0, size);

                yield return new MessageBatch
                {
                    Data = fragment,
                    MessageId = messageId,
                    FragmentIndex = i,
                    TotalFragments = totalFragments
                };
            }
        }
        finally
        {
            _arrayPool.Return(buffer);
        }
    }

    /// <summary>
    /// 处理接收到的消息分片
    /// </summary>
    /// <returns>如果所有分片都已收到，则返回完整消息；否则返回 null</returns>
    public byte[]? HandleFragment(MessageBatch fragment)
    {
        if (!fragment.IsFragment)
        {
            return fragment.Data;
        }

        var collection = _fragmentCollections.GetOrAdd(fragment.MessageId,
            _ => new FragmentCollection(fragment.TotalFragments));

        return collection.AddFragment(fragment);
    }

    /// <summary>
    /// 清理过期的分片集合
    /// </summary>
    public void CleanupExpiredCollections(TimeSpan expireAfter)
    {
        var now = DateTime.UtcNow;
        var expired = _fragmentCollections
            .Where(kvp => (now - kvp.Value.LastUpdateTime) > expireAfter)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var messageId in expired)
        {
            _fragmentCollections.TryRemove(messageId, out _);
        }
    }

    private class FragmentCollection
    {
        private readonly byte[][] _fragments;
        private int _receivedCount;
        private readonly object _lock = new();

        public DateTime LastUpdateTime { get; private set; }

        public FragmentCollection(int totalFragments)
        {
            _fragments = new byte[totalFragments][];
            LastUpdateTime = DateTime.UtcNow;
        }

        public byte[]? AddFragment(MessageBatch fragment)
        {
            lock (_lock)
            {
                if (_fragments[fragment.FragmentIndex] != null)
                {
                    return null; // 重复的分片
                }

                _fragments[fragment.FragmentIndex] = fragment.Data;
                _receivedCount++;
                LastUpdateTime = DateTime.UtcNow;

                if (_receivedCount == _fragments.Length)
                {
                    // 所有分片都已收到，合并消息
                    var totalSize = _fragments.Sum(f => f.Length);
                    var result = new byte[totalSize];
                    var offset = 0;

                    foreach (var fragmentData in _fragments)
                    {
                        Buffer.BlockCopy(fragmentData, 0, result, offset, fragmentData.Length);
                        offset += fragmentData.Length;
                    }

                    return result;
                }

                return null;
            }
        }
    }
}
