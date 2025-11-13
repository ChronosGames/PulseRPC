# PulseRPC 最佳实践指南

本文档提供使用 PulseRPC 框架的最佳实践建议，帮助您构建高性能、可维护和可扩展的分布式应用程序。

## 📋 目录

- [架构设计](#架构设计)
- [客户端最佳实践](#客户端最佳实践)
- [服务端最佳实践](#服务端最佳实践)
- [序列化优化](#序列化优化)
- [传输层优化](#传输层优化)
- [Unity 集成实践](#unity-集成实践)
- [性能优化](#性能优化)
- [错误处理](#错误处理)
- [监控与调试](#监控与调试)
- [部署实践](#部署实践)

## 架构设计

### 🏗️ 服务边界设计

#### 1. 基于领域的服务划分

```csharp
// ✅ 推荐：按业务领域划分接口
[PulseRpcService]
public interface IChatService
{
    Task<SendMessageResponse> SendMessageAsync(SendMessageRequest request);
    Task<GetMessagesResponse> GetMessagesAsync(GetMessagesRequest request);
    Task<JoinRoomResponse> JoinRoomAsync(JoinRoomRequest request);
}

[PulseRpcService]
public interface IUserService  
{
    Task<GetUserResponse> GetUserAsync(GetUserRequest request);
    Task<UpdateUserResponse> UpdateUserAsync(UpdateUserRequest request);
    Task<GetUserListResponse> GetUserListAsync(GetUserListRequest request);
}

// ❌ 避免：过于宽泛的服务接口
[PulseRpcService]
public interface IBusinessService
{
    Task<object> ProcessAsync(object request); // 太模糊
    Task<dynamic> HandleAsync(dynamic data);   // 类型不安全
}
```

#### 2. 合理的接口粒度

```csharp
// ✅ 推荐：适中的接口粒度
[PulseRpcService]
public interface IChatHub
{
    // 核心聊天功能
    Task<SendMessageResponse> SendMessageAsync(SendMessageRequest request);
    Task<BroadcastMessageResponse> BroadcastMessageAsync(BroadcastMessageRequest request);
    
    // 房间管理
    Task<JoinRoomResponse> JoinRoomAsync(JoinRoomRequest request);
    Task<LeaveRoomResponse> LeaveRoomAsync(LeaveRoomRequest request);
    Task<GetRoomInfoResponse> GetRoomInfoAsync(GetRoomInfoRequest request);
    
    // 用户状态
    Task<GetOnlineUsersResponse> GetOnlineUsersAsync(GetOnlineUsersRequest request);
}

// ❌ 避免：过度细化的接口
[PulseRpcService]
public interface IMessageSender
{
    Task<SendMessageResponse> SendAsync(SendMessageRequest request);
}

[PulseRpcService]
public interface IRoomManager
{
    Task<JoinRoomResponse> JoinAsync(JoinRoomRequest request);
}
```

### 🔗 依赖管理原则

```csharp
// ✅ 推荐：清晰的分层架构
namespace MyApp.Services
{
    // 应用层 - 依赖领域层和基础设施层
    public class ChatApplicationService
    {
        private readonly IChatDomainService _chatDomainService;
        private readonly IUserRepository _userRepository;
        private readonly IMessageRepository _messageRepository;
        
        // 处理应用级逻辑，协调领域服务
    }
    
    // 领域层 - 包含核心业务逻辑
    public class ChatDomainService : IChatDomainService
    {
        // 纯业务逻辑，不依赖外部系统
        public async Task<Message> CreateMessage(User user, string content)
        {
            // 领域规则验证
            if (string.IsNullOrWhiteSpace(content))
                throw new DomainException("消息内容不能为空");
                
            return new Message(user.Id, content, DateTime.UtcNow);
        }
    }
}
```

## 客户端最佳实践

### 🔧 客户端配置

```csharp
// ✅ 推荐：使用 PulseRpcClientFactory 创建客户端
public class ChatClient
{
    private readonly IPulseClient _client;
    private readonly IChatService _chatService;

    public ChatClient()
    {
        // 创建 TCP 客户端
        _client = PulseRpcClientFactory.CreateTcpClient("chat", "localhost", 8000);
        
        // 获取服务代理
        _chatService = _client.GetService<IChatService>();
    }

    public async Task<string> SendMessageAsync(string message)
    {
        try
        {
            var request = new SendMessageRequest { Message = message };
            var response = await _chatService.SendMessageAsync(request);
            return response.Result;
        }
        catch (PulseRpcException ex)
        {
            // 处理 RPC 异常
            Console.WriteLine($"RPC 调用失败: {ex.Message}");
            throw;
        }
    }
}
```

### 🔄 连接管理

```csharp
// ✅ 推荐：实现 IDisposable 模式
public class ChatClientManager : IDisposable
{
    private readonly IPulseClient _client;
    private bool _disposed = false;

    public ChatClientManager(string host, int port)
    {
        _client = PulseRpcClientFactory.CreateTcpClient("chat", host, port);
    }

    public async Task<T> CallServiceAsync<T>(Func<IChatService, Task<T>> serviceCall)
    {
        ThrowIfDisposed();
        
        var service = _client.GetService<IChatService>();
        return await serviceCall(service);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ChatClientManager));
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _client?.Dispose();
            _disposed = true;
        }
    }
}
```

### 📦 批量操作优化

```csharp
// ✅ 推荐：使用批量接口减少网络往返
[PulseRpcService]
public interface IUserService
{
    // 单个操作
    Task<GetUserResponse> GetUserAsync(GetUserRequest request);
    
    // 批量操作 - 减少网络开销
    Task<GetMultipleUsersResponse> GetMultipleUsersAsync(GetMultipleUsersRequest request);
}

public class UserClient
{
    private readonly IUserService _userService;

    public async Task<List<User>> GetUsersAsync(List<int> userIds)
    {
        // ✅ 批量获取而非多次单独调用
        var request = new GetMultipleUsersRequest { UserIds = userIds };
        var response = await _userService.GetMultipleUsersAsync(request);
        return response.Users;
    }
}
```

## 服务端最佳实践

### 🚀 服务实现

```csharp
// ✅ 推荐：良好的服务实现结构
public class ChatService : IChatService
{
    private readonly ILogger<ChatService> _logger;
    private readonly IChatDomainService _domainService;
    private readonly IMessageRepository _messageRepository;
    private readonly IConnectionManager _connectionManager;

    public ChatService(
        ILogger<ChatService> logger,
        IChatDomainService domainService,
        IMessageRepository messageRepository,
        IConnectionManager connectionManager)
    {
        _logger = logger;
        _domainService = domainService;
        _messageRepository = messageRepository;
        _connectionManager = connectionManager;
    }

    public async Task<SendMessageResponse> SendMessageAsync(SendMessageRequest request)
    {
        // 参数验证
        if (request == null)
            throw new ArgumentNullException(nameof(request));
            
        if (string.IsNullOrWhiteSpace(request.Message))
            throw new ArgumentException("消息内容不能为空", nameof(request.Message));

        try
        {
            _logger.LogInformation("处理发送消息请求: {UserId} -> {RoomId}", 
                request.UserId, request.RoomId);

            // 领域逻辑处理
            var message = await _domainService.CreateMessageAsync(
                request.UserId, request.RoomId, request.Message);

            // 持久化
            await _messageRepository.SaveAsync(message);

            // 广播给房间内其他用户
            await _connectionManager.BroadcastToRoomAsync(
                request.RoomId, message, excludeUserId: request.UserId);

            _logger.LogInformation("消息发送成功: {MessageId}", message.Id);

            return new SendMessageResponse
            {
                Success = true,
                MessageId = message.Id,
                Timestamp = message.CreatedAt
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送消息失败: {UserId} -> {RoomId}", 
                request.UserId, request.RoomId);
            throw;
        }
    }
}
```

### 🔒 输入验证和安全

```csharp
// ✅ 推荐：分层验证策略
[MemoryPackable]
public partial class SendMessageRequest
{
    [MemoryPackOrder(0)]
    public int UserId { get; set; }
    
    [MemoryPackOrder(1)]
    public int RoomId { get; set; }
    
    [MemoryPackOrder(2)]
    public string Message { get; set; } = string.Empty;
    
    // 客户端验证
    public bool IsValid(out string errorMessage)
    {
        if (UserId <= 0)
        {
            errorMessage = "用户ID无效";
            return false;
        }
        
        if (RoomId <= 0)
        {
            errorMessage = "房间ID无效";
            return false;
        }
        
        if (string.IsNullOrWhiteSpace(Message))
        {
            errorMessage = "消息内容不能为空";
            return false;
        }
        
        if (Message.Length > 1000)
        {
            errorMessage = "消息内容过长";
            return false;
        }
        
        errorMessage = string.Empty;
        return true;
    }
}

// 服务端验证
public class ChatService : IChatService
{
    public async Task<SendMessageResponse> SendMessageAsync(SendMessageRequest request)
    {
        // 服务端重复验证（防止客户端绕过）
        if (!request.IsValid(out var errorMessage))
        {
            throw new ArgumentException($"请求参数无效: {errorMessage}");
        }
        
        // 权限检查
        if (!await _authorizationService.CanSendMessageAsync(request.UserId, request.RoomId))
        {
            throw new UnauthorizedAccessException("用户无权限在该房间发送消息");
        }
        
        // 内容安全检查
        if (await _contentModerationService.IsInappropriateAsync(request.Message))
        {
            throw new ArgumentException("消息内容不合规");
        }
        
        // 业务逻辑处理...
    }
}
```

## 序列化优化

### 📦 MemoryPack 最佳实践

```csharp
// ✅ 推荐：正确使用 MemoryPack 特性
[MemoryPackable]
public partial class ChatMessage
{
    [MemoryPackOrder(0)]
    public int Id { get; set; }
    
    [MemoryPackOrder(1)]
    public int SenderId { get; set; }
    
    [MemoryPackOrder(2)]
    public int RoomId { get; set; }
    
    [MemoryPackOrder(3)]
    public string Content { get; set; } = string.Empty;
    
    [MemoryPackOrder(4)]
    public DateTime CreatedAt { get; set; }
    
    // MemoryPack 要求无参构造函数
    public ChatMessage() { }
    
    public ChatMessage(int senderId, int roomId, string content)
    {
        SenderId = senderId;
        RoomId = roomId;
        Content = content;
        CreatedAt = DateTime.UtcNow;
    }
}

// ✅ 推荐：使用 Union 处理多态
[MemoryPackable]
[MemoryPackUnion(0, typeof(TextMessage))]
[MemoryPackUnion(1, typeof(ImageMessage))]
[MemoryPackUnion(2, typeof(FileMessage))]
public abstract partial class BaseMessage
{
    [MemoryPackOrder(0)]
    public int Id { get; set; }
    
    [MemoryPackOrder(1)]
    public int SenderId { get; set; }
    
    [MemoryPackOrder(2)]
    public DateTime CreatedAt { get; set; }
}

[MemoryPackable]
public partial class TextMessage : BaseMessage
{
    [MemoryPackOrder(10)]
    public string Content { get; set; } = string.Empty;
}

[MemoryPackable]
public partial class ImageMessage : BaseMessage
{
    [MemoryPackOrder(10)]
    public string ImageUrl { get; set; } = string.Empty;
    
    [MemoryPackOrder(11)]
    public int Width { get; set; }
    
    [MemoryPackOrder(12)]
    public int Height { get; set; }
}
```

### 🚫 避免序列化陷阱

```csharp
// ❌ 避免：循环引用
public class BadUser
{
    public int Id { get; set; }
    public List<BadMessage> Messages { get; set; } = new(); // 可能导致循环引用
}

public class BadMessage
{
    public int Id { get; set; }
    public BadUser Sender { get; set; } = null!; // 循环引用
}

// ✅ 推荐：使用 ID 引用而非对象引用
[MemoryPackable]
public partial class User
{
    [MemoryPackOrder(0)]
    public int Id { get; set; }
    
    [MemoryPackOrder(1)]
    public string Name { get; set; } = string.Empty;
    
    // 不直接包含 Messages 集合，避免循环引用
}

[MemoryPackable]
public partial class Message
{
    [MemoryPackOrder(0)]
    public int Id { get; set; }
    
    [MemoryPackOrder(1)]
    public int SenderId { get; set; } // 使用 ID 而非对象引用
    
    [MemoryPackOrder(2)]
    public string Content { get; set; } = string.Empty;
}
```

## 传输层优化

### 🌐 TCP vs KCP 选择

```csharp
// ✅ TCP 适用场景：可靠性要求高
public class FileTransferClient
{
    public FileTransferClient()
    {
        // 文件传输使用 TCP 确保数据完整性
        _client = PulseRpcClientFactory.CreateTcpClient("file-transfer", "localhost", 8001);
    }
}

// ✅ KCP 适用场景：实时性要求高
public class GameClient
{
    public GameClient()
    {
        // 游戏数据使用 KCP 获得更低延迟
        _client = PulseRpcClientFactory.CreateKcpClient("game", "localhost", 8002);
    }
}

// ✅ 混合使用：不同场景选择不同协议
public class HybridGameClient
{
    private readonly IPulseClient _tcpClient;  // 用于重要数据
    private readonly IPulseClient _kcpClient; // 用于实时数据

    public HybridGameClient()
    {
        _tcpClient = PulseRpcClientFactory.CreateTcpClient("game-reliable", "localhost", 8001);
        _kcpClient = PulseRpcClientFactory.CreateKcpClient("game-realtime", "localhost", 8002);
    }

    public async Task SaveGameAsync(GameSaveData data)
    {
        // 重要数据使用 TCP
        var saveService = _tcpClient.GetService<IGameSaveService>();
        await saveService.SaveAsync(data);
    }

    public async Task SendPlayerPositionAsync(Vector3 position)
    {
        // 实时数据使用 KCP
        var realtimeService = _kcpClient.GetService<IRealtimeService>();
        await realtimeService.UpdatePositionAsync(position);
    }
}
```

### 📊 连接池优化

```csharp
// ✅ 推荐：合理配置连接参数
public class OptimizedClientFactory
{
    public static IPulseClient CreateOptimizedClient(string name, string host, int port)
    {
        return PulseRpcClientFactory.CreateClient(builder =>
        {
            builder.AddTcp(name, host, port, options =>
            {
                // TCP 相关优化
                options.KeepAlive = true;
                options.NoDelay = true;          // 禁用 Nagle 算法，降低延迟
                options.ReceiveBufferSize = 64 * 1024;  // 64KB 接收缓冲区
                options.SendBufferSize = 64 * 1024;     // 64KB 发送缓冲区
                options.ConnectTimeout = TimeSpan.FromSeconds(10);
                options.ReceiveTimeout = TimeSpan.FromSeconds(30);
                options.SendTimeout = TimeSpan.FromSeconds(30);
            });
        });
    }
}
```

## Unity 集成实践

### 🎮 Unity 客户端实现

```csharp
// ✅ Unity 中的 PulseRPC 客户端
public class UnityGameClient : MonoBehaviour
{
    [SerializeField] private string serverHost = "localhost";
    [SerializeField] private int serverPort = 8000;
    
    private IPulseClient _client;
    private IGameService _gameService;
    private bool _isConnected = false;

    async void Start()
    {
        try
        {
            // 创建客户端
            _client = PulseRpcClientFactory.CreateTcpClient("game", serverHost, serverPort);
            _gameService = _client.GetService<IGameService>();
            
            // 测试连接
            await _gameService.PingAsync();
            _isConnected = true;
            
            Debug.Log("成功连接到游戏服务器");
        }
        catch (Exception ex)
        {
            Debug.LogError($"连接游戏服务器失败: {ex.Message}");
        }
    }

    public async Task<bool> LoginAsync(string username, string password)
    {
        if (!_isConnected) return false;

        try
        {
            var request = new LoginRequest { Username = username, Password = password };
            var response = await _gameService.LoginAsync(request);
            return response.Success;
        }
        catch (Exception ex)
        {
            Debug.LogError($"登录失败: {ex.Message}");
            return false;
        }
    }

    void OnDestroy()
    {
        // Unity 中确保资源清理
        _client?.Dispose();
    }
}
```

### 📱 Unity 异步处理

```csharp
// ✅ Unity 中处理异步操作
public class AsyncGameManager : MonoBehaviour
{
    private IGameService _gameService;
    private CancellationTokenSource _cancellationTokenSource;

    void Start()
    {
        _cancellationTokenSource = new CancellationTokenSource();
        // 初始化客户端...
    }

    // 使用 UniTask 或 Unity 的 async/await
    public async Task<GameState> LoadGameStateAsync()
    {
        try
        {
            var request = new GetGameStateRequest { PlayerId = GetPlayerId() };
            var response = await _gameService.GetGameStateAsync(request)
                .ConfigureAwait(false); // 避免死锁
            
            // 切换回主线程更新 UI
            await UnityMainThreadDispatcher.Instance.EnqueueAsync(() =>
            {
                UpdateGameUI(response.GameState);
            });
            
            return response.GameState;
        }
        catch (OperationCanceledException)
        {
            Debug.Log("游戏状态加载被取消");
            return null;
        }
        catch (Exception ex)
        {
            Debug.LogError($"加载游戏状态失败: {ex.Message}");
            return null;
        }
    }

    void OnDestroy()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
    }
}
```

## 性能优化

### ⚡ 并发处理

```csharp
// ✅ 推荐：合理的并发控制
public class HighPerformanceService
{
    private readonly SemaphoreSlim _semaphore;
    private readonly ILogger<HighPerformanceService> _logger;

    public HighPerformanceService(ILogger<HighPerformanceService> logger)
    {
        _logger = logger;
        // 控制并发数量，避免资源耗尽
        _semaphore = new SemaphoreSlim(Environment.ProcessorCount * 2);
    }

    public async Task<ProcessResult> ProcessBatchAsync(List<ProcessRequest> requests)
    {
        var tasks = requests.Select(async request =>
        {
            await _semaphore.WaitAsync();
            try
            {
                return await ProcessSingleRequestAsync(request);
            }
            finally
            {
                _semaphore.Release();
            }
        });

        var results = await Task.WhenAll(tasks);
        return new ProcessResult { Results = results.ToList() };
    }

    private async Task<ProcessSingleResult> ProcessSingleRequestAsync(ProcessRequest request)
    {
        // 单个请求处理逻辑
        await Task.Delay(100); // 模拟处理时间
        return new ProcessSingleResult { Success = true };
    }
}
```

### 📈 缓存策略

```csharp
// ✅ 推荐：多层缓存策略
public class CachedUserService : IUserService
{
    private readonly IUserService _innerService;
    private readonly IMemoryCache _memoryCache;
    private readonly IDistributedCache _distributedCache;
    private readonly ILogger<CachedUserService> _logger;

    public async Task<GetUserResponse> GetUserAsync(GetUserRequest request)
    {
        var cacheKey = $"user:{request.UserId}";

        // 一级缓存：内存缓存（最快）
        if (_memoryCache.TryGetValue(cacheKey, out GetUserResponse cachedResponse))
        {
            _logger.LogDebug("从内存缓存获取用户: {UserId}", request.UserId);
            return cachedResponse;
        }

        // 二级缓存：分布式缓存
        var distributedData = await _distributedCache.GetStringAsync(cacheKey);
        if (!string.IsNullOrEmpty(distributedData))
        {
            var response = JsonSerializer.Deserialize<GetUserResponse>(distributedData);
            
            // 回填内存缓存
            _memoryCache.Set(cacheKey, response, TimeSpan.FromMinutes(5));
            
            _logger.LogDebug("从分布式缓存获取用户: {UserId}", request.UserId);
            return response;
        }

        // 缓存未命中，调用实际服务
        var result = await _innerService.GetUserAsync(request);

        // 写入缓存
        var serializedData = JsonSerializer.Serialize(result);
        await _distributedCache.SetStringAsync(cacheKey, serializedData, 
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
            });

        _memoryCache.Set(cacheKey, result, TimeSpan.FromMinutes(5));

        _logger.LogDebug("从服务获取用户并缓存: {UserId}", request.UserId);
        return result;
    }
}
```

## 错误处理

### 🔄 重试和容错

```csharp
// ✅ 推荐：智能重试机制
public class ResilientServiceClient
{
    private readonly IPulseClient _client;
    private readonly ILogger<ResilientServiceClient> _logger;

    public async Task<T> CallWithRetryAsync<T>(
        Func<Task<T>> operation,
        int maxRetries = 3,
        TimeSpan? baseDelay = null)
    {
        var delay = baseDelay ?? TimeSpan.FromMilliseconds(500);
        var attempt = 0;

        while (true)
        {
            try
            {
                return await operation();
            }
            catch (Exception ex) when (ShouldRetry(ex, attempt, maxRetries))
            {
                attempt++;
                var waitTime = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * Math.Pow(2, attempt - 1));
                
                _logger.LogWarning("操作失败，{Delay}ms 后进行第 {Attempt} 次重试: {Error}", 
                    waitTime.TotalMilliseconds, attempt, ex.Message);
                
                await Task.Delay(waitTime);
            }
        }
    }

    private static bool ShouldRetry(Exception ex, int currentAttempt, int maxRetries)
    {
        if (currentAttempt >= maxRetries) return false;

        // 只对特定异常进行重试
        return ex is SocketException ||
               ex is TimeoutException ||
               ex is TaskCanceledException ||
               (ex is PulseRpcException rpcEx && rpcEx.IsTransient);
    }
}
```

### 🛡️ 全局异常处理

```csharp
// ✅ 推荐：统一异常处理
public class GlobalExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public async Task<ServiceResponse<T>> HandleAsync<T>(Func<Task<T>> operation, string operationName)
    {
        try
        {
            var result = await operation();
            return ServiceResponse<T>.Success(result);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning("参数错误 - {Operation}: {Message}", operationName, ex.Message);
            return ServiceResponse<T>.Failure("INVALID_ARGUMENT", ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning("权限错误 - {Operation}: {Message}", operationName, ex.Message);
            return ServiceResponse<T>.Failure("UNAUTHORIZED", "访问被拒绝");
        }
        catch (TimeoutException ex)
        {
            _logger.LogError("超时错误 - {Operation}: {Message}", operationName, ex.Message);
            return ServiceResponse<T>.Failure("TIMEOUT", "操作超时");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "未处理异常 - {Operation}", operationName);
            return ServiceResponse<T>.Failure("INTERNAL_ERROR", "内部服务器错误");
        }
    }
}

public class ServiceResponse<T>
{
    public bool Success { get; set; }
    public string ErrorCode { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public T? Data { get; set; }

    public static ServiceResponse<T> Success(T data) => new()
    {
        Success = true,
        Data = data
    };

    public static ServiceResponse<T> Failure(string errorCode, string errorMessage) => new()
    {
        Success = false,
        ErrorCode = errorCode,
        ErrorMessage = errorMessage
    };
}
```

## 监控与调试

### 📊 性能监控

```csharp
// ✅ 推荐：集成性能监控
public class MonitoredChatService : IChatService
{
    private readonly IChatService _innerService;
    private readonly IMetricsCollector _metrics;
    private readonly ILogger<MonitoredChatService> _logger;

    public async Task<SendMessageResponse> SendMessageAsync(SendMessageRequest request)
    {
        var stopwatch = Stopwatch.StartNew();
        var labels = new Dictionary<string, string>
        {
            ["method"] = nameof(SendMessageAsync),
            ["room_id"] = request.RoomId.ToString()
        };

        try
        {
            _metrics.IncrementCounter("rpc_requests_total", labels);
            
            var response = await _innerService.SendMessageAsync(request);
            
            _metrics.IncrementCounter("rpc_requests_success_total", labels);
            return response;
        }
        catch (Exception ex)
        {
            labels["error_type"] = ex.GetType().Name;
            _metrics.IncrementCounter("rpc_requests_error_total", labels);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            _metrics.RecordHistogram("rpc_request_duration_seconds", 
                stopwatch.Elapsed.TotalSeconds, labels);
        }
    }
}
```

### 🔍 结构化日志

```csharp
// ✅ 推荐：结构化日志记录
public class ChatService : IChatService
{
    private readonly ILogger<ChatService> _logger;

    public async Task<SendMessageResponse> SendMessageAsync(SendMessageRequest request)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = Guid.NewGuid(),
            ["UserId"] = request.UserId,
            ["RoomId"] = request.RoomId,
            ["Operation"] = "SendMessage"
        });

        _logger.LogInformation("开始处理发送消息请求");

        try
        {
            // 业务逻辑...
            var response = new SendMessageResponse { Success = true };

            _logger.LogInformation("消息发送成功, MessageId: {MessageId}", response.MessageId);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "消息发送失败");
            throw;
        }
    }
}
```

## 部署实践

### 🐳 容器化部署

```dockerfile
# ✅ 优化的 Dockerfile for .NET 10
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

# 创建非 root 用户
RUN addgroup --system --gid 1001 pulserpc
RUN adduser --system --uid 1001 --ingroup pulserpc pulserpc

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# 先复制项目文件，利用 Docker 缓存
COPY ["Directory.Packages.props", "./"]
COPY ["Directory.Build.props", "./"]
COPY ["src/MyPulseRPCService/MyPulseRPCService.csproj", "src/MyPulseRPCService/"]
COPY ["src/PulseRPC.Abstractions/PulseRPC.Abstractions.csproj", "src/PulseRPC.Abstractions/"]
COPY ["src/PulseRPC.Server/PulseRPC.Server.csproj", "src/PulseRPC.Server/"]

RUN dotnet restore "src/MyPulseRPCService/MyPulseRPCService.csproj"

# 复制源代码并构建
COPY . .
WORKDIR "/src/src/MyPulseRPCService"
RUN dotnet build "MyPulseRPCService.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "MyPulseRPCService.csproj" -c Release -o /app/publish \
    --no-restore --no-build

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# 安全配置
USER pulserpc

# 健康检查
HEALTHCHECK --interval=30s --timeout=3s --start-period=30s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "MyPulseRPCService.dll"]
```

### ☸️ Kubernetes 配置

```yaml
# ✅ 生产级 Kubernetes 部署
apiVersion: apps/v1
kind: Deployment
metadata:
  name: pulserpc-service
  labels:
    app: pulserpc-service
    version: v1.0.0
spec:
  replicas: 3
  strategy:
    type: RollingUpdate
    rollingUpdate:
      maxUnavailable: 1
      maxSurge: 1
  selector:
    matchLabels:
      app: pulserpc-service
  template:
    metadata:
      labels:
        app: pulserpc-service
        version: v1.0.0
    spec:
      containers:
      - name: pulserpc-service
        image: pulserpc-service:v1.0.0
        ports:
        - containerPort: 8080
          name: rpc
        env:
        - name: ASPNETCORE_ENVIRONMENT
          value: "Production"
        - name: PULSERPC_SERVER_HOST
          value: "0.0.0.0"
        - name: PULSERPC_SERVER_PORT
          value: "8080"
        resources:
          requests:
            memory: "512Mi"
            cpu: "500m"
          limits:
            memory: "1Gi"
            cpu: "1000m"
        livenessProbe:
          httpGet:
            path: /health
            port: 8080
          initialDelaySeconds: 30
          periodSeconds: 10
          timeoutSeconds: 5
          failureThreshold: 3
        readinessProbe:
          httpGet:
            path: /ready
            port: 8080
          initialDelaySeconds: 10
          periodSeconds: 5
          timeoutSeconds: 3
          failureThreshold: 3
        securityContext:
          allowPrivilegeEscalation: false
          runAsNonRoot: true
          runAsUser: 1001
          capabilities:
            drop:
            - ALL
---
apiVersion: v1
kind: Service
metadata:
  name: pulserpc-service
spec:
  selector:
    app: pulserpc-service
  ports:
  - name: rpc
    port: 8080
    targetPort: 8080
  type: ClusterIP
---
apiVersion: autoscaling/v2
kind: HorizontalPodAutoscaler
metadata:
  name: pulserpc-service-hpa
spec:
  scaleTargetRef:
    apiVersion: apps/v1
    kind: Deployment
    name: pulserpc-service
  minReplicas: 3
  maxReplicas: 20
  metrics:
  - type: Resource
    resource:
      name: cpu
      target:
        type: Utilization
        averageUtilization: 70
  - type: Resource
    resource:
      name: memory
      target:
        type: Utilization
        averageUtilization: 80
```

遵循这些最佳实践将帮助您构建健壮、高性能和可维护的 PulseRPC 应用程序。随着框架的发展，请持续关注最新的实践建议和性能优化技巧。
