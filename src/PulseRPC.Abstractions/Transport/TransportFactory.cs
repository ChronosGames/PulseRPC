using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC.Transport.Kcp;
using PulseRPC.Transport.Tcp;

namespace PulseRPC.Transport
{
    /// <summary>
    /// 传输工厂实现
    /// </summary>
    public class TransportFactory : ITransportFactory
    {
        private readonly ILoggerFactory? _loggerFactory;

        public TransportFactory(ILoggerFactory? loggerFactory = null)
        {
            _loggerFactory = loggerFactory;
        }

        /// <summary>
        /// 创建客户端传输
        /// </summary>
        public Task<IClientTransport> CreateClientTransportAsync(
            TransportType type,
            TransportOptions? options = null)
        {
            options ??= new TransportOptions();

            IClientTransport transport = type switch
            {
                TransportType.Tcp => new TcpClientTransport(options, CreateLogger<TcpClientTransport>()),
                TransportType.Kcp => new KcpClientTransport(options, CreateLogger<KcpClientTransport>()),
                _ => throw new NotSupportedException($"不支持的传输类型: {type}")
            };

            return Task.FromResult(transport);
        }

        /// <summary>
        /// 创建服务端监听器
        /// </summary>
        public Task<IServerListener> CreateServerListenerAsync(
            TransportType type,
            int port,
            TransportOptions? options = null)
        {
            options ??= new TransportOptions();

            IServerListener listener = type switch
            {
                TransportType.Tcp => new TcpServerListener(port, options, CreateLogger<TcpServerListener>()),
                TransportType.Kcp => new KcpServerListener(port, options, CreateLogger<KcpServerListener>()),
                _ => throw new NotSupportedException($"不支持的传输类型: {type}")
            };

            return Task.FromResult(listener);
        }

        // 创建日志器
        private ILogger<T> CreateLogger<T>()
        {
            return _loggerFactory?.CreateLogger<T>() ?? NullLogger<T>.Instance;
        }
    }
}
