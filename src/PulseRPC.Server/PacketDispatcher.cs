using Microsoft.Extensions.Logging;
using PulseRPC.Protocol.Messages;
using PulseRPC.Protocol.Network;

namespace PulseRPC.Server;

// 线程处理策略枚举
public enum HandlerThreadingPolicy
{
    // 在工作线程池中处理
    WorkerThread,

    // 在专用高优先级线程处理
    HighPriorityThread,

    // 在专用低延迟线程处理（适用于战斗逻辑）
    LowLatencyThread,

    // 在主线程处理（适用于维护全局状态的操作）
    MainThread
}

/// <summary>
/// 消息分发器，负责将接收到的消息分发到相应的处理程序
/// </summary>
public class PacketDispatcher(
    IServiceProvider serviceProvider,
    HandlerRegistry registry,
    HandlerThreadPoolManager threadPoolManager,
    ILogger<PacketDispatcher> logger)
    : IMessageDispatcher
{
    public async Task DispatchAsync(NetworkSession session, ushort sequenceId, IPacket packet, CancellationToken cancellationToken = default)
    {
        switch (packet)
        {
            case IRequest request:
            {
                // 尝试获取处理器信息
                if (!registry.TryGetRequestHandler(packet.GetType(), out var handlerInfo))
                {
                    logger.LogWarning("未找到消息ID {Name} 的请求处理器", packet.GetType().Name);
                    return;
                }

                // 获取处理器实例
                var handler = serviceProvider.GetService(handlerInfo!.HandlerType);
                if (handler == null)
                {
                    logger.LogError("无法创建处理器实例 {HandlerType}", handlerInfo.HandlerType.Name);
                    return;
                }

                // 根据线程策略处理请求并获取响应
                var response = await threadPoolManager.SubmitRequestTaskAsync(
                    handlerInfo.ThreadingPolicy,
                    handlerInfo.Priority,
                    handler,
                    request,
                    handlerInfo.ResponseType,
                    session,
                    cancellationToken);

                // 如果有响应，将其发送回客户端
                if (response != null)
                {
                    // 构造响应消息ID (通常是请求ID + 1 或其他约定)
                    // ushort responseMessageId = (ushort)(packet.Id + 1);

                    // 发送响应
                    dynamic abc = response;
                    await session.SendPacketAsync(abc, sequenceId);
                }

                break;
            }
            case ICommand command:
            {
                // 尝试获取处理器信息
                if (!registry.TryGetCommandHandler(packet.GetType(), out var handlerInfo))
                {
                    logger.LogWarning("未找到消息ID {Name} 的请求处理器", packet.GetType().Name);
                    return;
                }

                // 获取处理器实例
                var handler = serviceProvider.GetService(handlerInfo!.HandlerType);
                if (handler == null)
                {
                    logger.LogError("无法创建处理器实例 {HandlerType}", handlerInfo.HandlerType.Name);
                    return;
                }

                // 根据线程策略分发到正确的线程处理
                await threadPoolManager.SubmitCommandTaskAsync(
                    handlerInfo.ThreadingPolicy,
                    handlerInfo.Priority,
                    handler,
                    command,
                    session,
                    cancellationToken);
                break;
            }
        }


    }
}
