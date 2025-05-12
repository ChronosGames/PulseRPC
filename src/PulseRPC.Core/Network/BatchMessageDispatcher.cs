using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MemoryPack;
using PulseRPC.Protocol.Messages;

namespace PulseRPC.Protocol.Network;

/// <summary>
/// 消息批处理分发器 - 高效处理批量消息
/// </summary>
public class BatchMessageDispatcher
{
    private readonly Dictionary<Type, Func<IPacket, Task>> _handlers = new Dictionary<Type, Func<IPacket, Task>>();

    /// <summary>
    /// 注册消息处理器
    /// </summary>
    public void RegisterHandler<T>(Func<T, Task> handler) where T : IPacket
    {
        _handlers[typeof(T)] = packet => handler((T)packet);
    }

    /// <summary>
    /// 分发一个消息
    /// </summary>
    public async Task DispatchAsync(IPacket packet)
    {
        if (packet is CommandBatch batch)
        {
            // 并行处理批量命令
            var tasks = new List<Task>(batch.Commands.Length);
            foreach (var command in batch.Commands)
            {
                tasks.Add(DispatchSinglePacketAsync(command));
            }

            // 等待所有处理完成
            await Task.WhenAll(tasks);
        }
        else
        {
            // 处理单个消息
            await DispatchSinglePacketAsync(packet);
        }
    }

    /// <summary>
    /// 分发单个消息
    /// </summary>
    private Task DispatchSinglePacketAsync(IPacket packet)
    {
        var packetType = packet.GetType();

        // 如果没有找到处理器，返回完成的任务
        return _handlers.TryGetValue(packetType, out var handler) ? handler(packet) : Task.CompletedTask;
    }
}

/// <summary>
/// 命令批处理包
/// </summary>
[MemoryPackable]
public partial class CommandBatch : Command
{
    public Command[] Commands { get; set; } = Array.Empty<Command>();
}
