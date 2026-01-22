using System.CommandLine;
using System.CommandLine.Parsing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PulseRPC.Benchmark.Client;
using PulseRPC.Benchmark.Models;
using PulseRPC.Benchmark.Server;

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
});

rootCommand.Subcommands.Add(serverCommand);
rootCommand.Subcommands.Add(clientCommand);
rootCommand.Subcommands.Add(listCommand);

return await rootCommand.Parse(args).InvokeAsync();
