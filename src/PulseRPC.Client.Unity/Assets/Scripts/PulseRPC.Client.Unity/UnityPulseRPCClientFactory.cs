using System;
using System.Threading.Tasks;
using UnityEngine;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC.Client;

namespace PulseRPC.Client.Unity
{
    /// <summary>
    /// Unity 特化的 PulseRPC 客户端工厂
    /// 提供 Unity 环境下的客户端创建和配置
    /// </summary>
    public static class UnityPulseRPCClientFactory
    {
        /// <summary>
        /// 创建针对 Unity 优化的 PulseRPC 客户端
        /// </summary>
        /// <param name="serverAddress">服务器地址</param>
        /// <param name="serverPort">服务器端口</param>
        /// <param name="loggerFactory">日志工厂（可选）</param>
        /// <returns>配置好的 PulseRPC 客户端</returns>
        public static async Task<IPulseRPCClient> CreateClientAsync(
            string serverAddress,
            int serverPort,
            ILoggerFactory? loggerFactory = null)
        {
            // 在 Unity 环境中，自动设置 UNITY 编译条件
            var adapter = PlatformAdapterFactory.CreateAdapter(loggerFactory ?? NullLoggerFactory.Instance);

            // 配置 Unity 特定的线程设置
            adapter.ConfigureThreading();

            var clientOptions = new PulseRPCClientOptions
            {
                ServerAddress = serverAddress,
                ServerPort = serverPort,
                PlatformAdapter = adapter,
                // Unity 特定的配置
                UseUnityOptimizations = true,
                EnableUnityMainThreadDispatch = true
            };

            return await PulseRPCClient.CreateAsync(clientOptions);
        }

        /// <summary>
        /// 创建带有 Unity MonoBehaviour 集成的客户端
        /// </summary>
        /// <param name="serverAddress">服务器地址</param>
        /// <param name="serverPort">服务器端口</param>
        /// <param name="gameObject">用于承载客户端的 GameObject</param>
        /// <param name="loggerFactory">日志工厂（可选）</param>
        /// <returns>带有 MonoBehaviour 集成的客户端组件</returns>
        public static async Task<UnityPulseRPCClientComponent> CreateClientComponentAsync(
            string serverAddress,
            int serverPort,
            GameObject gameObject,
            ILoggerFactory? loggerFactory = null)
        {
            var client = await CreateClientAsync(serverAddress, serverPort, loggerFactory);

            var component = gameObject.AddComponent<UnityPulseRPCClientComponent>();
            component.Initialize(client);

            return component;
        }

        /// <summary>
        /// 为 Unity 编辑器环境创建测试客户端
        /// </summary>
        /// <param name="serverAddress">服务器地址</param>
        /// <param name="serverPort">服务器端口</param>
        /// <returns>测试客户端</returns>
        public static async Task<IPulseRPCClient> CreateEditorTestClientAsync(
            string serverAddress = "localhost",
            int serverPort = 12345)
        {
#if UNITY_EDITOR
            Debug.Log($"[PulseRPC] Creating editor test client for {serverAddress}:{serverPort}");

            var loggerFactory = CreateUnityLoggerFactory();
            return await CreateClientAsync(serverAddress, serverPort, loggerFactory);
#else
            throw new InvalidOperationException("Editor test client can only be created in Unity Editor");
#endif
        }

        /// <summary>
        /// 创建 Unity 专用的日志工厂
        /// </summary>
        /// <returns>Unity 日志工厂</returns>
        private static ILoggerFactory CreateUnityLoggerFactory()
        {
            // 在实际实现中，这里可以创建一个将日志输出到 Unity Console 的日志提供程序
            // 目前返回 NullLoggerFactory 作为占位符
            return NullLoggerFactory.Instance;
        }
    }
}
