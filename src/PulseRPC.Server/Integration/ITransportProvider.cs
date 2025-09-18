using Microsoft.Extensions.Logging;
using PulseRPC.Server.Transport;
using PulseRPC.Transport;

namespace PulseRPC.Server.Integration;

/// <summary>
/// 传输提供程序接口 - 抽象各种传输协议的工厂
/// </summary>
public interface ITransportProvider
{
    /// <summary>
    /// 支持的传输类型
    /// </summary>
    string TransportType { get; }

    /// <summary>
    /// 提供程序名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 创建服务器监听器
    /// </summary>
    /// <param name="config">传输配置</param>
    /// <param name="loggerFactory">日志工厂</param>
    /// <returns>服务器监听器</returns>
    IServerListener CreateServerListener(TransportChannelConfiguration config, ILoggerFactory loggerFactory);

    /// <summary>
    /// 验证传输配置
    /// </summary>
    /// <param name="config">传输配置</param>
    /// <returns>验证结果</returns>
    TransportValidationResult ValidateConfiguration(TransportChannelConfiguration config);
}

/// <summary>
/// 传输配置验证结果
/// </summary>
public sealed class TransportValidationResult
{
    public bool IsValid { get; init; }
    public List<string> Errors { get; init; } = new();
    public List<string> Warnings { get; init; } = new();

    public static TransportValidationResult Valid() => new() { IsValid = true };

    public static TransportValidationResult Invalid(params string[] errors) =>
        new() { IsValid = false, Errors = errors.ToList() };

    public static TransportValidationResult ValidWithWarnings(params string[] warnings) =>
        new() { IsValid = true, Warnings = warnings.ToList() };
}
