using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC.Client.Transport;
using PulseRPC.Transport;
using PulseRPC.Transport.Kcp;
using PulseRPC.Transport.Tcp;

namespace PulseRPC.Client
{
    public interface IClientTransportFactory
    {
        /// <summary>
        /// 创建客户端传输
        /// </summary>
        Task<IClientTransport> CreateTransportAsync(
            TransportType type,
            TransportOptions? options = null);
    }

    /// <summary>
    /// 传输工厂实现
    /// </summary>
    public class TransportFactory : IClientTransportFactory
    {
        private readonly ILoggerFactory? _loggerFactory;

        public TransportFactory(ILoggerFactory? loggerFactory = null)
        {
            _loggerFactory = loggerFactory;
        }

        /// <summary>
        /// 创建客户端传输
        /// </summary>
        public Task<IClientTransport> CreateTransportAsync(
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

        // 创建日志器
        private ILogger<T> CreateLogger<T>()
        {
            return _loggerFactory?.CreateLogger<T>() ?? NullLogger<T>.Instance;
        }
    }
}
