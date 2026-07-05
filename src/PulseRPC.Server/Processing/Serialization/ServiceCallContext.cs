using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PulseRPC.Messaging;
using PulseRPC.Serialization;
using PulseRPC.Server.Processing.Engine;
using MemoryPack;

namespace PulseRPC.Server.Processing.Serialization;

/// <summary>
/// 服务调用上下文
/// </summary>
public sealed class ServiceCallContext
{
    public string ConnectionId { get; }
    public Guid MessageId { get; }
    public string ServiceName { get; }
    public string MethodName { get; }
    public ushort ProtocolId { get; }
    public object? RequestData { get; }
    public MessageType MessageType { get; }
    public DateTime ReceivedTime { get; }
    public int ProcessorId { get; }
    public MessageFlags Flags { get; }
    public DateTime DeserializedTime { get; }

    /// <summary>
    /// 服务器传输连接（用于 RequestContext）
    /// </summary>
    public PulseRPC.Shared.IServerTransport? Transport { get; }

    public ServiceCallContext(
        string connectionId,
        Guid messageId,
        string serviceName,
        string methodName,
        ushort protocolId,
        object? requestData,
        MessageType messageType,
        DateTime receivedTime,
        int processorId,
        MessageFlags flags,
        PulseRPC.Shared.IServerTransport? transport = null)
    {
        ConnectionId = connectionId;
        MessageId = messageId;
        ServiceName = serviceName;
        MethodName = methodName;
        ProtocolId = protocolId;
        RequestData = requestData;
        MessageType = messageType;
        ReceivedTime = receivedTime;
        ProcessorId = processorId;
        Flags = flags;
        DeserializedTime = DateTime.UtcNow;
        Transport = transport;
    }
}
