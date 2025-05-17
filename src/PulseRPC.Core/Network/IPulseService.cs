using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using MemoryPack;
using PulseRPC.Network;

namespace PulseRPC;


/// <summary>
/// PulseRPC服务接口 - 统一的消息处理接口
/// </summary>
public interface IPulseService
{
    /// <summary>
    /// 序列化对象
    /// </summary>
    void Serialize<T>(IBufferWriter<byte> writer, in T value) where T : IMemoryPackable<T>;

    /// <summary>
    /// 处理消息
    /// </summary>
    Task ProcessMessageAsync(NetworkSession session, ushort sequenceId,
        ReadOnlySequence<byte> buffer, CancellationToken cancellationToken = default);

    /// <summary>
    /// 反序列化对象
    /// </summary>
    T Deserialize<T>(ReadOnlySpan<byte> bytes) where T : IMemoryPackable<T>;

    /// <summary>
    /// 注册消息处理器
    /// </summary>
    void RegisterHandler<TMessage, TResponse>(Func<NetworkSession, TMessage, CancellationToken, Task<TResponse>> handler)
        where TMessage : IMemoryPackable<TMessage>
        where TResponse : IMemoryPackable<TResponse>;

    /// <summary>
    /// 注册单向消息处理器
    /// </summary>
    void RegisterHandler<TMessage>(Func<NetworkSession, TMessage, CancellationToken, Task> handler)
        where TMessage : IMemoryPackable<TMessage>;
}
