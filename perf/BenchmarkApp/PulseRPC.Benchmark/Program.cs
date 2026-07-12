using System.CommandLine;
using System.CommandLine.Parsing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PulseRPC.Benchmark.Client;
using PulseRPC.Benchmark.Clustering;
using PulseRPC.Benchmark.Models;
using PulseRPC.Benchmark.Server;
using PulseRPC.Benchmark.Architecture;

var rootCommand = new RootCommand("PulseRPC 基准测试工具");

// ============ 服务端命令 ============
var serverCommand = new Command("server") { Description = "启动基准测试服务端" };
var tcpPortOption = new Option<int>("--tcp-port")
{
    Description = "TCP 监听端口",
    DefaultValueFactory = _ => 12345
};
serverCommand.Options.Add(tcpPortOption);

serverCommand.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
{
    var tcpPort = parseResult.GetValue(tcpPortOption);

    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    await BenchmarkServer.RunAsync(tcpPort, cts.Token);
});

// ============ 客户端命令 ============
var clientCommand = new Command("client") { Description = "运行基准测试场景" };

// 场景参数
var scenarioArgument = new Argument<string>("scenario") { Description = "测试场景: latency, throughput, upload, download, stability" };

// 通用选项
var hostOption = new Option<string>("--host")
{
    Description = "服务端主机地址",
    DefaultValueFactory = _ => "localhost"
};
var portOption = new Option<int>("--port")
{
    Description = "服务端端口",
    DefaultValueFactory = _ => 12345
};
var iterationsOption = new Option<int>("--iterations")
{
    Description = "迭代次数",
    DefaultValueFactory = _ => 10000
};
var durationOption = new Option<int>("--duration")
{
    Description = "持续时间（秒）",
    DefaultValueFactory = _ => 30
};
var connectionsOption = new Option<int>("--connections")
{
    Description = "并发连接数",
    DefaultValueFactory = _ => 1
};
var sizeOption = new Option<int>("--size")
{
    Description = "消息大小（字节）",
    DefaultValueFactory = _ => 1024
};
var warmupOption = new Option<int>("--warmup")
{
    Description = "预热迭代次数",
    DefaultValueFactory = _ => 100
};
var outputOption = new Option<string?>("--output")
{
    Description = "输出文件路径（JSON）"
};

clientCommand.Arguments.Add(scenarioArgument);
clientCommand.Options.Add(hostOption);
clientCommand.Options.Add(portOption);
clientCommand.Options.Add(iterationsOption);
clientCommand.Options.Add(durationOption);
clientCommand.Options.Add(connectionsOption);
clientCommand.Options.Add(sizeOption);
clientCommand.Options.Add(warmupOption);
clientCommand.Options.Add(outputOption);

clientCommand.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
{
    var scenario = parseResult.GetValue(scenarioArgument)!;
    var host = parseResult.GetValue(hostOption)!;
    var port = parseResult.GetValue(portOption);
    var iterations = parseResult.GetValue(iterationsOption);
    var duration = parseResult.GetValue(durationOption);
    var connections = parseResult.GetValue(connectionsOption);
    var size = parseResult.GetValue(sizeOption);
    var warmup = parseResult.GetValue(warmupOption);
    var output = parseResult.GetValue(outputOption);

    var services = new ServiceCollection();
    services.AddLogging(builder =>
    {
        builder.AddConsole();
        builder.SetMinimumLevel(LogLevel.Information);
    });

    var serviceProvider = services.BuildServiceProvider();
    var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

    var config = new BenchmarkConfig
    {
        Host = host,
        TcpPort = port,
        Iterations = iterations,
        DurationSeconds = duration,
        Connections = connections,
        MessageSize = size,
        WarmupIterations = warmup,
        OutputFile = output
    };

    var runner = new ScenarioRunner(loggerFactory);

    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    try
    {
        await runner.RunAsync(scenario, config, cts.Token);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"错误: {ex.Message}");
    }
});

