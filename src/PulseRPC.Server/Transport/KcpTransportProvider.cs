using Microsoft.Extensions.Logging;
using PulseRPC.Server.Transport;
using PulseRPC.Shared;

namespace PulseRPC.Server.Transport;

/// <summary>
/// KCP 传输提供程序 - 集成现有的 KcpServerListener
/// </summary>
internal sealed class KcpTransportProvider : ITransportProvider
{
    public string TransportType => PulseRPC.Shared.TransportType.KCP.ToString();
    public string Name => "Built-in KCP Transport Provider";

    public IServerListener CreateServerListener(TransportChannelConfiguration config, ILoggerFactory loggerFactory)
    {
        // 验证配置
        var validation = ValidateConfiguration(config);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException($"KCP传输配置无效: {string.Join(", ", validation.Errors)}");
        }

        // 转换为KCP特定选项
        var kcpOptions = CreateKcpOptions(config.Options);

        // 创建现有的 KcpServerListener 实例
        return new KcpServerListener(config.Port, kcpOptions, loggerFactory.CreateLogger<KcpServerListener>());
    }

    public TransportValidationResult ValidateConfiguration(TransportChannelConfiguration config)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        // 端口验证
        if (config.Port <= 0 || config.Port > 65535)
        {
            errors.Add($"端口号无效: {config.Port}, 必须在 1-65535 范围内");
        }

        // KCP特定配置验证
        if (config.Options != null && config.Options is KcpTransportOptions kcpOptions)
        {

            // KCP更新间隔验证
            if (kcpOptions.Interval <= 0 || kcpOptions.Interval > 1000)
            {
                errors.Add($"KCP更新间隔无效: {kcpOptions.Interval}ms, 建议在10-100毫秒之间");
            }
            else if (kcpOptions.Interval < 10)
            {
                warnings.Add($"KCP更新间隔过小: {kcpOptions.Interval}ms, 可能导致CPU使用率过高");
            }

            // 窗口大小验证
            if (kcpOptions.SendWindow <= 0 || kcpOptions.SendWindow > 2048)
            {
                warnings.Add($"KCP发送窗口大小可能不合理: {kcpOptions.SendWindow}, 建议在32-512之间");
            }

            if (kcpOptions.RecvWindow <= 0 || kcpOptions.RecvWindow > 2048)
            {
                warnings.Add($"KCP接收窗口大小可能不合理: {kcpOptions.RecvWindow}, 建议在32-512之间");
            }

            // 重传设置验证
            if (kcpOptions.Resend < 0 || kcpOptions.Resend > 4)
            {
                warnings.Add($"KCP重传模式设置可能不合理: {kcpOptions.Resend}, 建议在0-2之间");
            }
        }

        if (errors.Count > 0)
        {
            return new TransportValidationResult { IsValid = false, Errors = errors, Warnings = warnings };
        }

        if (warnings.Count > 0)
        {
            return TransportValidationResult.ValidWithWarnings(warnings.ToArray());
        }

        return TransportValidationResult.Valid();
    }

    /// <summary>
    /// 创建KCP特定的传输选项
    /// </summary>
    private static KcpTransportOptions CreateKcpOptions(TransportOptions? source)
    {
        var kcpOptions = new KcpTransportOptions();

        if (source == null)
        {
            return kcpOptions;
        }

        kcpOptions.NoDelay = source.NoDelay;
        kcpOptions.RecvBufferSize = source.RecvBufferSize;
        kcpOptions.SendBufferSize = source.SendBufferSize;
        kcpOptions.MaxPacketSize = source.MaxPacketSize;
        // kcpOptions.SendTimeout = source.SendTimeout;
        // kcpOptions.RecvTimeout = source.RecvTimeout;

        // 设置默认值
        // MaxPacketSize = kcpOptions.MaxPacketSize == 0 ? 32 * 1024 : kcpOptions.MaxPacketSize;

        // 设置KCP特定配置
        if (source is KcpTransportOptions kcpOpts)
        {
            kcpOptions.Interval = kcpOpts?.Interval ?? 40;
            kcpOptions.Resend = kcpOpts?.Resend ?? 2;
            kcpOptions.DisableFlowControl = kcpOpts?.DisableFlowControl ?? false;
            kcpOptions.SendWindow = kcpOpts?.SendWindow ?? 128;
            kcpOptions.RecvWindow = kcpOpts?.RecvWindow ?? 128;
        }

        return kcpOptions;
    }
}
