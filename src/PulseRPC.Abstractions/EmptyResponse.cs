using MemoryPack;

namespace PulseRPC
{
    /// <summary>
    /// 空响应类型，用于不需要返回值的RPC方法
    /// </summary>
    [MemoryPackable]
    public partial class EmptyResponse
    {
        /// <summary>
        /// 单例实例
        /// </summary>
        public static EmptyResponse Instance { get; } = new EmptyResponse();
    }
}
