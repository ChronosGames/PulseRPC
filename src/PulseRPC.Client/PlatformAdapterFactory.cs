using Microsoft.Extensions.Logging;

#if UNITY
using PulseRPC.Client.Platform.Unity;
#else
using PulseRPC.Client.Platform.Net;
#endif

namespace PulseRPC.Client
{
    /// <summary>
    /// 平台适配器工厂，根据运行环境自动选择合适的平台适配器
    /// </summary>
    public static class PlatformAdapterFactory
    {
        /// <summary>
        /// 创建适合当前平台的适配器
        /// </summary>
        /// <param name="loggerFactory">日志工厂</param>
        /// <returns>平台适配器实例</returns>
        public static IPlatformAdapter CreateAdapter(ILoggerFactory? loggerFactory = null)
        {
#if UNITY
            return new UnityPlatformAdapter(loggerFactory);
#else
            return new NetPlatformAdapter(loggerFactory);
#endif
        }

        /// <summary>
        /// 检查当前是否运行在 Unity 环境
        /// </summary>
        /// <returns>如果在 Unity 环境则返回 true</returns>
        public static bool IsUnityPlatform()
        {
#if UNITY
            return true;
#else
            return false;
#endif
        }
    }
}
