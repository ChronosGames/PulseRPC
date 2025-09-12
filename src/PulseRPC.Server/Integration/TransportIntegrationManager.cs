using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Transport;
using PulseRPC.Transport;

namespace PulseRPC.Server.Integration;

/// <summary>
/// 传输层集成管理器实现 - 高性能线程安全设计
/// </summary>
internal sealed class TransportIntegrationManager : ITransportIntegrationManager
{
    private readonly ConcurrentDictionary<string, ITransportProvider> _providers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<TransportIntegrationManager> _logger;

    public TransportIntegrationManager(IEnumerable<ITransportProvider> providers,
        ILogger<TransportIntegrationManager> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        // 自动注册所有传输提供程序
        foreach (var provider in providers ?? Enumerable.Empty<ITransportProvider>())
        {
            RegisterProvider(provider);
        }

        _logger.LogInformation("传输层集成管理器已初始化，支持的传输类型: [{TransportTypes}]", 
            string.Join(", ", _providers.Keys));
    }

    public void RegisterProvider(ITransportProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        
        if (string.IsNullOrWhiteSpace(provider.TransportType))
        {
            throw new ArgumentException("传输类型不能为空", nameof(provider));
        }

        var added = _providers.TryAdd(provider.TransportType, provider);
        if (added)
        {
            _logger.LogInformation("已注册传输提供程序: {TransportType} - {Name}", 
                provider.TransportType, provider.Name);
        }
        else
        {
            _logger.LogWarning("传输提供程序已存在，跳过注册: {TransportType} - {Name}", 
                provider.TransportType, provider.Name);
        }
    }

    public IServerListener CreateListener(TransportChannelConfiguration config, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        var transportType = config.Type.ToString();
        
        if (!_providers.TryGetValue(transportType, out var provider))
        {
            throw new NotSupportedException($"不支持的传输类型: {transportType}. " +
                $"支持的类型: [{string.Join(", ", _providers.Keys)}]");
        }

        try
        {
            _logger.LogDebug("正在创建传输监听器: {TransportType}, 端口: {Port}, 名称: {Name}", 
                transportType, config.Port, config.Name);

            var listener = provider.CreateServerListener(config, loggerFactory);
            
            _logger.LogInformation("已创建传输监听器: {TransportType}, 端口: {Port}, 名称: {Name}", 
                transportType, config.Port, config.Name);

            return listener;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建传输监听器失败: {TransportType}, 端口: {Port}", 
                transportType, config.Port);
            throw;
        }
    }

    public IReadOnlyList<string> GetSupportedTransportTypes()
    {
        return _providers.Keys.ToList();
    }

    public bool IsSupported(string transportType)
    {
        return !string.IsNullOrWhiteSpace(transportType) && 
               _providers.ContainsKey(transportType);
    }
}