namespace PulseRPC.Gateway;

/// <summary>
/// Gateway wire 协议号。客户端和服务端必须共享这些固定值，避免中转协议随方法签名重构而漂移。
/// </summary>
public static class GatewayProtocolIds
{
    /// <summary><c>IGatewayFrontHub.RelayAskAsync</c> 的固定协议号。</summary>
    public const ushort FrontRelayAsk = 0x30ED;

    /// <summary><c>IGatewayFrontHub.RelaySendAsync</c> 的固定协议号。</summary>
    public const ushort FrontRelaySend = 0xD098;

    /// <summary><c>IGatewayRelayHub.PushRawFrameAsync</c> 的固定协议号。</summary>
    public const ushort RelayPushFrame = 0x9E31;

    /// <summary><c>IGatewayRelayHub.AskConnectionAsync</c> 的固定协议号。</summary>
    public const ushort RelayAskConnection = 0x9E32;
}
