using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using PulseRPC.Serialization;

namespace PulseRPC.Client.Serialization;

/// <summary>
/// 高性能序列化器管理器接口 - 基于现有的 ISerializerProvider
/// </summary>
public interface ISerializerManager
{
    /// <summary>
    /// 获取序列化器 - 基于类型缓存，零分配
    /// </summary>
    ISerializer GetSerializer<TRequest, TResponse>();

    /// <summary>
    /// 获取序列化器 - 运行时类型
    /// </summary>
    ISerializer GetSerializer(Type requestType, Type responseType);

    /// <summary>
    /// 设置默认序列化器提供者
    /// </summary>
    void SetDefaultProvider(ISerializerProvider provider);

    /// <summary>
    /// 获取当前序列化器提供者
    /// </summary>
    ISerializerProvider GetDefaultProvider();
}

/// <summary>
/// 高性能序列化器管理器实现
/// 使用编译时类型缓存和运行时优化
/// </summary>
internal sealed class SerializerManager : ISerializerManager
{
    // 编译时类型缓存 - 零分配查找
    private static readonly ConcurrentDictionary<SerializerKey, ISerializer> _typeCache = new();

    private volatile ISerializerProvider _defaultProvider;

    public SerializerManager(ISerializerProvider? defaultProvider = null)
    {
        _defaultProvider = defaultProvider ?? PulseRPCSerializerProvider.Instance;
    }

    /// <summary>
    /// 获取序列化器 - 泛型版本，编译期优化
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ISerializer GetSerializer<TRequest, TResponse>()
    {
        // 使用静态泛型类确保每个类型组合只计算一次
        return SerializerCache<TRequest, TResponse>.GetOrCreate(_defaultProvider);
    }

    /// <summary>
    /// 获取序列化器 - 运行时类型版本
    /// </summary>
    public ISerializer GetSerializer(Type requestType, Type responseType)
    {
        var key = new SerializerKey(requestType, responseType);

        return _typeCache.GetOrAdd(key, static (k, provider) =>
        {
            // 创建序列化器
            return provider.Create(MethodType.Unary, null);
        }, _defaultProvider);
    }

    public void SetDefaultProvider(ISerializerProvider provider)
    {
        _defaultProvider = provider ?? throw new ArgumentNullException(nameof(provider));

        // 清空缓存以使用新的提供者
        _typeCache.Clear();
    }

    public ISerializerProvider GetDefaultProvider() => _defaultProvider;

    /// <summary>
    /// 静态泛型类 - 确保每个类型组合的序列化器只创建一次
    /// </summary>
    private static class SerializerCache<TRequest, TResponse>
    {
        private static volatile ISerializer? _instance;
        private static readonly object _lock = new();

        public static ISerializer GetOrCreate(ISerializerProvider provider)
        {
            if (_instance != null)
                return _instance;

            lock (_lock)
            {
                if (_instance != null)
                    return _instance;

                _instance = provider.Create(MethodType.Unary, null);
                return _instance;
            }
        }
    }
}

/// <summary>
/// 序列化器缓存键
/// </summary>
internal readonly struct SerializerKey : IEquatable<SerializerKey>
{
    private readonly Type _requestType;
    private readonly Type _responseType;
    private readonly int _hashCode;

    public SerializerKey(Type requestType, Type responseType)
    {
        _requestType = requestType ?? throw new ArgumentNullException(nameof(requestType));
        _responseType = responseType ?? throw new ArgumentNullException(nameof(responseType));
        _hashCode = HashCode.Combine(_requestType, _responseType);
    }

    public bool Equals(SerializerKey other)
    {
        return _requestType == other._requestType && _responseType == other._responseType;
    }

    public override bool Equals(object? obj)
    {
        return obj is SerializerKey other && Equals(other);
    }

    public override int GetHashCode() => _hashCode;
}
