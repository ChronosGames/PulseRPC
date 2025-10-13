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
    /// <summary>所属服务名称。</summary>
    string ServiceName { get; }

    /// <summary>所属方法名称。</summary>
    string MethodName { get; }

    /// <summary>
    /// 序列化响应对象。
    /// </summary>
    /// <param name="response">服务方法返回值（非空）。</param>
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
/// 由生成器产出的响应序列化注册表访问接口。
/// </summary>
public interface IResponseSerializerRegistry
{
    /// <summary>
    /// 根据服务与方法名称获取对应的响应序列化器。
    /// </summary>
    bool TryGetSerializer(string serviceName, string methodName, [NotNullWhen(true)] out IResponseSerializer? serializer);

    /// <summary>
    /// 获取所有序列化器条目，用于测试或诊断。
    /// </summary>
    ReadOnlySpan<IResponseSerializer> EnumerateSerializers();
}

/// <summary>
/// 默认的空注册表实现，便于在未生成代码时提供合理降级。
/// </summary>
public sealed class EmptyResponseSerializerRegistry : IResponseSerializerRegistry
{
    public static EmptyResponseSerializerRegistry Instance { get; } = new();

    private EmptyResponseSerializerRegistry()
    {
    }

    public bool TryGetSerializer(string serviceName, string methodName, out IResponseSerializer serializer)
    {
        serializer = default!;
        return false;
    }

    public ReadOnlySpan<IResponseSerializer> EnumerateSerializers()
    {
        return ReadOnlySpan<IResponseSerializer>.Empty;
    }
}

