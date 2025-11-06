# PulseRPC.Server 最佳实践指南

## 目标读者

本文档面向使用 PulseRPC.Server 开发分布式服务的开发者，提供生产环境下的最佳实践和避坑指南。

## 快速检查清单

在开始开发前，确保理解以下核心概念：

- [ ] IPulseService 是**有状态**的服务实例（类似 Actor）
- [ ] IPulseHub 是**无状态**的通信契约（类似 gRPC Service）
- [ ] IPulseServiceFactory 管理 Service 实例的生命周期
- [ ] 同一 ServiceId 的请求在同一线程执行（无需加锁）
- [ ] 多个 Hub 可以共享同一个 Service 实例

---

## 1. ServiceId 命名规范

### ✅ 推荐做法

#### 规则：`{ServiceName}:{BusinessId}`

```csharp
// ✅ 正确：每个聊天室有独立的 ServiceId
public class ChatRoomService : IPulseService
{
    public string ServiceName => "ChatRoom";
    public string ServiceId { get; }

    public ChatRoomService(string roomId)
    {
        ServiceId = $"ChatRoom:{roomId}";  // "ChatRoom:room-123"
    }
}

// ✅ 正确：每个游戏房间有独立的 ServiceId
public class GameRoomService : IPulseService
{
    public string ServiceName => "GameRoom";
    public string ServiceId { get; }

    public GameRoomService(string gameId)
    {
        ServiceId = $"GameRoom:{gameId}";  // "GameRoom:game-456"
    }
}

// ✅ 正确：每个用户有独立的购物车
public class ShoppingCartService : IPulseService
{
    public string ServiceName => "ShoppingCart";
    public string ServiceId { get; }

    public ShoppingCartService(string userId)
    {
        ServiceId = $"ShoppingCart:{userId}";  // "ShoppingCart:user-789"
    }
}
```

### ❌ 常见错误

```csharp
// ❌ 错误：所有实例使用相同的 ServiceId
public class ChatRoomService : IPulseService
{
    public string ServiceName => "ChatRoom";
    public string ServiceId => "ChatRoom";  // 所有房间共享同一个实例！

    // 问题：所有房间的消息会混在一起
}

// ❌ 错误：ServiceId 和 ServiceName 相同
public class GameRoomService : IPulseService
{
    public string ServiceName => ServiceId;  // 循环引用！
    public string ServiceId => ServiceName;
}

// ❌ 错误：使用空格或特殊字符
public class OrderService : IPulseService
{
    public string ServiceId => "Order Service: 123";  // 避免空格
    // 应该使用：Order:123 或 Order-Service:123
}
```

### 📌 命名建议

| 业务场景 | ServiceName | ServiceId 格式 | 示例 |
|---------|-------------|----------------|------|
| 聊天室 | ChatRoom | ChatRoom:{roomId} | ChatRoom:room-123 |
| 游戏房间 | GameRoom | GameRoom:{gameId} | GameRoom:game-456 |
| 用户会话 | UserSession | UserSession:{userId} | UserSession:user-789 |
| 订单处理 | OrderProcessor | OrderProcessor:{orderId} | OrderProcessor:order-001 |
| 库存管理 | Inventory | Inventory:{warehouseId} | Inventory:warehouse-A |

---

## 2. Service 状态管理

### ✅ 推荐做法

#### 所有可变状态放在 Service 中

```csharp
public class ChatRoomService : IPulseService
{
    // ✅ 正确：实例状态（每个房间独立）
    private readonly List<Message> _messages = new();
    private readonly HashSet<string> _participants = new();
    private readonly HashSet<string> _bannedUsers = new();
    private DateTimeOffset _createdAt;
    private DateTimeOffset _lastActivityAt;

    public ChatRoomService(string roomId, ILogger<ChatRoomService> logger)
    {
        ServiceId = $"ChatRoom:{roomId}";
        _createdAt = DateTimeOffset.UtcNow;
        _lastActivityAt = _createdAt;
    }

    public void AddMessage(Message msg)
    {
        // ✅ 线程安全：同一 ServiceId 的请求在同一线程执行
        _messages.Add(msg);
        _lastActivityAt = DateTimeOffset.UtcNow;
    }

    public IReadOnlyList<Message> GetMessages() => _messages;
}
```

### ❌ 常见错误