// ============ 真实三节点 TCP 基准 ============
var clusterCommand = new Command("cluster-three-hop")
{
    Description = "启动三个 loopback 节点并测量 Gateway A -> B -> C（含 claims）真实 TCP 端到端性能"
};
var clusterWarmupOption = new Option<int>("--warmup")
{
    Description = "预热请求数",
    DefaultValueFactory = _ => 200
};
var clusterIterationsOption = new Option<int>("--iterations")
{
    Description = "计量请求数",
    DefaultValueFactory = _ => 10000
};
var clusterConcurrencyOption = new Option<int>("--concurrency")
{
    Description = "并发请求数",
    DefaultValueFactory = _ => Math.Max(1, Environment.ProcessorCount)
};
var clusterSmokeOption = new Option<bool>("--smoke")
{
    Description = "短跑验证：最多 5 次预热、30 次计量、2 并发"
};
clusterCommand.Options.Add(clusterWarmupOption);
clusterCommand.Options.Add(clusterIterationsOption);
clusterCommand.Options.Add(clusterConcurrencyOption);
clusterCommand.Options.Add(clusterSmokeOption);
clusterCommand.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
{
    await ThreeHopClusterBenchmark.RunAsync(
        parseResult.GetValue(clusterWarmupOption),
        parseResult.GetValue(clusterIterationsOption),
        parseResult.GetValue(clusterConcurrencyOption),
        parseResult.GetValue(clusterSmokeOption),
        cancellationToken);
});

// ============ 场景列表命令 ============
var listCommand = new Command("list") { Description = "列出可用的测试场景" };
listCommand.SetAction((ParseResult _) =>
{
    Console.WriteLine("可用的测试场景:");
    Console.WriteLine("  latency    - 测量单次RPC调用的往返时间(RTT)");
    Console.WriteLine("  throughput - 测量系统的吞吐量和处理能力");
    Console.WriteLine("  upload     - 测试客户端到服务端的数据传输带宽");
    Console.WriteLine("  download   - 测试服务端到客户端的数据传输带宽");
    Console.WriteLine("  stability  - 长时间运行测试，监控内存泄漏和连接稳定性");
    Console.WriteLine("  cluster-three-hop - 真实三节点 Gateway A -> B -> C TCP 端到端基准（独立命令）");
    Console.WriteLine("  architecture-baseline - 传输与 Actor 高并发延迟、分配和背压基线（独立命令）");
});

rootCommand.Subcommands.Add(serverCommand);
rootCommand.Subcommands.Add(clientCommand);
rootCommand.Subcommands.Add(clusterCommand);

// ============ 传输 / Actor 高并发架构基线 ============
var architectureCommand = new Command("architecture-baseline")
{
    Description = "测量传输背压与 Actor 高并发延迟、分配，导出或比较 JSON 基线"
};
var architectureOperationsOption = new Option<int>("--operations")
{
    Description = "每个主要场景的操作数",
    DefaultValueFactory = _ => 2000
};
var architectureConcurrencyOption = new Option<int>("--concurrency")
{
    Description = "并发 worker 数",
    DefaultValueFactory = _ => Math.Clamp(Environment.ProcessorCount * 4, 8, 64)
};
var architectureRepetitionsOption = new Option<int>("--repetitions")
{
    Description = "正式基准重复次数（1-9），报告逐指标中位数；smoke 固定为 1",
    DefaultValueFactory = _ => 3
};
var architectureSmokeOption = new Option<bool>("--smoke")
{
    Description = "使用最多 250 次操作 / 8 并发 / 1 轮快速验证"
};
var architectureOutputOption = new Option<string?>("--output")
{
    Description = "JSON 结果输出路径"
};
var architectureCompareOption = new Option<string?>("--compare")
{
    Description = "用于计算回归百分比的旧 JSON 基线"
};
var architectureRegressionOption = new Option<double>("--max-regression-percent")
{
    Description = "超过该百分比时以非零状态退出；0 仅报告不门禁",
    DefaultValueFactory = _ => 0
};
architectureCommand.Options.Add(architectureOperationsOption);
architectureCommand.Options.Add(architectureConcurrencyOption);
architectureCommand.Options.Add(architectureRepetitionsOption);
architectureCommand.Options.Add(architectureSmokeOption);
architectureCommand.Options.Add(architectureOutputOption);
architectureCommand.Options.Add(architectureCompareOption);
architectureCommand.Options.Add(architectureRegressionOption);
architectureCommand.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
{
    await ArchitectureBaselineBenchmark.RunAsync(
        parseResult.GetValue(architectureOperationsOption),
        parseResult.GetValue(architectureConcurrencyOption),
        parseResult.GetValue(architectureRepetitionsOption),
        parseResult.GetValue(architectureSmokeOption),
        parseResult.GetValue(architectureOutputOption),
        parseResult.GetValue(architectureCompareOption),
        parseResult.GetValue(architectureRegressionOption),
        cancellationToken);
});
rootCommand.Subcommands.Add(architectureCommand);
rootCommand.Subcommands.Add(listCommand);

return await rootCommand.Parse(args).InvokeAsync();
