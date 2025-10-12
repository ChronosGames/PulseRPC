using System;
using MemoryPack;

namespace PulseRPC.Server.Engine;

/// <summary>
/// RPC 方法处理器的元数据
/// 在运行时定义，由 Source Generator 填充
/// </summary>
public sealed class HandlerMetadata
{
    /// <summary>
    /// 服务名称（Hub 接口名）
    /// </summary>
    public string ServiceName { get; }

    /// <summary>
    /// 方法名称
    /// </summary>
    public string MethodName { get; }

    /// <summary>
    /// 请求类型
    /// </summary>
    public Type? RequestType { get; }

    /// <summary>
    /// 响应类型
    /// </summary>
    public Type? ResponseType { get; }

    /// <summary>
    /// 请求是否为 MemoryPackable（可通过 MemoryPack 零拷贝反序列化）
    /// </summary>
    public bool RequestIsMemoryPackable { get; }

    /// <summary>
    /// 响应是否为 MemoryPackable
    /// </summary>
    public bool ResponseIsMemoryPackable { get; }

    /// <summary>
    /// 是否为单向方法（无响应）
    /// </summary>
    public bool IsOneWay { get; }

    /// <summary>
    /// 方法签名（用于调试和日志）
    /// </summary>
    public string Signature { get; }

    /// <summary>
    /// 反序列化委托（可选，由生成器提供）
    /// </summary>
    public Func<ReadOnlyMemory<byte>, object?>? DeserializeRequest { get; }

    /// <summary>
    /// 序列化委托（可选，由生成器提供）
    /// </summary>
    public Func<object?, byte[]>? SerializeResponse { get; }

    public HandlerMetadata(
        string serviceName,
        string methodName,
        Type? requestType,
        Type? responseType,
        bool requestIsMemoryPackable,
        bool responseIsMemoryPackable,
        bool isOneWay,
        string signature,
        Func<ReadOnlyMemory<byte>, object?>? deserializeRequest = null,
        Func<object?, byte[]>? serializeResponse = null)
    {
        ServiceName = serviceName ?? throw new ArgumentNullException(nameof(serviceName));
        MethodName = methodName ?? throw new ArgumentNullException(nameof(methodName));
        RequestType = requestType;
        ResponseType = responseType;
        RequestIsMemoryPackable = requestIsMemoryPackable;
        ResponseIsMemoryPackable = responseIsMemoryPackable;
        IsOneWay = isOneWay;
        Signature = signature ?? throw new ArgumentNullException(nameof(signature));
        DeserializeRequest = deserializeRequest;
        SerializeResponse = serializeResponse;
    }

    /// <summary>
    /// 使用 MemoryPack 反序列化请求
    /// </summary>
    public object? DeserializeRequestUsingMemoryPack(ReadOnlyMemory<byte> payload)
    {
        if (RequestType == null || payload.IsEmpty)
            return null;

        if (DeserializeRequest != null)
        {
            return DeserializeRequest(payload);
        }

        // 使用 MemoryPack 2.x API
        return MemoryPackSerializer.Deserialize(RequestType, payload.Span);
    }

    /// <summary>
    /// 使用 MemoryPack 序列化响应
    /// </summary>
    public byte[] SerializeResponseUsingMemoryPack(object? response)
    {
        if (response == null)
            return Array.Empty<byte>();

        if (SerializeResponse != null)
        {
            return SerializeResponse(response);
        }

        // 回退到反射调用
        return MemoryPackSerializer.Serialize(ResponseType!, response);
    }

    public override string ToString() => Signature;
}

