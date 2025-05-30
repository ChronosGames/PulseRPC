using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace PulseRPC.Benchmark.Client.Configuration;

/// <summary>
/// 客户端配置加载器
/// </summary>
public class ClientConfigurationLoader
{
    private readonly ILogger<ClientConfigurationLoader>? _logger;

    public ClientConfigurationLoader(ILogger<ClientConfigurationLoader>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// 从文件加载配置
    /// </summary>
    public async Task<ClientConfiguration> LoadFromFileAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"配置文件不存在: {filePath}");
        }

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            var config = JsonSerializer.Deserialize<ClientConfiguration>(json) ?? new ClientConfiguration();

            _logger?.LogInformation("配置文件加载成功: {FilePath}", filePath);
            return config;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"配置文件JSON格式错误: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"加载配置文件失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 创建默认配置
    /// </summary>
    public ClientConfiguration CreateDefault()
    {
        return new ClientConfiguration();
    }
}

/// <summary>
/// 客户端配置
/// </summary>
public class ClientConfiguration
{
    /// <summary>
    /// 服务器地址
    /// </summary>
    public string ServerAddress { get; set; } = "localhost:8080";

    /// <summary>
    /// 默认测试场景
    /// </summary>
    public string DefaultScenario { get; set; } = "ping-pong";

    /// <summary>
    /// 连接超时（毫秒）
    /// </summary>
    public int ConnectionTimeoutMs { get; set; } = 30000;

    /// <summary>
    /// 请求超时（毫秒）
    /// </summary>
    public int RequestTimeoutMs { get; set; } = 10000;

    /// <summary>
    /// 默认并发连接数
    /// </summary>
    public int DefaultConnections { get; set; } = 10;

    /// <summary>
    /// 默认请求速率
    /// </summary>
    public int DefaultRequestRate { get; set; } = 100;

    /// <summary>
    /// 默认测试持续时间（秒）
    /// </summary>
    public int DefaultDurationSeconds { get; set; } = 60;

    /// <summary>
    /// 启用重试
    /// </summary>
    public bool EnableRetry { get; set; } = true;

    /// <summary>
    /// 最大重试次数
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// 重试间隔（毫秒）
    /// </summary>
    public int RetryIntervalMs { get; set; } = 1000;

    /// <summary>
    /// 验证配置
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrEmpty(ServerAddress))
            throw new ArgumentException("服务器地址不能为空");

        if (ConnectionTimeoutMs <= 0)
            throw new ArgumentException("连接超时必须大于0");

        if (RequestTimeoutMs <= 0)
            throw new ArgumentException("请求超时必须大于0");

        if (DefaultConnections <= 0)
            throw new ArgumentException("默认连接数必须大于0");

        if (DefaultRequestRate <= 0)
            throw new ArgumentException("默认请求速率必须大于0");

        if (DefaultDurationSeconds <= 0)
            throw new ArgumentException("默认测试时长必须大于0");

        if (MaxRetryAttempts < 0)
            throw new ArgumentException("最大重试次数不能为负数");

        if (RetryIntervalMs < 0)
            throw new ArgumentException("重试间隔不能为负数");
    }
}
