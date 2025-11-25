using DistributedGameApp.Shared.Domain.Matchmaking;
using DistributedGameApp.Shared.Hubs;
using DistributedGameApp.Shared.Receivers;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PulseRPC;
using PulseRPC.Messaging;
using PulseRPC.Protocol;
using PulseRPC.Server;
using PulseRPC.Server.Abstractions;
using System.Buffers;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace DistributedGameApp.GameServer.Services;

/// <summary>
/// GameServer 内部 RPC Hub 实现 - 接收其他服务器的回调通知
/// </summary>
/// <remarks>
/// 职责：
/// - 接收 BackendServer 的匹配成功回调
/// - 维护玩家的连接状态
/// - 转发通知给客户端
/// </remarks>
public class GameServerInternalHub : BaseService, IGameServerInternalHub, IPulseService
{
    private readonly INamedPulseServer _externalServer;
    private readonly ConcurrentDictionary<string, string> _playerToConnectionMap = new();

    public string ServiceName => "GameServerInternalHub";
    public string ServiceId => "GameServerInternalHub:Global";

    public GameServerInternalHub(
        [FromKeyedServices("External")] INamedPulseServer externalServer,
        ILogger<GameServerInternalHub> logger,
        IAuthenticationService authenticationService,
        PermissionValidator permissionValidator)
        : base(logger, authenticationService, permissionValidator)
    {
        _externalServer = externalServer;
    }

    /// <summary>
    /// 计算 Protocol ID（与生成器逻辑一致）
    /// </summary>
    private static ushort ComputeProtocolId(string methodName)
    {
        var bytes = Encoding.UTF8.GetBytes(methodName);
        var hash = SHA256.HashData(bytes);
        return BitConverter.ToUInt16(hash, 0);
    }

    /// <summary>
    /// 注册玩家连接（由 GameHub 登录时调用）
    /// </summary>
    public void RegisterPlayerConnection(string playerId, string connectionId)
    {
        _playerToConnectionMap[playerId] = connectionId;
        Logger.LogDebug("Player connection registered: {PlayerId} → {ConnectionId}", playerId, connectionId);
    }

    /// <summary>
    /// 注销玩家连接（由 GameHub 断开时调用）
    /// </summary>
    public void UnregisterPlayerConnection(string playerId)
    {
        _playerToConnectionMap.TryRemove(playerId, out _);
        Logger.LogDebug("Player connection unregistered: {PlayerId}", playerId);
    }

    /// <summary>
    /// 匹配成功回调 - BackendServer 调用
    /// </summary>
    public async Task<bool> OnMatchFoundAsync(string playerId, MatchFoundNotification notification)
    {
        try
        {
            // 查找玩家连接
            if (!_playerToConnectionMap.TryGetValue(playerId, out var connectionId))
            {
                Logger.LogWarning("Player not connected: {PlayerId}", playerId);
                return false;
            }

            // 计算 Protocol ID for IGameReceiver.OnMatchFoundAsync
            var protocolId = ComputeProtocolId("OnMatchFoundAsync");

            // 序列化通知内容
            var payloadBytes = MemoryPackSerializer.Serialize(notification);

            // 构造消息头
            var header = new MessageHeader(MessageType.Event, "IGameReceiver", "OnMatchFoundAsync")
            {
                MessageId = Guid.NewGuid(),
                ProtocolId = protocolId,
                Flags = MessageFlags.None,
                Timestamp = DateTimeOffset.UtcNow.Ticks
            };

            // 创建消息包
            var packet = new MessagePacket(header, payloadBytes);

            // 序列化消息包
            var estimatedSize = packet.EstimateSize();
            var buffer = ArrayPool<byte>.Shared.Rent(estimatedSize);

            try
            {
                var bytesWritten = packet.WriteTo(buffer);
                var packetData = buffer.AsMemory(0, bytesWritten);

                // 发送到客户端
                var success = await _externalServer.SendAsync(connectionId, packetData);

                if (success)
                {
                    Logger.LogInformation(
                        "Match found notification sent - PlayerId: {PlayerId}, MatchId: {MatchId}, BattleRoom: {RoomId}",
                        playerId, notification.MatchId, notification.BattleRoomId);
                }
                else
                {
                    Logger.LogWarning("Failed to send notification - PlayerId: {PlayerId}, ConnectionId: {ConnectionId}",
                        playerId, connectionId);
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
            Logger.LogError(ex, "Failed to send match found notification - PlayerId: {PlayerId}", playerId);
            return false;
        }
    }

    /// <summary>
    /// 匹配取消回调
    /// </summary>
    public async Task<bool> OnMatchCancelledAsync(string playerId, string reason)
    {
        try
        {
            if (!_playerToConnectionMap.TryGetValue(playerId, out var connectionId))
            {
                Logger.LogWarning("Player not connected: {PlayerId}", playerId);
                return false;
            }

            var protocolId = ComputeProtocolId("OnMatchCancelledAsync");
            var payloadBytes = MemoryPackSerializer.Serialize(reason);

            var header = new MessageHeader(MessageType.Event, "IGameReceiver", "OnMatchCancelledAsync")
            {
                MessageId = Guid.NewGuid(),
                ProtocolId = protocolId,
                Flags = MessageFlags.None,
                Timestamp = DateTimeOffset.UtcNow.Ticks
            };

            var packet = new MessagePacket(header, payloadBytes);
            var estimatedSize = packet.EstimateSize();
            var buffer = ArrayPool<byte>.Shared.Rent(estimatedSize);

            try
            {
                var bytesWritten = packet.WriteTo(buffer);
                var packetData = buffer.AsMemory(0, bytesWritten);
                var success = await _externalServer.SendAsync(connectionId, packetData);

                if (success)
                {
                    Logger.LogInformation(
                        "Match cancelled notification sent - PlayerId: {PlayerId}, Reason: {Reason}",
                        playerId, reason);
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
            Logger.LogError(ex, "Failed to send match cancelled notification - PlayerId: {PlayerId}", playerId);
            return false;
        }
    }
}
