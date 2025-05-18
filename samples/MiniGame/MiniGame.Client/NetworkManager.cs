using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC;
using PulseRPC.Client;
using MiniGame.Shared;

namespace MiniGame.Client
{
    /// <summary>
    /// 游戏网络服务，封装对NetworkManager的调用
    /// </summary>
    public class GameNetworkService
    {
        private const string DefaultNodeName = "MiniGameServer";
        private static GameNetworkService? _instance;
        private NetworkClient? _client;

        public static GameNetworkService Instance => _instance ??= new GameNetworkService();

        private GameNetworkService() { }

        /// <summary>
        /// 初始化网络连接
        /// </summary>
        /// <param name="host">服务器地址</param>
        /// <param name="port">服务器端口</param>
        /// <returns>连接任务</returns>
        public async Task InitializeAsync(string host, int port)
        {
            try
            {
                // 创建网络客户端
                var logger = NullLogger.Instance;
                var options = new NodeOptions
                {
                    AutoReconnect = true,
                    ReconnectInterval = TimeSpan.FromSeconds(5),
                    ConnectionTimeout = TimeSpan.FromSeconds(10)
                };

                // 使用DefaultPulseService (源生成器生成)
                var pulseService = new DefaultPulseService();
                
                // 输出消息通知用户源生成器正在工作
                Console.WriteLine("使用PulseRPC自动生成的序列化器...");
                Console.WriteLine("所有临时请求/响应对象将使用生成的MemoryPack格式化器");
                
                _client = new NetworkClient(logger, host, port, pulseService, options);

                // 连接到服务器
                await _client.ConnectAsync();

                Console.WriteLine($"已连接到服务器: {host}:{port}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"连接服务器失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 获取身份验证Hub
        /// </summary>
        public AuthStreamingHub GetAuthHub()
        {
            if (_client == null)
                throw new InvalidOperationException("客户端未初始化，请先调用InitializeAsync");

            return PulseRPCFactory.CreateClient<AuthStreamingHub>(_client);
        }

        /// <summary>
        /// 获取游戏Hub
        /// </summary>
        public GameStreamingHub GetGameHub()
        {
            if (_client == null)
                throw new InvalidOperationException("客户端未初始化，请先调用InitializeAsync");

            return PulseRPCFactory.CreateClient<GameStreamingHub>(_client);
        }

        /// <summary>
        /// 关闭连接
        /// </summary>
        public async Task DisconnectAsync()
        {
            try
            {
                if (_client != null)
                {
                    await Task.Run(() => _client.Close());
                    Console.WriteLine("已断开连接");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"断开连接时发生错误: {ex.Message}");
            }
        }
    }
}