```csharp
public class ChatRoomService : IPulseService
{
    // ❌ 错误：静态字段（所有实例共享）
    private static List<Message> _messages = new();
    // 问题：所有房间共享同一个消息列表！

    // ❌ 错误：单例字段（跨实例共享）
    private static readonly Dictionary<string, List<Message>> _roomMessages = new();
    // 问题：应该让每个实例管理自己的状态，而不是在静态字典中

    // ❌ 错误：不必要的锁
    private readonly object _lock = new();
    private List<Message> _messages = new();

    public void AddMessage(Message msg)
    {
        lock (_lock)  // ❌ 不必要！线程亲和性已保证
        {
            _messages.Add(msg);
        }
    }
}
```

### 📌 状态管理原则

1. **实例状态优于静态状态**
   - ✅ `private List<Message> _messages`
   - ❌ `static List<Message> _messages`

2. **无需手动加锁**
   - ThreadAffinityManager 保证同一 ServiceId 的请求在同一线程执行
   - 只有跨 Service 通信才需要考虑并发

3. **状态隔离**
   - 每个 Service 实例管理自己的状态
   - 不同实例的状态完全隔离

---

## 3. Hub 设计模式

### ✅ 推荐做法

#### 模式 1：Hub 作为 Service 的门面

```csharp
// Service：状态容器
public class ChatRoomService : IPulseService
{
    public string ServiceName => "ChatRoom";
    public string ServiceId { get; }

    private readonly List<Message> _messages = new();

    public ChatRoomService(string roomId)
    {
        ServiceId = $"ChatRoom:{roomId}";
    }

    // 内部方法（供 Hub 调用）
    public void AddMessage(Message msg) => _messages.Add(msg);
    public IReadOnlyList<Message> GetMessages() => _messages;
}

// Hub：无状态门面
public class ChatRoomHub : IPulseHub
{
    private readonly IPulseServiceFactory<ChatRoomService> _factory;

    public ChatRoomHub(IPulseServiceFactory<ChatRoomService> factory)
    {
        _factory = factory;
    }

    public async Task<Message[]> GetMessagesAsync(string roomId)
    {
        var service = await _factory.GetOrCreateAsync($"ChatRoom:{roomId}");
        return service.GetMessages().ToArray();
    }

    public async Task SendMessageAsync(string roomId, string text, string userId)
    {
        var service = await _factory.GetOrCreateAsync($"ChatRoom:{roomId}");
        var msg = new Message { Text = text, UserId = userId };
        service.AddMessage(msg);
    }
}
```

#### 模式 2：多个 Hub 共享 Service（权限分离）

```csharp
// Service：状态容器
public class ChatRoomService : IPulseService
{
    private readonly List<Message> _messages = new();
    private readonly HashSet<string> _bannedUsers = new();

    public void AddMessage(Message msg)
    {
        if (_bannedUsers.Contains(msg.UserId))
            throw new InvalidOperationException("User is banned");
        _messages.Add(msg);
    }

    public void BanUser(string userId) => _bannedUsers.Add(userId);
    public IReadOnlyList<Message> GetMessages() => _messages;
}

// 用户 Hub：普通权限
public class ChatRoomUserHub : IPulseHub
{
    private readonly IPulseServiceFactory<ChatRoomService> _factory;

    public async Task SendMessageAsync(string roomId, string text, string userId)
    {
        var service = await _factory.GetOrCreateAsync($"ChatRoom:{roomId}");
        service.AddMessage(new Message { Text = text, UserId = userId });
    }
}

// 管理员 Hub：高级权限
public class ChatRoomAdminHub : IPulseHub
{
    private readonly IPulseServiceFactory<ChatRoomService> _factory;

    public async Task BanUserAsync(string roomId, string userId)
    {
        var service = await _factory.GetOrCreateAsync($"ChatRoom:{roomId}");
        service.BanUser(userId);  // 只有管理员可以封禁用户
    }

    public async Task GetAllMessagesAsync(string roomId)
    {
        var service = await _factory.GetOrCreateAsync($"ChatRoom:{roomId}");
        return service.GetMessages(); // 管理员可以看到所有消息
    }
}
```

### ❌ 常见错误

