using System;
using System.Diagnostics;
using System.Threading;

namespace PulseRPC.Server.Scheduling;

/// <summary>
/// 令牌桶算法实现 - 用于速率限制
/// </summary>
public sealed class TokenBucket
{
    private readonly object _lock = new object();
    private readonly long _capacity;
    private readonly long _tokensPerSecond;
    private readonly double _tokensPerTick;
    private long _tokens;
    private long _lastRefillTime;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="tokensPerSecond">每秒生成的令牌数</param>
    /// <param name="burstCapacity">突发容量（桶大小）</param>
    public TokenBucket(long tokensPerSecond, long burstCapacity)
    {
        if (tokensPerSecond <= 0)
            throw new ArgumentException("每秒令牌数必须大于0", nameof(tokensPerSecond));

        if (burstCapacity <= 0)
            throw new ArgumentException("突发容量必须大于0", nameof(burstCapacity));

        _tokensPerSecond = tokensPerSecond;
        _capacity = burstCapacity;
        _tokens = burstCapacity; // 初始时桶是满的
        _tokensPerTick = (double)tokensPerSecond / Stopwatch.Frequency;
        _lastRefillTime = Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// 尝试消费一个令牌
    /// </summary>
    /// <returns>是否成功消费</returns>
    public bool TryConsume()
    {
        return TryConsume(1);
    }

    /// <summary>
    /// 尝试消费指定数量的令牌
    /// </summary>
    /// <param name="tokens">要消费的令牌数</param>
    /// <returns>是否成功消费</returns>
    public bool TryConsume(long tokens)
    {
        if (tokens <= 0)
            throw new ArgumentException("令牌数必须大于0", nameof(tokens));

        lock (_lock)
        {
            RefillTokens();

            if (_tokens >= tokens)
            {
                _tokens -= tokens;
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// 获取当前可用令牌数
    /// </summary>
    public long AvailableTokens
    {
        get
        {
            lock (_lock)
            {
                RefillTokens();
                return _tokens;
            }
        }
    }

    /// <summary>
    /// 获取桶容量
    /// </summary>
    public long Capacity => _capacity;

    /// <summary>
    /// 获取每秒令牌生成速率
    /// </summary>
    public long TokensPerSecond => _tokensPerSecond;

    /// <summary>
    /// 重置令牌桶（填满）
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _tokens = _capacity;
            _lastRefillTime = Stopwatch.GetTimestamp();
        }
    }

    /// <summary>
    /// 填充令牌
    /// </summary>
    private void RefillTokens()
    {
        var now = Stopwatch.GetTimestamp();
        var elapsedTicks = now - _lastRefillTime;

        if (elapsedTicks > 0)
        {
            var tokensToAdd = (long)(elapsedTicks * _tokensPerTick);
            if (tokensToAdd > 0)
            {
                _tokens = Math.Min(_capacity, _tokens + tokensToAdd);
                _lastRefillTime = now;
            }
        }
    }

    /// <summary>
    /// 获取令牌桶状态信息
    /// </summary>
    public TokenBucketStatus GetStatus()
    {
        lock (_lock)
        {
            RefillTokens();
            return new TokenBucketStatus
            {
                AvailableTokens = _tokens,
                Capacity = _capacity,
                TokensPerSecond = _tokensPerSecond,
                UtilizationRatio = 1.0 - (double)_tokens / _capacity
            };
        }
    }
}

/// <summary>
/// 令牌桶状态信息
/// </summary>
public class TokenBucketStatus
{
    public long AvailableTokens { get; set; }
    public long Capacity { get; set; }
    public long TokensPerSecond { get; set; }
    public double UtilizationRatio { get; set; }
}
