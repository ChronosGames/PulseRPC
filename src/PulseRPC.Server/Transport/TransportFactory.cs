using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC.Transport;
using PulseRPC.Transport.Kcp;
using PulseRPC.Transport.Tcp;

namespace PulseRPC.Server.Transport;

/// <summary>
/// 传输工厂接口
/// </summary>
public interface IServerTransportFactory
{
    /// <summary>
    /// 创建服务端监听器
    /// </summary>
    Task<IServerListener> CreateListenerAsync(
        TransportType type,
        int port,
        TransportOptions? options = null);
}

/// <summary>
/// 传输工厂实现
/// </summary>
public class ServerTransportFactory : IServerTransportFactory
{
    private readonly ILoggerFactory? _loggerFactory;

    public ServerTransportFactory(ILoggerFactory? loggerFactory = null)
    {
        _loggerFactory = loggerFactory;
    }

    /// <summary>
    /// 创建服务端监听器
    /// </summary>
    public Task<IServerListener> CreateListenerAsync(
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