```csharp
// ❌ 错误：Hub 存储状态
public class ChatRoomHub : IPulseHub
{
    private readonly IPulseServiceFactory<ChatRoomService> _factory;
    private ChatRoomService? _cachedService;  // ❌ 不要缓存！

    public async Task SendMessageAsync(string roomId, string text)
    {
        // ❌ 错误：缓存 Service 实例
        _cachedService ??= await _factory.GetOrCreateAsync($"ChatRoom:{roomId}");
        _cachedService.AddMessage(new Message { Text = text });
        // 问题：Hub 是单例，不同 roomId 会使用同一个缓存实例！
    }
}

// ❌ 错误：Hub 直接实现业务逻辑
public class ChatRoomHub : IPulseHub
{
    private readonly IPulseServiceFactory<ChatRoomService> _factory;
    private readonly IChatRepository _repository;

    public async Task SendMessageAsync(string roomId, string text)
    {
        // ❌ 错误：业务逻辑应该在 Service 中
        var service = await _factory.GetOrCreateAsync($"ChatRoom:{roomId}");
        var msg = new Message { Text = text };

        // ❌ 这些逻辑应该在 Service 中
        if (msg.Text.Length > 1000)
            throw new InvalidOperationException("Message too long");

        service.AddMessage(msg);

        // ❌ 持久化逻辑也应该在 Service 的生命周期钩子中
        await _repository.SaveAsync(msg);
    }
}
```

### 📌 Hub 设计原则

1. **Hub 是无状态的**
   - 不存储任何业务状态
   - 不缓存 Service 实例
   - 只负责参数验证和调用 Service

2. **Hub 是薄的**
   - 业务逻辑在 Service 中
   - Hub 只做简单的转发

3. **Hub 可以多个**
   - 根据权限分离（User/Admin）
   - 根据功能分离（Query/Command）
   - 根据客户端类型分离（Web/Mobile/Desktop）

---

## 4. 生命周期钩子

### ✅ 推荐做法

#### OnActivateAsync：加载状态

```csharp
public class ChatRoomService : IPulseService, IServiceLifecycle
{
    private readonly IChatRepository _repository;
    private readonly ILogger _logger;
    private List<Message> _messages = new();

    public async Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Activating {ServiceId}", ServiceId);

        try
        {
            // ✅ 从数据库加载历史消息
            var messages = await _repository.LoadMessagesAsync(ServiceId, cancellationToken);
            _messages = messages.ToList();

            _logger.LogInformation("Loaded {Count} messages", _messages.Count);
        }
        catch (Exception ex)
        {
            // ✅ 提供降级处理
            _logger.LogError(ex, "Failed to load messages, starting with empty state");
            _messages = new List<Message>();
            // 不抛出异常，允许实例继续创建
        }
    }
}
```

#### OnDeactivateAsync：保存状态

```csharp
public async Task OnDeactivateAsync(CancellationToken cancellationToken = default)
{
    _logger.LogInformation("Deactivating {ServiceId}", ServiceId);

    try
    {
        // ✅ 保存状态到数据库
        await _repository.SaveMessagesAsync(ServiceId, _messages, cancellationToken);

        _logger.LogInformation("Saved {Count} messages", _messages.Count);
    }
    catch (Exception ex)
    {
        // ✅ 记录错误但不抛出异常
        _logger.LogError(ex, "Failed to save messages");
        // 不抛出异常，允许实例正常销毁
    }
}
```

#### OnHealthCheckAsync：健康检查

```csharp
public Task<bool> OnHealthCheckAsync(CancellationToken cancellationToken = default)
{
    try
    {
        // ✅ 检查多个维度
        var memoryOk = _messages.Count < 10000;
        var stateValid = ValidateInternalState();
        var connectionOk = _repository.IsConnected;

        var isHealthy = memoryOk && stateValid && connectionOk;

        if (!isHealthy)
        {
            _logger.LogWarning(
                "Health check failed: Memory={MemoryOk}, State={StateValid}, Connection={ConnectionOk}",
                memoryOk, stateValid, connectionOk);
        }

        return Task.FromResult(isHealthy);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Health check exception");
        return Task.FromResult(false);  // ✅ 异常视为不健康
    }
}

private bool ValidateInternalState()
{
    // ✅ 快速检查，避免耗时操作
    return _messages.All(m => !string.IsNullOrEmpty(m.Id));
}
```

### ❌ 常见错误

