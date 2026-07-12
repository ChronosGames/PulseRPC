using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC.Serialization;

/// <summary>
/// 运行时响应序列化接口，由源代码生成器生成实现。
/// 负责将服务方法的返回值写入零拷贝缓冲区。
/// </summary>
public interface IResponseSerializer
{
    /// <summary>协议号。</summary>
    ushort ProtocolId { get; }

    /// <summary>
    /// 序列化响应对象。
    /// </summary>
    /// <param name="response">服务方法返回值；实现 <see cref="INullResponseSerializer"/> 的序列化器可接收 <see langword="null"/>。</param>
    /// <param name="writer">目标缓冲区。</param>
    void Serialize(object response, IBufferWriter<byte> writer);

    /// <summary>
    /// 异步序列化响应对象到管道或池化缓冲区，支持零拷贝写入。
    /// </summary>
    ValueTask SerializeAsync(object response, IBufferWriter<byte> writer, CancellationToken cancellationToken = default);

    /// <summary>
    /// 尝试获取强类型序列化委托，便于运行时直接使用泛型无装箱序列化。
    /// </summary>
    bool TryGetTypedSerializer<T>(out Action<T, IBufferWriter<byte>> serializer);
}

/// <summary>
/// 标记能够把 <see langword="null"/> 编码为声明响应类型 wire 表示的响应序列化器。
/// </summary>
/// <remarks>
/// 响应管道只会把成功的空返回值传给实现此接口的序列化器；未实现该接口的既有自定义
/// 序列化器继续沿用空 payload 行为，避免 void/null 升级后突然收到空对象。
/// </remarks>
public interface INullResponseSerializer : IResponseSerializer
{
}

/// <summary>
/// 由生成器产出的响应序列化注册表访问接口。
/// </summary>
public interface IResponseSerializerRegistry
{
    #region 基于协议号的查找 (推荐 - 高性能)

    /// <summary>
    /// [推荐] 根据协议号获取对应的响应序列化器（零字符串分配）。
    /// </summary>
    bool TryGetSerializer(ushort protocolId, [NotNullWhen(true)] out IResponseSerializer? serializer);

    #endregion

    /// <summary>
    /// 获取所有序列化器条目，用于测试或诊断。
    /// </summary>
    ReadOnlySpan<IResponseSerializer> EnumerateSerializers();
}
