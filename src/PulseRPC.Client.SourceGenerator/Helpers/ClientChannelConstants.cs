namespace PulseRPC.Generator.Helpers;

/// <summary>
/// 统一标记模型下用于标识"客户端实现、服务端推送"方向的 <c>[Channel("CLIENT")]</c> 通道名常量。
/// </summary>
/// <remarks>
/// 集中定义，避免客户端生成器内多处（<c>ServiceProxyGenerator</c>、
/// <c>ReceiverMigrationCodeFixProvider</c>）各自散落硬编码 <c>"CLIENT"</c> 字符串。
/// </remarks>
internal static class ClientChannelConstants
{
    /// <summary>
    /// 标记接口为"客户端实现的推送接收器"的通道名（大小写不敏感比较，见各处 <c>OrdinalIgnoreCase</c> 用法）。
    /// </summary>
    public const string ClientChannelName = "CLIENT";
}