```csharp
// ❌ 错误：OnActivateAsync 抛出异常
public async Task OnActivateAsync(CancellationToken cancellationToken = default)
{
    // ❌ 不处理异常，会导致实例创建失败
    var messages = await _repository.LoadMessagesAsync(ServiceId, cancellationToken);
    _messages = messages.ToList();
    // 如果数据库连接失败，整个实例创建会失败
}

// ❌ 错误：OnDeactivateAsync 执行耗时操作
public async Task OnDeactivateAsync(CancellationToken cancellationToken = default)
{
    // ❌ 不要执行耗时操作
    await Task.Delay(TimeSpan.FromSeconds(10)); // 阻塞清理线程！

    // ❌ 不要进行复杂的计算
    var summary = GenerateComplexSummary(); // 耗时计算

    await _repository.SaveAsync(summary);
}

// ❌ 错误：OnHealthCheckAsync 执行耗时操作
public async Task<bool> OnHealthCheckAsync(CancellationToken cancellationToken = default)
{
    // ❌ 不要执行数据库查询
    var count = await _repository.GetMessageCountAsync(ServiceId);
    return count < 10000;

    // ❌ 不要执行网络请求
    var response = await _httpClient.GetAsync("http://health-check-api");
    return response.IsSuccessStatusCode;
}
```

### 📌 生命周期最佳实践

1. **OnActivateAsync**
   - ✅ 加载持久化状态
   - ✅ 建立外部连接
   - ✅ 提供降级处理（不抛出异常）
   - ❌ 不执行耗时操作（<1 秒）

2. **OnDeactivateAsync**
   - ✅ 保存状态
   - ✅ 释放资源
   - ✅ 记录日志
   - ❌ 不抛出异常
   - ❌ 不执行耗时操作（<1 秒）

3. **OnHealthCheckAsync**
   - ✅ 快速检查（<100ms）
   - ✅ 检查多个维度
   - ✅ 返回 bool（不抛出异常）
   - ❌ 不执行 IO 操作
   - ❌ 不执行复杂计算

---

## 5. DI 注册模式

### ✅ 推荐做法

#### 模式 1：使用自定义工厂函数

```csharp
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddChatRoomServices(this IServiceCollection services)
    {
        // 1. 注册依赖
        services.AddSingleton<IChatRepository, ChatRepository>();

        // 2. 注册 ServiceFactory
        services.AddPulseServiceFactory<ChatRoomService>(
            serviceFactory: (sp, serviceId) =>
            {
                // 从 ServiceId 中提取 roomId
                var roomId = serviceId.Split(':')[1];

                // 使用 DI 容器创建实例
                return new ChatRoomService(
                    roomId,
                    sp.GetRequiredService<ILogger<ChatRoomService>>(),
                    sp.GetRequiredService<IChatRepository>());
            },
            configureOptions: options =>
            {
                options.IdleTimeout = TimeSpan.FromMinutes(10);
                options.MaxCachedInstances = 5000;
            });

        // 3. 注册 Hub
        services.AddSingleton<ChatRoomUserHub>();
        services.AddSingleton<ChatRoomAdminHub>();

        return services;
    }
}

// 使用
services.AddChatRoomServices();
```

#### 模式 2：使用 ActivatorUtilities

```csharp
// Service 构造函数：第一个参数必须是 string serviceId
public class ChatRoomService : IPulseService
{
    public ChatRoomService(
        string roomId,  // 第一个参数
        ILogger<ChatRoomService> logger,
        IChatRepository repository)
    {
        ServiceId = $"ChatRoom:{roomId}";
    }
}

// 注册
services.AddChatRoomServices();
services.AddPulseServiceFactory<ChatRoomService>(
    configureOptions: options =>
    {
        options.IdleTimeout = TimeSpan.FromMinutes(10);
    });
// ActivatorUtilities 会自动注入其他依赖
```

#### 模式 3：条件注册

```csharp
public static IServiceCollection AddChatRoomServices(
    this IServiceCollection services,
    IConfiguration configuration)
{
    // 根据配置决定注册方式
    var useRedis = configuration.GetValue<bool>("ChatRoom:UseRedis");

    if (useRedis)
    {
        services.AddSingleton<IChatRepository, RedisChatRepository>();
    }
    else
    {
        services.AddSingleton<IChatRepository, InMemoryChatRepository>();
    }

    services.AddPulseServiceFactory<ChatRoomService>((sp, serviceId) =>
    {
        var roomId = serviceId.Split(':')[1];
        var repository = sp.GetRequiredService<IChatRepository>();
        var logger = sp.GetRequiredService<ILogger<ChatRoomService>>();

        return new ChatRoomService(roomId, logger, repository);
    });

    return services;
}
```

