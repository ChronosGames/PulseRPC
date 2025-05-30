using System.Threading.Tasks;
using PulseRPC.Benchmark.Metrics.Models;

namespace PulseRPC.Benchmark.Metrics.Exporters;

/// <summary>
/// 基准测试报告生成器接口
/// </summary>
public interface IBenchmarkReportGenerator
{
    /// <summary>
    /// 生成报告并返回字符串内容
    /// </summary>
    /// <param name="data">报告数据</param>
    /// <param name="config">报告配置</param>
    /// <returns>报告内容</returns>
    Task<string> GenerateReportAsync(BenchmarkReportData data, ReportConfiguration config);

    /// <summary>
    /// 生成报告并返回字节数组
    /// </summary>
    /// <param name="data">报告数据</param>
    /// <param name="config">报告配置</param>
    /// <returns>报告字节数组</returns>
    Task<byte[]> GenerateReportBytesAsync(BenchmarkReportData data, ReportConfiguration config);

    /// <summary>
    /// 生成报告并保存到文件
    /// </summary>
    /// <param name="data">报告数据</param>
    /// <param name="config">报告配置</param>
    /// <returns>保存的文件路径</returns>
    Task<string> GenerateReportToFileAsync(BenchmarkReportData data, ReportConfiguration config);

    /// <summary>
    /// 验证报告配置
    /// </summary>
    /// <param name="config">报告配置</param>
    /// <returns>验证结果</returns>
    Task<ReportValidationResult> ValidateConfigurationAsync(ReportConfiguration config);

    /// <summary>
    /// 获取支持的报告格式
    /// </summary>
    /// <returns>支持的格式列表</returns>
    ReportFormat[] GetSupportedFormats();
}

/// <summary>
/// 报告验证结果
/// </summary>
public class ReportValidationResult
{
    /// <summary>
    /// 是否有效
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// 错误消息列表
    /// </summary>
    public List<string> Errors { get; set; } = new();

    /// <summary>
    /// 警告消息列表
    /// </summary>
    public List<string> Warnings { get; set; } = new();
} 