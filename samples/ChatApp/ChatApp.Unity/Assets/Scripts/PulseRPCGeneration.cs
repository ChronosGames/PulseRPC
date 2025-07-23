using PulseRPC;
using ChatApp;

#nullable enable

namespace ChatApp.Unity
{
    /// <summary>
    /// PulseRPC 客户端代理生成配置
    /// 此类用于指示源代码生成器生成相应的客户端代理类
    /// </summary>
    [PulseClientGeneration(typeof(IPlayerHub))]
    [PulseClientGeneration(typeof(IPlayerLoginEvents))]
    [PulseClientGeneration(typeof(IPlayerMovementEvents))]
    public static class PulseRPCGeneration
    {
        // 此类用于源代码生成器，不包含实际逻辑
    }
}