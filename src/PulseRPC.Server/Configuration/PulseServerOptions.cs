using PulseRPC.Server.Services;
using PulseRPC.Server.Processing.Pipeline;
using PulseRPC.Shared;

namespace PulseRPC.Server.Configuration;

/// <summary>
/// Configuration options for <see cref="PulseServer"/>.
/// </summary>
public sealed class PulseServerOptions
{
    // === Transport Configuration ===

    /// <summary>
    /// List of transport configurations.
    /// At least one transport must be configured, and exactly one must be marked as default.
    /// </summary>
    public List<TransportChannelConfiguration> Transports { get; set; } = new();

    // === Pipeline Configuration ===

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
    /// P-6：启用 facet 级 client-facing 可见性门闸的强制检查。
    /// </summary>
    /// <remarks>
    /// 默认 <c>false</c>，以保持向后兼容——现有项目在未主动开启前，未标注 <see cref="PulseRPC.ClientFacingAttribute"/>
    /// 的方法仍可像升级前一样被外部客户端调用。显式设置为 <c>true</c> 后，只有标注了
    /// <see cref="PulseRPC.ClientFacingAttribute"/>（facet 级或方法级）的方法才允许外部客户端调用，
    /// 其余方法即使实现了业务鉴权逻辑也会被协议框架层直接拒绝（<see cref="PulseRPC.Server.Security.ClientFacingAccessDeniedException"/>）。
    /// </remarks>
    public bool EnableClientFacingGate { get; set; } = false;

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
    public PulseServerOptions UsePreset(ServerPreset preset)
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
    public PulseServerOptions AddTcp(int port, bool isDefault = true, Action<TcpTransportOptions>? configure = null)
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
    public PulseServerOptions AddTcp(string name, int port, bool isDefault = true, Action<TcpTransportOptions>? configure = null)
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
    public PulseServerOptions AddKcp(int port, bool isDefault = false, Action<KcpTransportOptions>? configure = null)
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
    public PulseServerOptions AddKcp(string name, int port, bool isDefault = false, Action<KcpTransportOptions>? configure = null)
    {
        var options = new KcpTransportOptions();
        configure?.Invoke(options);
        Transports.Add(TransportChannelConfiguration.Kcp(name, port, options, isDefault));
        return this;
    }
}
