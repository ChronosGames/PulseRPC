using Microsoft.Extensions.Logging;
using PulseRPC.Server.Builder;
using PulseRPC.Server.Transport;
using PulseRPC.Transport;

namespace PulseRPC.Server.Integration;

/// <summary>
/// KCP 传输提供程序 - 集成现有的 KcpServerListener
/// </summary>
internal sealed class KcpTransportProvider : ITransportProvider
{
    public string TransportType => PulseRPC.Transport.TransportType.Kcp.ToString();
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
        return new KcpServerListener(config.Port, kcpOptions,
            loggerFactory.CreateLogger<KcpServerListener>());
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
        if (config.Options?.Kcp != null)
        {
            var kcpOptions = config.Options.Kcp;

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

            if (kcpOptions.ReceiveWindow <= 0 || kcpOptions.ReceiveWindow > 2048)
            {
                warnings.Add($"KCP接收窗口大小可能不合理: {kcpOptions.ReceiveWindow}, 建议在32-512之间");
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
    private static TransportOptions CreateKcpOptions(TransportOptions? source)
    {
        var kcpOptions = new KcpTransportOptions();

        // 复制基础配置
        if (source != null)
        {
            kcpOptions.ReadBufferSize = source.ReadBufferSize;
            kcpOptions.WriteBufferSize = source.WriteBufferSize;
            kcpOptions.SendTimeout = source.SendTimeout;
            kcpOptions.ReceiveTimeout = source.ReceiveTimeout;
            kcpOptions.MaxPacketSize = source.MaxPacketSize;
        }

        // 设置默认值
        kcpOptions.ReadBufferSize = kcpOptions.ReadBufferSize == 0 ? 4096 : kcpOptions.ReadBufferSize;
        kcpOptions.WriteBufferSize = kcpOptions.WriteBufferSize == 0 ? 4096 : kcpOptions.WriteBufferSize;
        kcpOptions.MaxPacketSize = kcpOptions.MaxPacketSize == 0 ? 32 * 1024 : kcpOptions.MaxPacketSize;

        // 设置KCP特定配置
        kcpOptions.Kcp = new KcpOptions
        {
            NoDelay = source?.Kcp?.NoDelay ?? true,
            Interval = source?.Kcp?.Interval ?? 40,
            Resend = source?.Kcp?.Resend ?? 2,
            DisableFlowControl = source?.Kcp?.DisableFlowControl ?? false,
            SendWindow = source?.Kcp?.SendWindow ?? 128,
            ReceiveWindow = source?.Kcp?.ReceiveWindow ?? 128,
            FastReliable = source?.Kcp?.FastReliable ?? false
        };

        return kcpOptions;
    }
}
