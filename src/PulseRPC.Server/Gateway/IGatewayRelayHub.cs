using System.Threading.Tasks;
using PulseRPC;
using PulseRPC.Gateway;
using PulseRPC.Protocol;

namespace PulseRPC.Server.Gateway;

/// <summary>
/// 节点→网关内部 RPC 契约 —— 由 Gateway 节点提供，供持有 Actor 的后端节点经 <see cref="PulseRPC.Clustering.INodeLink"/>
/// 调用，把一次推送/反向 Ask 转发给该网关上持有的真实客户端连接（虚拟连接的「最后一跳」，见 §6.2/§6.3）。
/// </summary>
/// <remarks>
/// 本接口方法以 <see cref="ProtocolAttribute"/> 显式固定协议号，供外部 <see cref="PulseRPC.Clustering.INodeLink"/>
/// 实现按同一协议稳定调用。不面向业务代码。
/// </remarks>
public interface IGatewayRelayHub : IPulseHub
{
    /// <summary>
    /// 单向：把一段已组帧的原始字节推送给 <paramref name="connectionId"/> 标识的真实客户端连接。
    /// </summary>
    [Protocol(GatewayProtocolIds.RelayPushFrame)]
    Task PushRawFrameAsync(string connectionId, byte[] framedPacket);

    /// <summary>
    /// 请求/响应：向 <paramref name="connectionId"/> 标识的真实客户端连接发起反向 Ask 并等待应答。
    /// </summary>
    [Protocol(GatewayProtocolIds.RelayAskConnection)]
    Task<byte[]> AskConnectionAsync(string connectionId, ushort protocolId, byte[] payload, int timeoutMs);
}
