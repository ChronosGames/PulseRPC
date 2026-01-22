using Microsoft.Extensions.Logging;
using PulseRPC.Benchmark.Contracts;
using PulseRPC.Benchmark.Models;
using PulseRPC.Benchmark.Reports;
using PulseRPC.Benchmark.Scenarios;
using PulseRPC.Client;
using PulseRPC.Client.Configuration;

namespace PulseRPC.Benchmark.Client;

/// <summary>
/// 场景运行器
/// </summary>
[PulseClientGeneration(typeof(IBenchmarkHub))]
public partial class ScenarioRunner
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<ScenarioRunner> _logger;
    private readonly Dictionary<string, Func<IScenario>> _scenarios;

    public ScenarioRunner(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<ScenarioRunner>();

        _scenarios = new Dictionary<string, Func<IScenario>>(StringComparer.OrdinalIgnoreCase)
        {
            ["latency"] = () => new EchoLatencyScenario(_loggerFactory.CreateLogger<EchoLatencyScenario>()),
            ["throughput"] = () => new ThroughputScenario(_loggerFactory.CreateLogger<ThroughputScenario>()),
            ["upload"] = () => new BandwidthScenario(_loggerFactory.CreateLogger<BandwidthScenario>(), isUpload: true),
            ["download"] = () => new BandwidthScenario(_loggerFactory.CreateLogger<BandwidthScenario>(), isUpload: false),
            ["stability"] = () => new StabilityScenario(_loggerFactory.CreateLogger<StabilityScenario>())
        };
    }

    /// <summary>
    /// 获取可用场景列表
    /// </summary>
    public IReadOnlyList<string> GetAvailableScenarios() => _scenarios.Keys.ToList();

    /// <summary>
    /// 运行指定场景
    /// </summary>
    public async Task<BenchmarkResult> RunAsync(string scenarioName, BenchmarkConfig config, CancellationToken cancellationToken = default)
    {
        if (!_scenarios.TryGetValue(scenarioName, out var scenarioFactory))
        {
            throw new ArgumentException($"未知的场景: {scenarioName}。可用场景: {string.Join(", ", _scenarios.Keys)}");
        }

        _logger.LogInformation("正在连接到服务端 {Host}:{Port}...", config.Host, config.TcpPort);

        // 创建客户端
        var builder = new PulseClientBuilder();
        // 设置 service 标签以便路由器能够找到正确的连接
        var tags = new Dictionary<string, string> { ["service"] = "IBenchmarkHub" };
        builder.AddTcpConnection("TcpChannel", "default", config.Host, config.TcpPort, tags: tags);
        using var client = builder.Build();

        // 初始化客户端（建立连接）
        await client.InitializeAsync(cancellationToken);

        // 获取服务代理
        var service = await client.GetServiceAsync<IBenchmarkHub>();

        // 健康检查
        var healthStatus = await service.HealthCheckAsync(new HealthCheckRequest(), cancellationToken);
        _logger.LogInformation("服务端健康状态: {Status}", healthStatus);

        // 创建并运行场景
        var scenario = scenarioFactory();
        _logger.LogInformation("运行场景: {Name} - {Description}", scenario.Name, scenario.Description);

        var result = await scenario.RunAsync(service, config, cancellationToken);

        // 输出结果
        ConsoleReporter.Print(result);

        // 保存 JSON 输出
        if (!string.IsNullOrEmpty(config.OutputFile))
        {
            await JsonReporter.SaveAsync(result, config.OutputFile);
            _logger.LogInformation("结果已保存到: {OutputFile}", config.OutputFile);
        }

        return result;
    }
}
