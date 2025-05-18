using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PulseRPC.Client;

namespace MiniGame.Client
{
    /// <summary>
    /// 网络管理器类，负责管理PulseRPC客户端连接
    /// </summary>
    public static class NetworkManager
    {
        private static ILogger? _logger;
        private static readonly NetworkClient _client;

        static NetworkManager()
        {
            // 创建默认的PulseService
            var pulseService = new PulseService();
            
            // 使用localhost作为默认连接
            _client = new NetworkClient(
                NullLogger.Instance,
                "localhost", 
                7000, 
                pulseService, 
                new NodeOptions());
        }

        /// <summary>
        /// 设置日志记录器
        /// </summary>
        /// <param name="logger">日志记录器</param>
        public static void SetLogger(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 注册服务节点
        /// </summary>
        /// <param name="name">节点名称</param>
        /// <param name="host">主机地址</param>
        /// <param name="port">端口号</param>
        /// <param name="options">节点选项</param>
        public static void RegisterNode(string name, string host, int port, NodeOptions options)
        {
            // 更新客户端配置
            _client.ConnectionTimeout = options.ConnectionTimeout;
            _client.IdleTimeout = options.IdleTimeout;
            _client.SendTimeout = options.SendTimeout;
        }

        /// <summary>
        /// 扫描程序集，查找服务客户端
        /// </summary>
        /// <param name="assemblies">程序集</param>
        public static void ScanAssemblies(params Assembly[] assemblies)
        {
            // 这里不需要实现，我们使用单一客户端
        }

        /// <summary>
        /// 创建服务客户端
        /// </summary>
        /// <typeparam name="T">服务类型</typeparam>
        /// <param name="nodeName">节点名称</param>
        /// <returns>服务客户端</returns>
        public static T CreateServiceClient<T>(string nodeName) where T : class
        {
            return PulseRPCFactory.CreateClient<T>(_client);
        }

        /// <summary>
        /// 连接所有节点
        /// </summary>
        public static async Task ConnectAllAsync()
        {
            await _client.ConnectAsync();
        }

        /// <summary>
        /// 断开所有连接
        /// </summary>
        public static async Task DisconnectAllAsync()
        {
            _client.Close();
            await Task.CompletedTask;
        }
    }
} 