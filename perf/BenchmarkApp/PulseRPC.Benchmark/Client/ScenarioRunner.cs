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

        var connectionCount = Math.Max(1, config.Connections);
        _logger.LogInformation("正在创建 {Count} 个独立连接到服务端 {Host}:{Port}...", connectionCount, config.Host, config.TcpPort);

        // 创建多个独立的客户端连接
        var clients = new List<IPulseClient>();
        var services = new List<IBenchmarkHub>();

        try
        {
            for (int i = 0; i < connectionCount; i++)
            {
                var builder = new PulseClientBuilder();
                var tags = new Dictionary<string, string> { ["service"] = "IBenchmarkHub" };
                builder.AddTcpConnection($"TcpChannel-{i}", "default", config.Host, config.TcpPort, tags: tags);
                var client = builder.Build();
                clients.Add(client);

                await client.InitializeAsync(cancellationToken);
                var service = await client.GetServiceAsync<IBenchmarkHub>();
                services.Add(service);

                _logger.LogDebug("连接 {Index}/{Total} 已建立", i + 1, connectionCount);
            }

            _logger.LogInformation("所有 {Count} 个连接已建立", connectionCount);

            // 健康检查（使用第一个连接）
            var healthStatus = await services[0].HealthCheckAsync(new HealthCheckRequest());
            _logger.LogInformation("服务端健康状态: {Status}", healthStatus);

            // 创建并运行场景
            var scenario = scenarioFactory();
            _logger.LogInformation("运行场景: {Name} - {Description}", scenario.Name, scenario.Description);

            var result = await scenario.RunAsync(services, config, cancellationToken);

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
        finally
        {
            // 释放所有客户端连接
            foreach (var client in clients)
            {
                client.Dispose();
            }
        }
    }
}