### ❌ 常见错误

```csharp
// ❌ 错误：直接注册 Service（不使用 Factory）
services.AddSingleton<ChatRoomService>();
// 问题：无法管理生命周期，无法支持多实例

// ❌ 错误：在 Factory 中创建新的 ServiceProvider
services.AddPulseServiceFactory<ChatRoomService>(
    (sp, serviceId) =>
    {
        // ❌ 不要创建新的 ServiceProvider
        using var scope = sp.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger>();
        // 问题：生命周期管理混乱
    });

// ❌ 错误：在 Factory 中使用静态依赖
services.AddPulseServiceFactory<ChatRoomService>(
    (sp, serviceId) =>
    {
        var roomId = serviceId.Split(':')[1];
        // ❌ 不要使用静态依赖
        return new ChatRoomService(roomId, GlobalLogger.Instance);
        // 问题：无法测试，无法替换实现
    });
```

### 📌 DI 注册原则

1. **使用扩展方法封装注册逻辑**
   - ✅ `services.AddChatRoomServices()`
   - ❌ 在 Startup 中直接注册大量服务

2. **通过 DI 容器注入依赖**
   - ✅ `sp.GetRequiredService<ILogger>()`
   - ❌ `new Logger()` 或 `GlobalLogger.Instance`

3. **配置外部化**
   - ✅ `configuration.GetValue<int>("ChatRoom:MaxInstances")`
   - ❌ 硬编码配置值

---

## 6. 错误处理

### ✅ 推荐做法

#### Hub 层：参数验证

```csharp
public class ChatRoomHub : IPulseHub
{
    public async Task SendMessageAsync(string roomId, string text, string userId)
    {
        // ✅ 参数验证
        if (string.IsNullOrWhiteSpace(roomId))
            throw new ArgumentException("RoomId is required", nameof(roomId));

        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text is required", nameof(text));

        if (text.Length > 1000)
            throw new ArgumentException("Text too long", nameof(text));

        // 调用 Service
        var service = await _factory.GetOrCreateAsync($"ChatRoom:{roomId}");
        service.AddMessage(new Message { Text = text, UserId = userId });
    }
}
```

#### Service 层：业务异常

```csharp
public class ChatRoomService : IPulseService
{
    public void AddMessage(Message msg)
    {
        // ✅ 业务规则验证
        if (_bannedUsers.Contains(msg.UserId))
        {
            throw new BusinessException(
                ErrorCode.UserBanned,
                $"User {msg.UserId} is banned from this room");
        }

        if (_messages.Count >= MaxMessages)
        {
            throw new BusinessException(
                ErrorCode.RoomFull,
                "Room has reached maximum message count");
        }

        _messages.Add(msg);
    }
}

// 自定义业务异常
public class BusinessException : Exception
{
    public string ErrorCode { get; }

    public BusinessException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }
}
```

#### 异常传播

```csharp
public class ChatRoomHub : IPulseHub
{
    private readonly ILogger _logger;

    public async Task<Result<Message>> SendMessageAsync(string roomId, string text, string userId)
    {
        try
        {
            var service = await _factory.GetOrCreateAsync($"ChatRoom:{roomId}");
            var msg = new Message { Text = text, UserId = userId };
            service.AddMessage(msg);

            return Result<Message>.Success(msg);
        }
        catch (BusinessException ex)
        {
            // ✅ 记录业务异常
            _logger.LogWarning(ex, "Business error: {ErrorCode}", ex.ErrorCode);
            return Result<Message>.Failure(ex.ErrorCode, ex.Message);
        }
        catch (Exception ex)
        {
            // ✅ 记录系统异常
            _logger.LogError(ex, "Unexpected error while sending message");
            return Result<Message>.Failure("INTERNAL_ERROR", "An unexpected error occurred");
        }
    }
}

// Result 类型
public class Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public string? ErrorCode { get; }
    public string? ErrorMessage { get; }

    public static Result<T> Success(T value) => new(true, value, null, null);
    public static Result<T> Failure(string errorCode, string errorMessage) =>
        new(false, default, errorCode, errorMessage);
}
```

### ❌ 常见错误

