using PulseRPC.Server.Core;
using PulseRPC.Server.Pipeline;
using PulseRPC.Transport;

namespace PulseRPC.Server.Configuration;

/// <summary>
/// Configuration options for UnifiedPulseServer.
/// Consolidates options from both PulseServer and ServerHost architectures.
/// </summary>
public sealed class UnifiedServerOptions
{
    // === Transport Configuration (from PulseServer) ===

    /// <summary>
    /// List of transport configurations.
    /// At least one transport must be configured, and exactly one must be marked as default.
    /// </summary>
    public List<TransportChannelConfiguration> Transports { get; set; } = new();

    // === Pipeline Configuration (from ServerHost) ===

    /// <summary>
    /// Configuration for message receiver component.
    /// </summary>
    public MessageReceiverOptions MessageReceiver { get; set; } = new();

    /// <summary>
    /// Configuration for response transmitter component.
    /// </summary>
    public ResponseTransmitterOptions ResponseTransmitter { get; set; } = new();

    /// <summary>
    /// Configuration for backpressure policy component.
    /// </summary>
    public BackpressurePolicyOptions BackpressurePolicy { get; set; } = new();

    // === General Server Options ===

    /// <summary>
    /// Default timeout for server operations.
    /// Default: 30 seconds.
    /// </summary>
    public TimeSpan DefaultOperationTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum number of concurrent operations.
    /// Default: 1000.
    /// </summary>
    public int MaxConcurrentOperations { get; set; } = 1000;

    /// <summary>
    /// Enable detailed logging for debugging.
    /// Default: false.
    /// </summary>
    public bool EnableDetailedLogging { get; set; } = false;

    /// <summary>
    /// Validates the configuration options.
    /// </summary>
    /// <exception cref="InvalidOperationException">Configuration is invalid</exception>
    public void Validate()
    {
        if (Transports.Count == 0)
            throw new InvalidOperationException("At least one transport must be configured");

        var defaultCount = Transports.Count(t => t.IsDefault);
        if (defaultCount != 1)
            throw new InvalidOperationException($"Exactly one transport must be marked as default (found {defaultCount})");

        var uniqueNames = Transports.Select(t => t.Name).Distinct().Count();
        if (uniqueNames != Transports.Count)
            throw new InvalidOperationException("Transport names must be unique");

        // Validate port ranges
        foreach (var transport in Transports)
        {
            if (transport.Port < 1 || transport.Port > 65535)
                throw new InvalidOperationException($"Transport '{transport.Name}' port must be between 1 and 65535 (got {transport.Port})");
        }

        if (DefaultOperationTimeout <= TimeSpan.Zero)
            throw new InvalidOperationException("DefaultOperationTimeout must be positive");

        if (MaxConcurrentOperations <= 0)
            throw new InvalidOperationException("MaxConcurrentOperations must be greater than zero");
    }

    /// <summary>
    /// 应用预设配置
    /// </summary>
    /// <param name="preset">预设模式</param>
    /// <returns>当前选项实例，支持链式调用</returns>
    public UnifiedServerOptions UsePreset(ServerPreset preset)
    {
        ServerPresets.ApplyPreset(this, preset);
        return this;
    }

    /// <summary>
    /// 添加 TCP 传输
    /// </summary>
    /// <param name="port">监听端口</param>
    /// <param name="isDefault">是否为默认传输</param>
    /// <param name="configure">传输选项配置</param>
    /// <returns>当前选项实例，支持链式调用</returns>
    public UnifiedServerOptions AddTcp(int port, bool isDefault = true, Action<TcpTransportOptions>? configure = null)
    {
        var options = new TcpTransportOptions();
        configure?.Invoke(options);
        Transports.Add(TransportChannelConfiguration.Tcp($"tcp-{port}", port, options, isDefault));
        return this;
    }

    /// <summary>
    /// 添加 TCP 传输（带名称）
    /// </summary>
    /// <param name="name">传输名称</param>
    /// <param name="port">监听端口</param>
    /// <param name="isDefault">是否为默认传输</param>
    /// <param name="configure">传输选项配置</param>
    /// <returns>当前选项实例，支持链式调用</returns>
    public UnifiedServerOptions AddTcp(string name, int port, bool isDefault = true, Action<TcpTransportOptions>? configure = null)
    {
        var options = new TcpTransportOptions();
        configure?.Invoke(options);
        Transports.Add(TransportChannelConfiguration.Tcp(name, port, options, isDefault));
        return this;
    }

    /// <summary>
    /// 添加 KCP 传输
    /// </summary>
    /// <param name="port">监听端口</param>
    /// <param name="isDefault">是否为默认传输</param>
    /// <param name="configure">传输选项配置</param>
    /// <returns>当前选项实例，支持链式调用</returns>
    public UnifiedServerOptions AddKcp(int port, bool isDefault = false, Action<KcpTransportOptions>? configure = null)
    {
        var options = new KcpTransportOptions();
        configure?.Invoke(options);
        Transports.Add(TransportChannelConfiguration.Kcp($"kcp-{port}", port, options, isDefault));
        return this;
    }

    /// <summary>
    /// 添加 KCP 传输（带名称）
    /// </summary>
    /// <param name="name">传输名称</param>
    /// <param name="port">监听端口</param>
    /// <param name="isDefault">是否为默认传输</param>
    /// <param name="configure">传输选项配置</param>
    /// <returns>当前选项实例，支持链式调用</returns>
    public UnifiedServerOptions AddKcp(string name, int port, bool isDefault = false, Action<KcpTransportOptions>? configure = null)
    {
        var options = new KcpTransportOptions();
        configure?.Invoke(options);
        Transports.Add(TransportChannelConfiguration.Kcp(name, port, options, isDefault));
        return this;
    }
}
