using DistributedGameApp.Shared.Receivers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PulseRPC;
using PulseRPC.Server;
using PulseRPC.Server.Abstractions;
using PulseRPC.Protocol;
using PulseRPC.Messaging;
using MemoryPack;
using System.Buffers;

namespace DistributedGameApp.BackendServer.Services;

/// <summary>
/// 客户端通知服务 - 处理服务器向客户端的主动推送
/// </summary>
/// <remarks>
/// 功能：
/// - 通过 ConnectionId 向指定客户端发送通知
/// - 支持批量推送到多个客户端
/// - 使用 MemoryPack 序列化确保一致性
/// - 自动处理离线客户端
/// - 使用 PulseRPC 的 SendAsync 机制进行真实推送
///
/// 实现细节：
/// 1. 使用 INamedPulseServer.SendAsync 发送数据到客户端
/// 2. 正确构造消息协议格式（包含 ProtocolId）
/// 3. 对应客户端的 IBackendReceiver 接口
/// </remarks>
public class ClientNotificationService
{
    private readonly PlayerConnectionRegistry _connectionRegistry;
    private readonly INamedPulseServer _pulseServer;
    private readonly ILogger<ClientNotificationService> _logger;

    public ClientNotificationService(
        PlayerConnectionRegistry connectionRegistry,
        [FromKeyedServices("Internal")] INamedPulseServer pulseServer,
        ILogger<ClientNotificationService> logger)
    {
        _connectionRegistry = connectionRegistry;
        _pulseServer = pulseServer;
        _logger = logger;
    }

    /// <summary>
    /// 向指定玩家发送通知
    /// </summary>
    /// <typeparam name="TNotification">通知类型</typeparam>
    /// <param name="playerId">玩家ID</param>
    /// <param name="methodName">方法名</param>
    /// <param name="protocolId">协议ID</param>
    /// <param name="notification">通知内容</param>
    /// <returns>是否发送成功</returns>
    public async Task<bool> NotifyPlayerAsync<TNotification>(
        string playerId,
        string methodName,
        ushort protocolId,
        TNotification notification)
    {
        // 获取玩家的连接ID
        var connectionId = _connectionRegistry.GetConnectionId(playerId);
        if (connectionId == null)
        {
            _logger.LogWarning("玩家不在线，无法发送通知 - PlayerId: {PlayerId}, Method: {Method}",
                playerId, methodName);
            return false;
        }

        try
        {
            // 1. 序列化通知内容
            var payloadBytes = MemoryPackSerializer.Serialize(notification);

            // 2. 构造消息头（使用 ProtocolId）
            var header = new MessageHeader(MessageType.Event, "IBackendReceiver", methodName)
            {
                MessageId = Guid.NewGuid(),
                ProtocolId = protocolId,
                Flags = MessageFlags.None, // 事件通知无需响应
                Timestamp = DateTimeOffset.UtcNow.Ticks
            };

            // 3. 创建消息包
            var packet = new MessagePacket(header, payloadBytes);

            // 4. 序列化消息包到缓冲区
            var estimatedSize = packet.EstimateSize();
            var buffer = ArrayPool<byte>.Shared.Rent(estimatedSize);

            try
            {
                var bytesWritten = packet.WriteTo(buffer);
                var packetData = buffer.AsMemory(0, bytesWritten);

                // 5. 发送到客户端
                var success = await _pulseServer.SendAsync(connectionId, packetData);

                if (success)
                {
                    _logger.LogDebug("通知发送成功 - PlayerId: {PlayerId}, ConnectionId: {ConnectionId}, Method: {Method}, ProtocolId: 0x{ProtocolId:X4}",
                        playerId, connectionId, methodName, protocolId);
                }
                else
                {
                    _logger.LogWarning("通知发送失败 - PlayerId: {PlayerId}, ConnectionId: {ConnectionId}, Method: {Method}",
                        playerId, connectionId, methodName);
                }

                return success;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送通知异常 - PlayerId: {PlayerId}, ConnectionId: {ConnectionId}, Method: {Method}",
                playerId, connectionId, methodName);
            return false;
        }
    }

    /// <summary>
    /// 批量向多个玩家发送通知
    /// </summary>
    public async Task<int> NotifyPlayersAsync<TNotification>(
        IEnumerable<string> playerIds,
        string methodName,
        ushort protocolId,
        TNotification notification)
    {
        var tasks = playerIds.Select(playerId =>
            NotifyPlayerAsync(playerId, methodName, protocolId, notification));

        var results = await Task.WhenAll(tasks);
        var successCount = results.Count(r => r);

        _logger.LogInformation(
            "批量通知完成 - Method: {Method}, ProtocolId: 0x{ProtocolId:X4}, Total: {Total}, Success: {Success}",
            methodName, protocolId, results.Length, successCount);

        return successCount;
    }
}

/// <summary>
/// ClientNotificationService 扩展方法 - 提供类型安全的通知方法
/// </summary>
public static class ClientNotificationExtensions
{
    // 使用方法签名的哈希生成 ProtocolId
    // 注意：这些值应该与客户端生成的 ProtocolId 一致
    // 在实际生产环境中，应该使用 SourceGenerator 自动生成
    private static readonly ushort OnMatchFoundProtocolId = GenerateProtocolId(
        $"{typeof(IBackendReceiver).FullName}.{nameof(IBackendReceiver.OnMatchFoundAsync)}");

    private static readonly ushort OnMatchCanceledProtocolId = GenerateProtocolId(
        $"{typeof(IBackendReceiver).FullName}.{nameof(IBackendReceiver.OnMatchCanceledAsync)}");

    /// <summary>
    /// 简单的协议ID生成（使用 xxHash32 算法的简化版本）
    /// </summary>
    private static ushort GenerateProtocolId(string methodSignature)
    {
        // 使用简单的哈希算法生成 ProtocolId
        // 注意：这应该与 PulseRPC SourceGenerator 使用相同的算法
        var hash = 0u;
        foreach (var c in methodSignature)
        {
            hash = hash * 31 + c;
        }
        return (ushort)(hash % 65536);
    }

    /// <summary>
    /// 发送匹配成功通知
    /// </summary>
    public static Task<bool> NotifyMatchFoundAsync(
        this ClientNotificationService service,
        string playerId,
        DistributedGameApp.Shared.Domain.Matchmaking.MatchFoundNotification notification)
    {
        return service.NotifyPlayerAsync(
            playerId,
            nameof(IBackendReceiver.OnMatchFoundAsync),
            OnMatchFoundProtocolId,
            notification);
    }

    /// <summary>
    /// 批量发送匹配成功通知
    /// </summary>
    public static Task<int> NotifyMatchFoundAsync(
        this ClientNotificationService service,
        IEnumerable<string> playerIds,
        DistributedGameApp.Shared.Domain.Matchmaking.MatchFoundNotification notification)
    {
        return service.NotifyPlayersAsync(
            playerIds,
            nameof(IBackendReceiver.OnMatchFoundAsync),
            OnMatchFoundProtocolId,
            notification);
    }

    /// <summary>
    /// 发送匹配取消通知
    /// </summary>
    public static Task<bool> NotifyMatchCanceledAsync(
        this ClientNotificationService service,
        string playerId,
        string reason)
    {
        return service.NotifyPlayerAsync(
            playerId,
            nameof(IBackendReceiver.OnMatchCanceledAsync),
            OnMatchCanceledProtocolId,
            reason);
    }
}