```csharp
// ❌ 错误：吞掉异常
public async Task SendMessageAsync(string roomId, string text)
{
    try
    {
        var service = await _factory.GetOrCreateAsync($"ChatRoom:{roomId}");
        service.AddMessage(new Message { Text = text });
    }
    catch
    {
        // ❌ 不记录日志，不抛出异常
        // 问题：错误被隐藏，难以调试
    }
}

// ❌ 错误：不区分异常类型
public async Task SendMessageAsync(string roomId, string text)
{
    try
    {
        var service = await _factory.GetOrCreateAsync($"ChatRoom:{roomId}");
        service.AddMessage(new Message { Text = text });
    }
    catch (Exception ex)
    {
        // ❌ 所有异常都返回相同的错误
        throw new Exception("Failed to send message");
        // 问题：丢失了原始异常信息
    }
}

// ❌ 错误：在 Service 中捕获异常但不处理
public void AddMessage(Message msg)
{
    try
    {
        _messages.Add(msg);
    }
    catch (Exception ex)
    {
        // ❌ 捕获但不处理
        Console.WriteLine(ex.Message);
        // 问题：异常没有传播到上层
    }
}
```

### 📌 错误处理原则

1. **分层处理**
   - Hub 层：参数验证
   - Service 层：业务逻辑验证
   - 生命周期钩子：降级处理

2. **日志记录**
   - 业务异常：Warning 级别
   - 系统异常：Error 级别
   - 包含上下文信息（ServiceId, UserId 等）

3. **异常传播**
   - 不吞掉异常
   - 转换为领域异常（BusinessException）
   - 使用 Result 类型避免异常

---

## 7. 性能优化

### ✅ 推荐做法

#### 减少 Factory 调用

```csharp
// ✅ 正确：一次获取 Service，多次使用
public async Task ProcessBatchAsync(string roomId, List<Message> messages)
{
    var service = await _factory.GetOrCreateAsync($"ChatRoom:{roomId}");

    foreach (var msg in messages)
    {
        service.AddMessage(msg);  // 复用同一个实例
    }
}

// ❌ 错误：每次都调用 Factory
public async Task ProcessBatchAsync(string roomId, List<Message> messages)
{
    foreach (var msg in messages)
    {
        // ❌ 每次都创建实例（虽然有缓存，但有开销）
        var service = await _factory.GetOrCreateAsync($"ChatRoom:{roomId}");
        service.AddMessage(msg);
    }
}
```

#### 批量操作

```csharp
// ✅ Service 提供批量接口
public class ChatRoomService : IPulseService
{
    public void AddMessages(IEnumerable<Message> messages)
    {
        _messages.AddRange(messages);  // 批量添加
    }
}

// ✅ Hub 使用批量接口
public async Task SendMessagesAsync(string roomId, List<Message> messages)
{
    var service = await _factory.GetOrCreateAsync($"ChatRoom:{roomId}");
    service.AddMessages(messages);  // 一次调用
}
```

#### 异步操作

```csharp
// ✅ 并行处理多个房间
public async Task BroadcastAsync(List<string> roomIds, Message msg)
{
    var tasks = roomIds.Select(async roomId =>
    {
        var service = await _factory.GetOrCreateAsync($"ChatRoom:{roomId}");
        service.AddMessage(msg);
    });

    await Task.WhenAll(tasks);  // 并行执行
}
```

### ❌ 常见性能问题

```csharp
// ❌ 错误：同步阻塞
public void SendMessage(string roomId, string text)
{
    // ❌ 使用 .Result 阻塞
    var service = _factory.GetOrCreateAsync($"ChatRoom:{roomId}").Result;
    service.AddMessage(new Message { Text = text });
    // 问题：阻塞线程，降低并发性能
}

// ❌ 错误：在循环中执行 IO
public async Task LoadHistoryAsync(string roomId)
{
    var service = await _factory.GetOrCreateAsync($"ChatRoom:{roomId}");

    foreach (var messageId in messageIds)
    {
        // ❌ 每次都查询数据库
        var msg = await _repository.GetMessageAsync(messageId);
        service.AddMessage(msg);
    }
    // 应该：批量查询
}

// ❌ 错误：过度使用 ConfigureAwait(false)
public async Task SendMessageAsync(string roomId, string text)
{
    var service = await _factory
        .GetOrCreateAsync($"ChatRoom:{roomId}")
        .ConfigureAwait(false);  // ❌ 不必要

    // 问题：PulseRPC 内部已经处理了上下文切换
}
```

