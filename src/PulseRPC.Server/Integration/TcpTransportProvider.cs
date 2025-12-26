using Microsoft.Extensions.Logging;
using PulseRPC.Server.Transport;
using PulseRPC.Transport;

namespace PulseRPC.Server.Integration;

/// <summary>
/// TCP 传输提供程序 - 集成现有的 TcpServerListener
/// </summary>
internal sealed class TcpTransportProvider : ITransportProvider
{
    public string TransportType => PulseRPC.Transport.TransportType.TCP.ToString();
    public string Name => "Built-in TCP Transport Provider";

    public IServerListener CreateServerListener(TransportChannelConfiguration config, ILoggerFactory loggerFactory)
    {
        // 验证配置
        var validation = ValidateConfiguration(config);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException($"TCP传输配置无效: {string.Join(", ", validation.Errors)}");
        }

        // 转换为TCP特定选项
        var tcpOptions = CreateTcpOptions(config.Options);

        // 创建现有的 TcpServerListener 实例
        return new TcpServerListener(config.Port, tcpOptions,
            loggerFactory.CreateLogger<TcpServerListener>());
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

        // 系统保留端口警告
        if (config.Port < 1024)
        {
            warnings.Add($"使用系统保留端口: {config.Port}, 可能需要管理员权限");
        }

        // TCP特定配置验证
        if (config.Options != null)
        {
            if (config.Options.RecvBufferSize <= 0)
            {
                errors.Add("读缓冲区大小必须大于0");
            }

            if (config.Options.SendBufferSize <= 0)
            {
                errors.Add("写缓冲区大小必须大于0");
            }

            if (config.Options.RecvBufferSize > 1024 * 1024) // 1MB
            {
                warnings.Add($"读缓冲区过大: {config.Options.RecvBufferSize} bytes, 建议小于1MB");
            }

            if (config.Options.SendBufferSize > 1024 * 1024) // 1MB
            {
                warnings.Add($"写缓冲区过大: {config.Options.SendBufferSize} bytes, 建议小于1MB");
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
    /// 创建TCP特定的传输选项
    /// </summary>
    private static TcpTransportOptions CreateTcpOptions(TransportOptions? source)
    {
        var opts = new TcpTransportOptions
        {
            // 基础网络选项
            RecvBufferSize = source?.RecvBufferSize ?? 8192,
            SendBufferSize = source?.SendBufferSize ?? 8192,
            NoDelay = source?.NoDelay ?? true,
            KeepAlive = source?.KeepAlive ?? true,
        };

        if (source != null && source is TcpTransportOptions o)
        {
            opts.LingerTime = o.LingerTime;
            // opts.SendTimeout = o.SendTimeout ?? 5000;
            // opts.RecvTimeout = source?.RecvTimeout ?? 0;

            // 继承其他选项
            // opts.MaxPacketSize = source?.MaxPacketSize ?? 64 * 1024;
            // opts.CompressionEnabled = source?.CompressionEnabled ?? false;
            // opts.CompressionThreshold = source?.CompressionThreshold ?? 1024;
        }

        return opts;
    }
}
