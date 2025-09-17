using PulseRPC.Transport;

namespace PulseRPC.Channels;

/// <summary>
/// 通道接口 - ITransportChannel 的别名
/// 为了保持与现有代码的兼容性
/// </summary>
public interface IChannel : ITransportChannel
{
}