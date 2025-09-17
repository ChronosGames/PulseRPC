// using System;
// using System.Threading.Tasks;
// using Microsoft.Extensions.DependencyInjection;
// using Microsoft.Extensions.Hosting;
// using Microsoft.Extensions.Logging;
// using PulseRPC.Server.Examples;
// using PulseRPC.Transport;
//
// namespace PulseRPC.Server.Examples;
//
// /// <summary>
// /// PulseRPC 高性能服务端演示程序
// /// 完整展示从字节流接收到响应发送的处理流程
// /// </summary>
// public class Program
// {
//     public static async Task Main(string[] args)
//     {
//         Console.WriteLine("=== PulseRPC 高性能服务端演示 ===");
//         Console.WriteLine("演示完整的服务端处理流程：");
//         Console.WriteLine("字节流接收 -> 消息解析 -> 反序列化 -> 消息调度 -> 服务处理 -> 响应序列化 -> 发送响应");
//         Console.WriteLine();
//
//         var host = CreateHostBuilder(args).Build();
//
//         try
//         {
//             Console.WriteLine("启动服务端...");
//             await host.RunAsync();
//         }
//         catch (Exception ex)
//         {
//             Console.WriteLine($"服务端运行失败: {ex.Message}");
//             Console.WriteLine($"异常详情: {ex}");
//         }
//         finally
//         {
//             Console.WriteLine("服务端已停止");
//         }
//     }
//
//     private static IHostBuilder CreateHostBuilder(string[] args) =>
//         Host.CreateDefaultBuilder(args)
//             .ConfigureServices((hostContext, services) =>
//             {
//                 // 配置日志
//                 services.AddLogging(builder =>
//                 {
//                     builder.AddConsole();
//                     builder.SetMinimumLevel(LogLevel.Information);
//                 });
//
//                 // 添加传输管理器（模拟实现）
//                 services.AddSingleton<ITransportManager, MockTransportManager>();
//
//                 // 添加 PulseRPC 高性能服务端
//                 services.AddPulseRPCHighPerformanceServer();
//             })
//             .ConfigureLogging(logging =>
//             {
//                 logging.AddConsole();
//                 logging.AddDebug();
//             });
// }
//
// /// <summary>
// /// 模拟传输管理器 - 用于演示目的
// /// 在实际应用中应该使用真实的 TCP/KCP 传输实现
// /// </summary>
// internal class MockTransportManager : ITransportManager
// {
//     private readonly ILogger<MockTransportManager> _logger;
//     private readonly TransportManager _realTransportManager;
//     private volatile bool _isRunning;
//
//     public MockTransportManager(ILogger<MockTransportManager> logger)
//     {
//         _logger = logger;
//         _realTransportManager = new TransportManager(TransportManagerType.Server, logger);
//     }
//
//     // ITransportManager 属性和方法的委托实现
//     public int ConnectionCount => _realTransportManager.ConnectionCount;
//     public IEnumerable<string> ConnectionIds => _realTransportManager.ConnectionIds;
//     public TransportManagerType ManagerType => _realTransportManager.ManagerType;
//
//     public TransportContext AddTransport(ITransport transport) => _realTransportManager.AddTransport(transport);
//     public TransportContext? GetTransportContext(string connectionId) => _realTransportManager.GetTransportContext(connectionId);
//     public Task<bool> RemoveTransportAsync(string connectionId) => _realTransportManager.RemoveTransportAsync(connectionId);
//     public IEnumerable<TransportContext> GetAllTransportContexts() => _realTransportManager.GetAllTransportContexts();
//     public IEnumerable<ITransport> GetTransportsByType(TransportType transportType) => _realTransportManager.GetTransportsByType(transportType);
//     public IEnumerable<TransportContext> GetAuthenticatedContexts() => _realTransportManager.GetAuthenticatedContexts();
//     public IEnumerable<TransportContext> GetContextsByUser(string username) => _realTransportManager.GetContextsByUser(username);
//     public Task<int> BroadcastAsync(ReadOnlyMemory<byte> data, Func<TransportContext, bool>? filter = null, CancellationToken cancellationToken = default) => _realTransportManager.BroadcastAsync(data, filter, cancellationToken);
//     public void SetDefaultTransport(string connectionId) => _realTransportManager.SetDefaultTransport(connectionId);
//     public ITransport? GetDefaultTransport() => _realTransportManager.GetDefaultTransport();
//     public TransportContext? GetDefaultTransportContext() => _realTransportManager.GetDefaultTransportContext();
//     public TransportManagerStatistics GetStatistics() => _realTransportManager.GetStatistics();
//     public void ResetStatistics() => _realTransportManager.ResetStatistics();
//
//     public event EventHandler<TransportConnectedEventArgs>? TransportConnected
//     {
//         add => _realTransportManager.TransportConnected += value;
//         remove => _realTransportManager.TransportConnected -= value;
//     }
//
//     public event EventHandler<TransportDisconnectedEventArgs>? TransportDisconnected
//     {
//         add => _realTransportManager.TransportDisconnected += value;
//         remove => _realTransportManager.TransportDisconnected -= value;
//     }
//
//     public event EventHandler<TransportAuthenticatedEventArgs>? TransportAuthenticated
//     {
//         add => _realTransportManager.TransportAuthenticated += value;
//         remove => _realTransportManager.TransportAuthenticated -= value;
//     }
//
//     public event EventHandler<TransportDataReceivedEventArgs>? DataReceived
//     {
//         add => _realTransportManager.DataReceived += value;
//         remove => _realTransportManager.DataReceived -= value;
//     }
//
//     public async Task StartAsync(CancellationToken cancellationToken = default)
//     {
//         _isRunning = true;
//         await _realTransportManager.StartAsync(cancellationToken);
//         _logger.LogInformation("模拟传输管理器已启动");
//
//         // 模拟接收数据的后台任务
//         _ = Task.Run(async () => await SimulateIncomingDataAsync(cancellationToken), cancellationToken);
//     }
//
//     public async Task StopAsync(CancellationToken cancellationToken = default)
//     {
//         _isRunning = false;
//         await _realTransportManager.StopAsync(cancellationToken);
//         _logger.LogInformation("模拟传输管理器已停止");
//     }
//
//     public async Task<bool> SendAsync(string connectionId, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
//     {
//         _logger.LogDebug("发送响应到连接 {ConnectionId}，数据长度: {Length} 字节", connectionId, data.Length);
//
//         // 模拟网络发送延迟
//         await Task.Delay(1, cancellationToken);
//
//         return true;
//     }
//
//     public void Dispose()
//     {
//         _isRunning = false;
//         _realTransportManager.Dispose();
//     }
//
//     public async ValueTask DisposeAsync()
//     {
//         _isRunning = false;
//         await _realTransportManager.DisposeAsync();
//     }
//
//     /// <summary>
//     /// 模拟接收传入数据
//     /// </summary>
//     private async Task SimulateIncomingDataAsync(CancellationToken cancellationToken)
//     {
//         var random = new Random();
//         var connectionCounter = 0;
//
//         while (_isRunning && !cancellationToken.IsCancellationRequested)
//         {
//             try
//             {
//                 // 每隔一段时间模拟新的连接和数据
//                 await Task.Delay(5000, cancellationToken);
//
//                 var connectionId = $"conn_{++connectionCounter}";
//
//                 // 创建模拟传输和上下文（一次性创建）
//                 var mockTransport = new MockServerTransport(connectionId);
//                 var context = _realTransportManager.AddTransport(mockTransport);
//
//                 // 模拟不同类型的请求消息
//                 await SimulateCalculatorRequest(context);
//                 await Task.Delay(1000, cancellationToken);
//
//                 await SimulateUserRequest(context);
//                 await Task.Delay(1000, cancellationToken);
//
//                 await SimulateHealthRequest(context);
//
//                 _logger.LogInformation("模拟连接 {ConnectionId} 的请求完成", context.ConnectionId);
//             }
//             catch (OperationCanceledException)
//             {
//                 break;
//             }
//             catch (Exception ex)
//             {
//                 _logger.LogError(ex, "模拟数据接收时发生异常");
//             }
//         }
//     }
//
//     /// <summary>
//     /// 模拟计算服务请求
//     /// </summary>
//     private async Task SimulateCalculatorRequest(string connectionId)
//     {
//         // 创建模拟的加法请求消息包
//         var messageId = Guid.NewGuid();
//         var addRequest = new AddRequest { A = 10, B = 20 };
//
//         var messagePacket = CreateMockMessagePacket(
//             connectionId,
//             messageId,
//             "PulseRPC.Server.Examples.ICalculatorService",
//             "AddAsync",
//             addRequest);
//
//         var data = SerializeMessagePacket(messagePacket);
//
//         // 创建模拟传输和上下文
//         var mockTransport = new MockServerTransport(connectionId);
//         var context = _realTransportManager.AddTransport(mockTransport);
//
//         // 触发数据接收事件
//         DataReceived?.Invoke(this, new TransportDataReceivedEventArgs(context, data));
//
//         _logger.LogDebug("模拟发送计算请求: {A} + {B}", addRequest.A, addRequest.B);
//     }
//
//     /// <summary>
//     /// 模拟用户服务请求
//     /// </summary>
//     private async Task SimulateUserRequest(string connectionId)
//     {
//         var messageId = Guid.NewGuid();
//         var getUserRequest = new GetUserRequest { UserId = 1 };
//
//         var messagePacket = CreateMockMessagePacket(
//             connectionId,
//             messageId,
//             "PulseRPC.Server.Examples.IUserService",
//             "GetUserAsync",
//             getUserRequest);
//
//         var data = SerializeMessagePacket(messagePacket);
//
//         DataReceived?.Invoke(this, new TransportDataReceivedEventArgs(
//             connectionId,
//             new ReadOnlySequence<byte>(data),
//             DateTime.UtcNow));
//
//         _logger.LogDebug("模拟发送用户查询请求: UserId = {UserId}", getUserRequest.UserId);
//     }
//
//     /// <summary>
//     /// 模拟健康检查请求
//     /// </summary>
//     private async Task SimulateHealthRequest(string connectionId)
//     {
//         var messageId = Guid.NewGuid();
//
//         var messagePacket = CreateMockMessagePacket(
//             connectionId,
//             messageId,
//             "PulseRPC.Server.Examples.IHealthService",
//             "CheckHealthAsync",
//             null);
//
//         var data = SerializeMessagePacket(messagePacket);
//
//         DataReceived?.Invoke(this, new TransportDataReceivedEventArgs(
//             connectionId,
//             new ReadOnlySequence<byte>(data),
//             DateTime.UtcNow));
//
//         _logger.LogDebug("模拟发送健康检查请求");
//     }
//
//     /// <summary>
//     /// 创建模拟消息包
//     /// </summary>
//     private MessagePacket CreateMockMessagePacket(string connectionId, Guid messageId, string serviceName, string methodName, object? requestData)
//     {
//         var header = new MessageHeader(MessageType.Request, serviceName, methodName)
//         {
//             MessageId = messageId,
//             Flags = MessageFlags.None
//         };
//
//         byte[] payload = Array.Empty<byte>();
//         if (requestData != null)
//         {
//             // 简化的序列化（实际应该使用 MemoryPack）
//             payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(requestData);
//         }
//
//         header.PayloadLength = payload.Length;
//
//         return new MessagePacket(header, payload);
//     }
//
//     /// <summary>
//     /// 序列化消息包为字节数组
//     /// </summary>
//     private byte[] SerializeMessagePacket(MessagePacket messagePacket)
//     {
//         // 估算大小并分配缓冲区
//         var estimatedSize = messagePacket.EstimateSize();
//         var buffer = new byte[estimatedSize + 100]; // 额外空间防止溢出
//
//         var bytesWritten = messagePacket.WriteTo(buffer);
//
//         // 返回实际大小的数组
//         var result = new byte[bytesWritten];
//         Array.Copy(buffer, result, bytesWritten);
//
//         return result;
//     }
// }
//
// /// <summary>
// /// 控制台日志格式化
// /// </summary>
// public static class ConsoleLoggerExtensions
// {
//     public static void LogServerFlow(this ILogger logger, string stage, string details)
//     {
//         logger.LogInformation("[{Stage}] {Details}", stage, details);
//     }
// }
//
// /// <summary>
// /// 模拟服务端传输实现
// /// </summary>
// internal class MockServerTransport : IServerTransport
// {
//     public string Name => "MockServer";
//     public TransportType Type => TransportType.Tcp;
//     public bool IsConnected => State == ConnectionState.Connected;
//     public ConnectionState State { get; private set; } = ConnectionState.Connected;
//     public EndPoint LocalEndPoint { get; } = new IPEndPoint(IPAddress.Loopback, 8080);
//     public EndPoint RemoteEndPoint { get; }
//     public string ConnectionId { get; }
//
//     public event EventHandler<TransportStateEventArgs>? StateChanged;
//     public event EventHandler<TransportDataEventArgs>? DataReceived;
//
//     public MockServerTransport(string connectionId)
//     {
//         ConnectionId = connectionId;
//         RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, Random.Shared.Next(10000, 65535));
//     }
//
//     public async Task<bool> SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
//     {
//         // 模拟发送延迟
//         await Task.Delay(1, cancellationToken);
//         return true;
//     }
//
//     public Task CloseAsync(CancellationToken cancellationToken = default)
//     {
//         State = ConnectionState.Disconnected;
//         StateChanged?.Invoke(this, new TransportStateEventArgs(ConnectionState.Connected, ConnectionState.Disconnected, "Closed"));
//         return Task.CompletedTask;
//     }
//
//     public void Dispose()
//     {
//         if (State != ConnectionState.Disconnected)
//         {
//             CloseAsync().GetAwaiter().GetResult();
//         }
//     }
// }