### 📌 性能优化建议

1. **复用 Service 实例**
   - 一次获取，多次使用
   - 避免重复调用 Factory

2. **批量操作**
   - 提供批量接口
   - 减少方法调用次数

3. **异步并发**
   - 使用 async/await
   - 使用 Task.WhenAll 并行处理

4. **配置调优**
   - 根据负载调整 WorkerThreadCount
   - 根据内存调整 MaxCachedInstances

---

## 8. 测试策略

### ✅ 推荐做法

#### 单元测试 Service

```csharp
public class ChatRoomServiceTests
{
    [Fact]
    public void AddMessage_Should_AddToList_When_UserNotBanned()
    {
        // Arrange
        var service = new ChatRoomService(
            roomId: "test-room",
            logger: NullLogger<ChatRoomService>.Instance,
            repository: Mock.Of<IChatRepository>());

        var msg = new Message { UserId = "user1", Text = "Hello" };

        // Act
        service.AddMessage(msg);

        // Assert
        service.GetMessages().Should().Contain(msg);
    }

    [Fact]
    public void AddMessage_Should_ThrowException_When_UserBanned()
    {
        // Arrange
        var service = new ChatRoomService("test-room", null, null);
        service.BanUser("user1");

        var msg = new Message { UserId = "user1", Text = "Hello" };

        // Act & Assert
        Assert.Throws<BusinessException>(() => service.AddMessage(msg));
    }
}
```

#### 单元测试 Hub

```csharp
public class ChatRoomHubTests
{
    [Fact]
    public async Task SendMessage_Should_CallService_When_Valid()
    {
        // Arrange
        var mockFactory = new Mock<IPulseServiceFactory<ChatRoomService>>();
        var mockService = new Mock<ChatRoomService>("test-room", null, null);

        mockFactory
            .Setup(f => f.GetOrCreateAsync("ChatRoom:room1", default))
            .ReturnsAsync(mockService.Object);

        var hub = new ChatRoomHub(mockFactory.Object);

        // Act
        await hub.SendMessageAsync("room1", "Hello", "user1");

        // Assert
        mockService.Verify(s => s.AddMessage(It.Is<Message>(m => m.Text == "Hello")), Times.Once);
    }
}
```

#### 集成测试

```csharp
public class ChatRoomIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    [Fact]
    public async Task MultipleHubs_Should_ShareState()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var userHub = scope.ServiceProvider.GetRequiredService<ChatRoomUserHub>();
        var adminHub = scope.ServiceProvider.GetRequiredService<ChatRoomAdminHub>();

        // Act
        await userHub.SendMessageAsync("room1", "Hello", "user1");
        var messages = await adminHub.GetAllMessagesAsync("room1");

        // Assert
        messages.Should().HaveCount(1);
        messages[0].Text.Should().Be("Hello");
    }
}
```

### 📌 测试原则

1. **单元测试 Service**
   - 测试业务逻辑
   - 使用 Mock 依赖

2. **单元测试 Hub**
   - 测试参数验证
   - Mock Factory 和 Service

3. **集成测试**
   - 测试完整流程
   - 测试多个 Hub 共享状态

---

## 9. 常见场景

### 场景 1：聊天室

```csharp
// Service
public class ChatRoomService : IPulseService, IServiceLifecycle
{
    public string ServiceName => "ChatRoom";
    public string ServiceId { get; }

    private readonly List<Message> _messages = new();
    private readonly HashSet<string> _participants = new();

    public ChatRoomService(string roomId)
    {
        ServiceId = $"ChatRoom:{roomId}";
    }

    public void Join(string userId) => _participants.Add(userId);
    public void Leave(string userId) => _participants.Remove(userId);
    public void AddMessage(Message msg) => _messages.Add(msg);
    public IReadOnlyList<Message> GetMessages() => _messages;
}

// Hub
public class ChatRoomHub : IPulseHub
{
    public async Task JoinAsync(string roomId, string userId)
    {
        var service = await _factory.GetOrCreateAsync($"ChatRoom:{roomId}");
        service.Join(userId);
    }

    public async Task SendMessageAsync(string roomId, string text, string userId)
    {
        var service = await _factory.GetOrCreateAsync($"ChatRoom:{roomId}");
        service.AddMessage(new Message { Text = text, UserId = userId });
    }
}
```

### 场景 2：游戏房间

