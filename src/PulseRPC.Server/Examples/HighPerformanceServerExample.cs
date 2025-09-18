// using System;
// using System.Threading;
// using System.Threading.Tasks;
// using Microsoft.Extensions.DependencyInjection;
// using Microsoft.Extensions.Hosting;
// using Microsoft.Extensions.Logging;
// using PulseRPC.Serialization;
// using PulseRPC.Server.Dispatch;
// using PulseRPC.Server.Network;
// using PulseRPC.Server.Response;
// using PulseRPC.Server.Serialization;
// using PulseRPC.Server.Services;
// using PulseRPC.Transport;
//
// namespace PulseRPC.Server.Examples;
//
//
// /// <summary>
// /// 高性能 PulseRPC 服务端完整示例
// /// 演示从字节流接收到响应发送的完整流程
// /// </summary>
// public class HighPerformanceServerExample
// {
//     private readonly ILogger<HighPerformanceServerExample> _logger;
//     private readonly IServiceProvider _serviceProvider;
//
//     // 核心组件
//     private INetworkProcessor? _networkProcessor;
//     private IMessageDeserializer? _messageDeserializer;
//     private IMessageDispatcher? _messageDispatcher;
//     private IServiceProcessor? _serviceProcessor;
//     private IResponseProcessor? _responseProcessor;
//     private ITransportManager? _transportManager;
//
//     // 取消令牌
//     private readonly CancellationTokenSource _shutdownCts = new();
//
//     public HighPerformanceServerExample(IServiceProvider serviceProvider, ILogger<HighPerformanceServerExample> logger)
//     {
//         _serviceProvider = serviceProvider;
//         _logger = logger;
//     }
//
//     /// <summary>
//     /// 启动高性能服务端
//     /// </summary>
//     public async Task StartAsync()
//     {
//         _logger.LogInformation("启动高性能 PulseRPC 服务端...");
//
//         try
//         {
//             // 1. 初始化传输管理器
//             await InitializeTransportManagerAsync();
//
//             // 2. 初始化核心处理组件
//             await InitializeProcessingComponentsAsync();
//
//             // 3. 注册示例服务
//             RegisterExampleServices();
//
//             // 4. 连接处理管道
//             ConnectProcessingPipeline();
//
//             // 5. 启动所有组件
//             await StartAllComponentsAsync();
//
//             _logger.LogInformation("PulseRPC 服务端启动完成");
//         }
//         catch (Exception ex)
//         {
//             _logger.LogError(ex, "服务端启动失败");
//             throw;
//         }
//     }
//
//     /// <summary>
//     /// 停止服务端
//     /// </summary>
//     public async Task StopAsync()
//     {
//         _logger.LogInformation("停止 PulseRPC 服务端...");
//
//         // 触发关闭信号
//         _shutdownCts.Cancel();
//
//         // 按顺序停止组件
//         var tasks = new[]
//         {
//             _responseProcessor?.StopAsync() ?? Task.CompletedTask,
//             _messageDispatcher?.StopAsync() ?? Task.CompletedTask,
//             _messageDeserializer?.StopAsync() ?? Task.CompletedTask,
//             _networkProcessor?.StopAsync() ?? Task.CompletedTask,
//             _transportManager?.StopAsync() ?? Task.CompletedTask
//         };
//
//         await Task.WhenAll(tasks);
//         _logger.LogInformation("PulseRPC 服务端已停止");
//     }
//
//     /// <summary>
//     /// 初始化传输管理器
//     /// </summary>
//     private async Task InitializeTransportManagerAsync()
//     {
//         _transportManager = _serviceProvider.GetRequiredService<ITransportManager>();
//
//         // 监听传输数据事件 - 这是数据流的起点
//         _transportManager.DataReceived += OnTransportDataReceived;
//
//         await _transportManager.StartAsync(_shutdownCts.Token);
//         _logger.LogInformation("传输管理器初始化完成");
//     }
//
//     /// <summary>
//     /// 初始化处理组件
//     /// </summary>
//     private async Task InitializeProcessingComponentsAsync()
//     {
//         // 1. 网络处理器 - 负责字节流解析
//         var networkOptions = new NetworkProcessorOptions
//         {
//             ProcessorThreadCount = Environment.ProcessorCount,
//             MaxMessageSize = 16 * 1024 * 1024 // 16MB
//         };
//         _networkProcessor = new HighPerformanceNetworkProcessor(
//             networkOptions,
//             _serviceProvider.GetService<ILogger<HighPerformanceNetworkProcessor>>());
//
//         // 2. 反序列化器 - 负责消息反序列化
//         var deserializerOptions = new DeserializerOptions
//         {
//             ProcessorThreadCount = Math.Max(1, Environment.ProcessorCount / 2),
//             ChannelCapacity = 10000
//         };
//         _messageDeserializer = new HighPerformanceDeserializer(
//             _serviceProvider.GetService<ISerializerProvider>(),
//             deserializerOptions,
//             _serviceProvider.GetService<ILogger<HighPerformanceDeserializer>>());
//
//         // 3. 消息调度器 - 负责消息路由和优先级调度
//         var dispatcherOptions = new DispatcherOptions
//         {
//             DispatcherThreadCount = Environment.ProcessorCount,
//             ChannelCapacity = 10000,
//             EnableLoadBalancing = true,
//             EnableStatistics = true
//         };
//         _messageDispatcher = new HighPerformanceMessageDispatcher(
//             _serviceProvider,
//             dispatcherOptions,
//             _serviceProvider.GetService<ILogger<HighPerformanceMessageDispatcher>>());
//
//         // 4. 服务处理器 - 负责服务注册和方法调用
//         _serviceProcessor = new HighPerformanceServiceProcessor(
//             _serviceProvider,
//             _serviceProvider.GetService<ISerializerProvider>(),
//             _serviceProvider.GetService<ILogger<HighPerformanceServiceProcessor>>());
//
//         // 5. 响应处理器 - 负责响应序列化和发送
//         var responseOptions = new ResponseProcessorOptions
//         {
//             ProcessorThreadCount = Math.Max(1, Environment.ProcessorCount / 2),
//             ChannelCapacity = 10000,
//             IncludeStackTrace = false
//         };
//         _responseProcessor = new HighPerformanceResponseProcessor(
//             _transportManager,
//             _serviceProvider.GetService<ISerializerProvider>(),
//             responseOptions,
//             _serviceProvider.GetService<ILogger<HighPerformanceResponseProcessor>>());
//
//         _logger.LogInformation("所有处理组件初始化完成");
//     }
//
//     /// <summary>
//     /// 注册示例服务
//     /// </summary>
//     private void RegisterExampleServices()
//     {
//         // 注册计算服务
//         _serviceProcessor!.RegisterService<ICalculatorHub, CalculatorHub>();
//
//         // 注册用户服务
//         _serviceProcessor.RegisterService<IUserHub, UserHub>();
//
//         // 注册单例服务
//         var healthService = new HealthHub();
//         _serviceProcessor.RegisterService<IHealthHub>(healthService);
//
//         // 将服务处理器注册到消息调度器
//         foreach (var serviceInfo in _serviceProcessor.GetRegisteredServices())
//         {
//             var handler = _serviceProcessor.GetServiceHandler(serviceInfo.ServiceName);
//             if (handler != null)
//             {
//                 _messageDispatcher!.RegisterServiceHandler(serviceInfo.ServiceName, handler);
//             }
//         }
//
//         _logger.LogInformation("已注册 {ServiceCount} 个服务", _serviceProcessor.GetRegisteredServices().Length);
//     }
//
//     /// <summary>
//     /// 连接处理管道
//     /// </summary>
//     private void ConnectProcessingPipeline()
//     {
//         // 1. 网络处理器 -> 反序列化器
//         _networkProcessor!.MessageParsed += async (sender, args) =>
//         {
//             await _messageDeserializer!.ProcessMessagePacketAsync(args);
//         };
//
//         // 2. 反序列化器 -> 消息调度器
//         _messageDeserializer!.MessageDeserialized += async (sender, args) =>
//         {
//             await _messageDispatcher!.DispatchMessageAsync(args);
//         };
//
//         // 3. 消息调度器 -> 响应处理器
//         _messageDispatcher!.MessageProcessed += async (sender, args) =>
//         {
//             await _responseProcessor!.ProcessMessageResultAsync(args);
//         };
//
//         _logger.LogInformation("处理管道连接完成");
//     }
//
//     /// <summary>
//     /// 启动所有组件
//     /// </summary>
//     private async Task StartAllComponentsAsync()
//     {
//         await _networkProcessor!.StartAsync(_shutdownCts.Token);
//         await _messageDeserializer!.StartAsync(_shutdownCts.Token);
//         await _messageDispatcher!.StartAsync(_shutdownCts.Token);
//         await _responseProcessor!.StartAsync(_shutdownCts.Token);
//
//         _logger.LogInformation("所有处理组件已启动");
//     }
//
//     /// <summary>
//     /// 传输数据接收事件处理 - 数据流入口
//     /// </summary>
//     private async void OnTransportDataReceived(object? sender, TransportDataReceivedEventArgs e)
//     {
//         try
//         {
//             // 将传输数据交给网络处理器进行解析
//             var connectionId = e.TransportContext?.ConnectionId ?? e.ConnectionId;
//
//             // 创建 ReadOnlySequence<byte> 用于网络处理器
//             var sequence = new ReadOnlySequence<byte>(e.Data);
//             await _networkProcessor!.ProcessTransportDataAsync(connectionId, sequence);
//         }
//         catch (Exception ex)
//         {
//             _logger.LogError(ex, "处理传输数据失败: 连接 {ConnectionId}",
//                 e.TransportContext?.ConnectionId ?? e.ConnectionId);
//         }
//     }
//
//     public void Dispose()
//     {
//         _shutdownCts?.Cancel();
//         _shutdownCts?.Dispose();
//
//         _networkProcessor?.Dispose();
//         _messageDeserializer?.Dispose();
//         _messageDispatcher?.Dispose();
//         _responseProcessor?.Dispose();
//     }
// }
//
// /// <summary>
// /// 计算服务接口
// /// </summary>
// public interface ICalculatorHub : IPulseHub
// {
//     Task<int> AddAsync(AddRequest request, CancellationToken cancellationToken = default);
//     Task<double> DivideAsync(DivideRequest request, CancellationToken cancellationToken = default);
//     Task<string> GetVersionAsync(CancellationToken cancellationToken = default);
// }
//
// /// <summary>
// /// 计算服务实现
// /// </summary>
// public class CalculatorHub : ICalculatorHub
// {
//     private readonly ILogger<CalculatorHub> _logger;
//
//     public CalculatorHub(ILogger<CalculatorHub> logger)
//     {
//         _logger = logger;
//     }
//
//     public async Task<int> AddAsync(AddRequest request, CancellationToken cancellationToken = default)
//     {
//         _logger.LogDebug("执行加法运算: {A} + {B}", request.A, request.B);
//
//         // 模拟一些异步处理
//         await Task.Delay(1, cancellationToken);
//
//         var result = request.A + request.B;
//         _logger.LogDebug("加法运算结果: {Result}", result);
//
//         return result;
//     }
//
//     public async Task<double> DivideAsync(DivideRequest request, CancellationToken cancellationToken = default)
//     {
//         _logger.LogDebug("执行除法运算: {A} / {B}", request.Dividend, request.Divisor);
//
//         if (request.Divisor == 0)
//         {
//             throw new ArgumentException("除数不能为零", nameof(request.Divisor));
//         }
//
//         await Task.Delay(2, cancellationToken);
//
//         var result = (double)request.Dividend / request.Divisor;
//         _logger.LogDebug("除法运算结果: {Result}", result);
//
//         return result;
//     }
//
//     public async Task<string> GetVersionAsync(CancellationToken cancellationToken = default)
//     {
//         await Task.Delay(1, cancellationToken);
//         return "Calculator Service v1.0.0";
//     }
// }
//
// /// <summary>
// /// 用户服务接口
// /// </summary>
// public interface IUserHub : IPulseHub
// {
//     Task<UserInfo> GetUserAsync(GetUserRequest request, CancellationToken cancellationToken = default);
//     Task<bool> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken = default);
// }
//
// /// <summary>
// /// 用户服务实现
// /// </summary>
// public class UserHub : IUserHub
// {
//     private readonly ILogger<UserHub> _logger;
//     private static readonly ConcurrentDictionary<int, UserInfo> _users = new();
//
//     static UserHub()
//     {
//         // 初始化一些测试用户
//         _users[1] = new UserInfo { Id = 1, Name = "张三", Email = "zhangsan@example.com" };
//         _users[2] = new UserInfo { Id = 2, Name = "李四", Email = "lisi@example.com" };
//     }
//
//     public UserHub(ILogger<UserHub> logger)
//     {
//         _logger = logger;
//     }
//
//     public async Task<UserInfo> GetUserAsync(GetUserRequest request, CancellationToken cancellationToken = default)
//     {
//         _logger.LogDebug("获取用户信息: ID = {UserId}", request.UserId);
//
//         await Task.Delay(5, cancellationToken);
//
//         if (_users.TryGetValue(request.UserId, out var user))
//         {
//             return user;
//         }
//
//         throw new InvalidOperationException($"用户 {request.UserId} 不存在");
//     }
//
//     public async Task<bool> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken = default)
//     {
//         _logger.LogDebug("创建用户: {Name}", request.Name);
//
//         await Task.Delay(10, cancellationToken);
//
//         var newId = _users.Count + 1;
//         var newUser = new UserInfo
//         {
//             Id = newId,
//             Name = request.Name,
//             Email = request.Email
//         };
//
//         _users[newId] = newUser;
//
//         _logger.LogInformation("用户创建成功: ID = {UserId}, Name = {Name}", newId, request.Name);
//         return true;
//     }
// }
//
// /// <summary>
// /// 健康检查服务接口
// /// </summary>
// public interface IHealthHub : IPulseHub
// {
//     Task<HealthStatus> CheckHealthAsync(CancellationToken cancellationToken = default);
//     Task<ServerInfo> GetServerInfoAsync(CancellationToken cancellationToken = default);
// }
//
// /// <summary>
// /// 健康检查服务实现（单例）
// /// </summary>
// public class HealthHub : IHealthHub
// {
//     private readonly DateTime _startTime = DateTime.UtcNow;
//
//     public async Task<HealthStatus> CheckHealthAsync(CancellationToken cancellationToken = default)
//     {
//         await Task.Delay(1, cancellationToken);
//
//         return new HealthStatus
//         {
//             Status = "Healthy",
//             Timestamp = DateTime.UtcNow,
//             Uptime = DateTime.UtcNow - _startTime
//         };
//     }
//
//     public async Task<ServerInfo> GetServerInfoAsync(CancellationToken cancellationToken = default)
//     {
//         await Task.Delay(1, cancellationToken);
//
//         return new ServerInfo
//         {
//             Version = "PulseRPC Server v1.0.0",
//             ProcessorCount = Environment.ProcessorCount,
//             WorkingSet = Environment.WorkingSet,
//             StartTime = _startTime
//         };
//     }
// }
//
// /// <summary>
// /// 示例消息类型定义
// /// </summary>
// [MemoryPack.MemoryPackable]
// public partial class AddRequest
// {
//     [MemoryPack.MemoryPackOrder(0)]
//     public int A { get; set; }
//
//     [MemoryPack.MemoryPackOrder(1)]
//     public int B { get; set; }
// }
//
// [MemoryPack.MemoryPackable]
// public partial class DivideRequest
// {
//     [MemoryPack.MemoryPackOrder(0)]
//     public int Dividend { get; set; }
//
//     [MemoryPack.MemoryPackOrder(1)]
//     public int Divisor { get; set; }
// }
//
// [MemoryPack.MemoryPackable]
// public partial class GetUserRequest
// {
//     [MemoryPack.MemoryPackOrder(0)]
//     public int UserId { get; set; }
// }
//
// [MemoryPack.MemoryPackable]
// public partial class CreateUserRequest
// {
//     [MemoryPack.MemoryPackOrder(0)]
//     public string Name { get; set; } = string.Empty;
//
//     [MemoryPack.MemoryPackOrder(1)]
//     public string Email { get; set; } = string.Empty;
// }
//
// [MemoryPack.MemoryPackable]
// public partial class UserInfo
// {
//     [MemoryPack.MemoryPackOrder(0)]
//     public int Id { get; set; }
//
//     [MemoryPack.MemoryPackOrder(1)]
//     public string Name { get; set; } = string.Empty;
//
//     [MemoryPack.MemoryPackOrder(2)]
//     public string Email { get; set; } = string.Empty;
// }
//
// [MemoryPack.MemoryPackable]
// public partial class HealthStatus
// {
//     [MemoryPack.MemoryPackOrder(0)]
//     public string Status { get; set; } = string.Empty;
//
//     [MemoryPack.MemoryPackOrder(1)]
//     public DateTime Timestamp { get; set; }
//
//     [MemoryPack.MemoryPackOrder(2)]
//     public TimeSpan Uptime { get; set; }
// }
//
// [MemoryPack.MemoryPackable]
// public partial class ServerInfo
// {
//     [MemoryPack.MemoryPackOrder(0)]
//     public string Version { get; set; } = string.Empty;
//
//     [MemoryPack.MemoryPackOrder(1)]
//     public int ProcessorCount { get; set; }
//
//     [MemoryPack.MemoryPackOrder(2)]
//     public long WorkingSet { get; set; }
//
//     [MemoryPack.MemoryPackOrder(3)]
//     public DateTime StartTime { get; set; }
// }
//
// /// <summary>
// /// 传输数据接收事件参数
// /// </summary>
// public class TransportDataReceivedEventArgs : EventArgs
// {
//     public string ConnectionId { get; }
//     public ReadOnlySequence<byte> Data { get; }
//     public DateTime ReceivedTime { get; }
//
//     public TransportDataReceivedEventArgs(string connectionId, ReadOnlySequence<byte> data, DateTime receivedTime)
//     {
//         ConnectionId = connectionId;
//         Data = data;
//         ReceivedTime = receivedTime;
//     }
// }
//
// /// <summary>
// /// 主机服务示例 - 演示完整的服务端生命周期
// /// </summary>
// public class PulseServerHostedService : BackgroundService
// {
//     private readonly HighPerformanceServerExample _server;
//     private readonly ILogger<PulseServerHostedService> _logger;
//
//     public PulseServerHostedService(
//         HighPerformanceServerExample server,
//         ILogger<PulseServerHostedService> logger)
//     {
//         _server = server;
//         _logger = logger;
//     }
//
//     protected override async Task ExecuteAsync(CancellationToken stoppingToken)
//     {
//         try
//         {
//             await _server.StartAsync();
//
//             // 等待取消信号
//             stoppingToken.WaitHandle.WaitOne();
//         }
//         catch (Exception ex)
//         {
//             _logger.LogError(ex, "服务端运行期间发生异常");
//             throw;
//         }
//         finally
//         {
//             await _server.StopAsync();
//         }
//     }
// }
//
// /// <summary>
// /// 依赖注入配置扩展
// /// </summary>
// public static class ServiceCollectionExtensions
// {
//     public static IServiceCollection AddPulseRPCHighPerformanceServer(this IServiceCollection services)
//     {
//         // 注册核心服务
//         services.AddSingleton<HighPerformanceServerExample>();
//         services.AddSingleton<ISerializerProvider, PulseRPCSerializerProvider>();
//
//         // 注册业务服务
//         services.AddScoped<ICalculatorHub, CalculatorHub>();
//         services.AddScoped<IUserHub, UserHub>();
//         services.AddSingleton<IHealthHub, HealthHub>();
//
//         // 注册主机服务
//         services.AddHostedService<PulseServerHostedService>();
//
//         return services;
//     }
// }