```csharp
// Service
public class GameRoomService : IPulseService
{
    public string ServiceName => "GameRoom";
    public string ServiceId { get; }

    private readonly List<Player> _players = new();
    private GamePhase _phase = GamePhase.Waiting;

    public void AddPlayer(Player player)
    {
        if (_phase != GamePhase.Waiting)
            throw new BusinessException("GAME_STARTED", "Game already started");

        _players.Add(player);
    }

    public void StartGame()
    {
        if (_players.Count < 2)
            throw new BusinessException("NOT_ENOUGH_PLAYERS", "Need at least 2 players");

        _phase = GamePhase.Playing;
    }
}

// Hub
public class GameRoomHub : IPulseHub
{
    public async Task JoinGameAsync(string gameId, Player player)
    {
        var service = await _factory.GetOrCreateAsync($"GameRoom:{gameId}");
        service.AddPlayer(player);
    }

    public async Task StartGameAsync(string gameId)
    {
        var service = await _factory.GetOrCreateAsync($"GameRoom:{gameId}");
        service.StartGame();
    }
}
```

### 场景 3：购物车

```csharp
// Service
public class ShoppingCartService : IPulseService, IServiceLifecycle
{
    public string ServiceName => "ShoppingCart";
    public string ServiceId { get; }

    private readonly Dictionary<string, CartItem> _items = new();

    public void AddItem(string productId, int quantity)
    {
        if (_items.TryGetValue(productId, out var item))
        {
            item.Quantity += quantity;
        }
        else
        {
            _items[productId] = new CartItem { ProductId = productId, Quantity = quantity };
        }
    }

    public void RemoveItem(string productId)
    {
        _items.Remove(productId);
    }

    public decimal GetTotal() => _items.Values.Sum(i => i.Price * i.Quantity);
}

// Hub
public class ShoppingCartHub : IPulseHub
{
    public async Task AddItemAsync(string userId, string productId, int quantity)
    {
        var service = await _factory.GetOrCreateAsync($"ShoppingCart:{userId}");
        service.AddItem(productId, quantity);
    }

    public async Task<decimal> GetTotalAsync(string userId)
    {
        var service = await _factory.GetOrCreateAsync($"ShoppingCart:{userId}");
        return service.GetTotal();
    }
}
```

---

## 10. 检查清单

### 设计阶段

- [ ] ServiceId 是否唯一？（格式：`{ServiceName}:{BusinessId}`）
- [ ] Service 是否只包含实例状态？（没有静态字段）
- [ ] Hub 是否无状态？（不缓存 Service 实例）
- [ ] 是否需要多个 Hub？（权限/功能分离）
- [ ] 是否实现了生命周期钩子？（OnActivate/OnDeactivate）

### 实现阶段

- [ ] Service 是否避免手动加锁？
- [ ] Hub 是否通过 Factory 获取 Service？
- [ ] 是否通过 DI 注入依赖？（不使用静态依赖）
- [ ] 是否处理异常？（不吞掉异常）
- [ ] 是否记录日志？（关键操作和异常）

### 测试阶段

- [ ] 是否编写了 Service 单元测试？
- [ ] 是否编写了 Hub 单元测试？
- [ ] 是否编写了集成测试？（多 Hub 共享状态）
- [ ] 是否测试了异常情况？
- [ ] 是否测试了生命周期钩子？

### 部署阶段

- [ ] 是否配置了合理的 IdleTimeout？
- [ ] 是否配置了合理的 MaxCachedInstances？
- [ ] 是否配置了合理的 WorkerThreadCount？
- [ ] 是否启用了健康检查？
- [ ] 是否配置了监控和日志？

---

## 总结

本文档提供了使用 PulseRPC.Server 开发有状态服务的最佳实践。关键要点：

1. **清晰的职责分离**：Service 管理状态，Hub 定义契约，Factory 管理生命周期
2. **线程安全保证**：ThreadAffinityManager 保证同一 ServiceId 的请求在同一线程执行
3. **灵活的生命周期**：通过钩子实现状态的加载、保存和健康检查
4. **完善的错误处理**：分层处理，日志记录，异常传播
5. **可测试性**：通过 DI 注入依赖，易于 Mock 和测试

遵循这些最佳实践，可以构建高性能、高可用的分布式服务。

---

**文档版本**：1.0
**最后更新**：2025-01-10
**反馈渠道**：https://github.com/yourorg/PulseRPC/issues
