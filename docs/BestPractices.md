# PulseRPC 最佳实践指南

本文档提供使用PulseRPC框架的最佳实践建议，帮助您构建高性能、可维护和可扩展的分布式应用程序。

## 📋 目录

- [架构设计](#架构设计)
- [服务设计](#服务设计)
- [性能优化](#性能优化)
- [错误处理](#错误处理)
- [安全考虑](#安全考虑)
- [监控与调试](#监控与调试)
- [部署实践](#部署实践)
- [维护指南](#维护指南)

## 架构设计

### 🏗️ 服务边界设计

#### 1. 领域驱动设计

```csharp
// ✅ 好的实践：按领域划分服务
public interface IUserService
{
    Task<User> GetUserAsync(int userId);
    Task<User> CreateUserAsync(CreateUserRequest request);
    Task<User> UpdateUserAsync(int userId, UpdateUserRequest request);
}

public interface IOrderService  
{
    Task<Order> GetOrderAsync(int orderId);
    Task<Order> CreateOrderAsync(CreateOrderRequest request);
    Task<OrderStatus> GetOrderStatusAsync(int orderId);
}

// ❌ 避免：过于宽泛的服务接口
public interface IBusinessService
{
    Task<User> GetUserAsync(int userId);
    Task<Order> GetOrderAsync(int orderId);
    Task<Product> GetProductAsync(int productId);
    // ... 混合了多个领域的方法
}
```

#### 2. 服务粒度控制

```csharp
// ✅ 推荐：适中的服务粒度
public interface IPaymentService
{
    Task<PaymentResult> ProcessPaymentAsync(PaymentRequest request);
    Task<PaymentStatus> GetPaymentStatusAsync(string paymentId);
    Task<RefundResult> RefundPaymentAsync(RefundRequest request);
}

// ❌ 避免：过于细粒度
public interface IPaymentProcessorService
{
    Task<PaymentResult> ProcessCreditCardAsync(CreditCardRequest request);
}

public interface IPaymentStatusService
{
    Task<PaymentStatus> GetStatusAsync(string paymentId);
}

public interface IRefundService
{
    Task<RefundResult> ProcessRefundAsync(RefundRequest request);
}
```

### 🔗 服务依赖管理

#### 1. 依赖方向控制

```csharp
// ✅ 好的实践：定义清晰的依赖关系
public class OrderService : IOrderService
{
    private readonly IUserService _userService;      // 用户服务
    private readonly IProductService _productService; // 产品服务
    private readonly IPaymentService _paymentService; // 支付服务

    // 订单服务依赖其他服务，但不被底层服务依赖
}
```

#### 2. 避免循环依赖

```csharp
// ❌ 避免：循环依赖
public class UserService : IUserService
{
    private readonly IOrderService _orderService; // 用户服务依赖订单服务
}

public class OrderService : IOrderService  
{
    private readonly IUserService _userService; // 订单服务依赖用户服务
}

// ✅ 解决方案：引入领域事件
public class UserService : IUserService
{
    private readonly IEventPublisher _eventPublisher;
    
    public async Task<User> UpdateUserAsync(UpdateUserRequest request)
    {
        var user = await UpdateUserInternalAsync(request);
        
        // 发布事件而不是直接调用其他服务
        await _eventPublisher.PublishAsync(new UserUpdatedEvent(user));
        
        return user;
    }
}
```

## 服务设计

### 📋 接口设计原则

#### 1. 使用异步模式

```csharp
// ✅ 推荐：异步接口
public interface IUserService
{
    Task<User> GetUserAsync(int userId);
    Task<IEnumerable<User>> GetUsersAsync(GetUsersRequest request);
    Task<User> CreateUserAsync(CreateUserRequest request);
}

// ❌ 避免：同步接口
public interface IUserService
{
    User GetUser(int userId);
    IEnumerable<User> GetUsers(GetUsersRequest request);
    User CreateUser(CreateUserRequest request);
}
```

#### 2. 版本化API设计

```csharp
// ✅ 推荐：版本化接口
namespace MyApp.Services.V1
{
    public interface IUserService
    {
        Task<User> GetUserAsync(int userId);
    }
}

namespace MyApp.Services.V2
{
    public interface IUserService
    {
        Task<UserV2> GetUserAsync(int userId);
        Task<UserProfile> GetUserProfileAsync(int userId); // 新增方法
    }
}

// 服务注册时支持多版本
services.AddPulseRpcService<V1.IUserService>(options =>
{
    options.ServiceName = "UserService";
    options.Version = "1.0";
});

services.AddPulseRpcService<V2.IUserService>(options =>
{
    options.ServiceName = "UserService";
    options.Version = "2.0";
});
```

#### 3. 请求响应模型设计

```csharp
// ✅ 推荐：明确的请求响应模型
public class GetUsersRequest
{
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string? NameFilter { get; set; }
    public UserStatus? StatusFilter { get; set; }
    public DateTime? CreatedAfter { get; set; }
}

public class GetUsersResponse
{
    public List<User> Users { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageIndex { get; set; }
    public int PageSize { get; set; }
    public bool HasNextPage => PageIndex * PageSize < TotalCount;
}

// ❌ 避免：使用过多参数
public interface IUserService
{
    Task<List<User>> GetUsersAsync(
        int pageIndex,
        int pageSize,
        string? nameFilter,
        UserStatus? statusFilter,
        DateTime? createdAfter,
        bool includeDeleted,
        string? sortBy,
        SortDirection sortDirection);
}
```

### 🔧 服务实现最佳实践

#### 1. 参数验证

```csharp
public class UserService : IUserService
{
    public async Task<User> GetUserAsync(int userId)
    {
        // ✅ 输入验证
        if (userId <= 0)
        {
            throw new ArgumentException("用户ID必须大于0", nameof(userId));
        }

        var user = await _repository.GetByIdAsync(userId);
        
        if (user == null)
        {
            throw new NotFoundException($"用户不存在: {userId}");
        }

        return user;
    }

    public async Task<User> CreateUserAsync(CreateUserRequest request)
    {
        // ✅ 业务逻辑验证
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            throw new ValidationException("邮箱地址不能为空");
        }

        if (await _repository.ExistsByEmailAsync(request.Email))
        {
            throw new BusinessException($"邮箱已存在: {request.Email}");
        }

        // 执行业务逻辑...
    }
}
```

#### 2. 异常处理设计

```csharp
// ✅ 定义明确的异常层次
public abstract class BusinessException : Exception
{
    public string ErrorCode { get; }
    
    protected BusinessException(string errorCode, string message) : base(message)
    {
        ErrorCode = errorCode;
    }
}

public class ValidationException : BusinessException
{
    public ValidationException(string message) 
        : base("VALIDATION_ERROR", message) { }
}

public class NotFoundException : BusinessException
{
    public NotFoundException(string message) 
        : base("NOT_FOUND", message) { }
}

public class BusinessLogicException : BusinessException
{
    public BusinessLogicException(string errorCode, string message) 
        : base(errorCode, message) { }
}

// 全局异常处理
public class GlobalExceptionHandler : IExceptionHandler
{
    public async Task<RpcResponse> HandleAsync(Exception exception, RpcRequest request)
    {
        return exception switch
        {
            ValidationException ex => new RpcResponse 
            { 
                Error = new RpcError 
                { 
                    Code = ex.ErrorCode, 
                    Message = ex.Message,
                    Type = "ValidationError"
                } 
            },
            NotFoundException ex => new RpcResponse 
            { 
                Error = new RpcError 
                { 
                    Code = ex.ErrorCode, 
                    Message = ex.Message,
                    Type = "NotFound"
                } 
            },
            _ => new RpcResponse 
            { 
                Error = new RpcError 
                { 
                    Code = "INTERNAL_ERROR", 
                    Message = "内部服务器错误",
                    Type = "InternalError"
                } 
            }
        };
    }
}
```

## 性能优化

### ⚡ 连接池优化

```csharp
// ✅ 连接池配置优化
services.AddPulseRpcClient(options =>
{
    options.ConnectionPool = new ConnectionPoolOptions
    {
        MaxConnections = Environment.ProcessorCount * 10, // 基于CPU核心数
        MaxIdleTime = TimeSpan.FromMinutes(5),
        ConnectionTimeout = TimeSpan.FromSeconds(30),
        KeepAliveInterval = TimeSpan.FromMinutes(1),
        EnableConnectionPooling = true
    };
});
```

### 📦 批量操作

```csharp
// ✅ 推荐：批量操作接口
public interface IUserService
{
    Task<User> GetUserAsync(int userId);
    Task<List<User>> GetUsersAsync(List<int> userIds); // 批量获取
    
    Task<User> CreateUserAsync(CreateUserRequest request);
    Task<List<User>> CreateUsersAsync(List<CreateUserRequest> requests); // 批量创建
}

// 实现批量优化
public class UserService : IUserService
{
    public async Task<List<User>> GetUsersAsync(List<int> userIds)
    {
        // ✅ 单次数据库查询而不是多次
        return await _repository.GetByIdsAsync(userIds);
    }
}
```

### 🗂️ 缓存策略

```csharp
public class UserService : IUserService
{
    private readonly IMemoryCache _cache;
    private readonly IUserRepository _repository;

    public async Task<User> GetUserAsync(int userId)
    {
        // ✅ 缓存策略
        var cacheKey = $"user:{userId}";
        
        if (_cache.TryGetValue(cacheKey, out User cachedUser))
        {
            return cachedUser;
        }

        var user = await _repository.GetByIdAsync(userId);
        
        if (user != null)
        {
            _cache.Set(cacheKey, user, TimeSpan.FromMinutes(10));
        }

        return user;
    }

    public async Task<User> UpdateUserAsync(int userId, UpdateUserRequest request)
    {
        var user = await _repository.UpdateAsync(userId, request);
        
        // ✅ 更新后清除缓存
        _cache.Remove($"user:{userId}");
        
        return user;
    }
}
```

### 🔄 异步并发处理

```csharp
public class OrderService : IOrderService
{
    public async Task<Order> CreateOrderAsync(CreateOrderRequest request)
    {
        // ✅ 并行执行独立操作
        var userTask = _userService.GetUserAsync(request.UserId);
        var productTasks = request.Items.Select(item => 
            _productService.GetProductAsync(item.ProductId));
        
        // 等待所有任务完成
        var user = await userTask;
        var products = await Task.WhenAll(productTasks);

        // 验证和创建订单
        ValidateOrder(user, products, request);
        
        return await _repository.CreateAsync(new Order
        {
            UserId = user.Id,
            Items = CreateOrderItems(products, request.Items),
            // ...
        });
    }
}
```

## 错误处理

### 🔄 重试策略

```csharp
// ✅ 配置智能重试策略
services.AddPulseRpcService<IPaymentService>(options =>
{
    options.ServiceName = "PaymentService";
    options.RetryCount = 3;
    options.RetryPolicy = RetryPolicy.ExponentialBackoff;
    options.RetryOptions = new ExponentialBackoffOptions
    {
        BaseDelay = TimeSpan.FromMilliseconds(500),
        MaxDelay = TimeSpan.FromSeconds(30),
        Multiplier = 2.0,
        Jitter = true // 添加随机抖动避免雷群效应
    };
});

// 特定异常的重试策略
services.Configure<RetryPolicyOptions>(options =>
{
    options.RetriableExceptions = new[]
    {
        typeof(TimeoutException),
        typeof(SocketException),
        typeof(HttpRequestException)
    };
    
    options.NonRetriableExceptions = new[]
    {
        typeof(ArgumentException),
        typeof(ValidationException),
        typeof(UnauthorizedException)
    };
});
```

### ⚡ 熔断器模式

```csharp
// ✅ 熔断器配置
services.AddPulseRpcService<IExternalApiService>(options =>
{
    options.ServiceName = "ExternalApiService";
    options.CircuitBreakerOptions = new CircuitBreakerOptions
    {
        Enabled = true,
        FailureThreshold = 5,        // 5次失败后熔断
        RecoveryTimeout = TimeSpan.FromMinutes(1), // 1分钟后尝试恢复
        SamplingDuration = TimeSpan.FromMinutes(1), // 1分钟内的失败计数
        MinimumThroughput = 10       // 最少10个请求后才开始计算失败率
    };
});
```

### 🛡️ 超时处理

```csharp
// ✅ 分层超时配置
services.AddPulseRpcService<IUserService>(options =>
{
    options.ServiceName = "UserService";
    options.Timeout = TimeSpan.FromSeconds(30); // 默认超时
});

services.AddPulseRpcService<IReportService>(options =>
{
    options.ServiceName = "ReportService";
    options.Timeout = TimeSpan.FromMinutes(5); // 报表服务需要更长时间
});

// 方法级别超时控制
public class OrderService : IOrderService
{
    public async Task<Order> CreateOrderAsync(CreateOrderRequest request)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        
        try
        {
            // 快速操作使用较短超时
            var user = await _userService.GetUserAsync(request.UserId, cts.Token);
            return await ProcessOrderAsync(user, request);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("创建订单操作超时");
        }
    }
}
```

## 安全考虑

### 🔐 认证和授权

```csharp
// ✅ 服务端认证配置
services.AddPulseRpcServer(options =>
{
    options.Authentication = new AuthenticationOptions
    {
        Enabled = true,
        Scheme = "Bearer",
        Authority = "https://your-identity-server.com",
        Audience = "pulse-rpc-api",
        ValidateIssuer = true,
        ValidateAudience = true,
        RequireHttpsMetadata = true
    };
});

// 方法级别授权
[Authorize(Roles = "Admin")]
public async Task<User> DeleteUserAsync(int userId)
{
    // 只有管理员可以删除用户
}

[Authorize(Policy = "UserManagement")]
public async Task<User> UpdateUserAsync(int userId, UpdateUserRequest request)
{
    // 需要用户管理权限
}
```

### 🔒 数据保护

```csharp
public class UserService : IUserService
{
    public async Task<User> GetUserAsync(int userId)
    {
        var user = await _repository.GetByIdAsync(userId);
        
        // ✅ 敏感数据脱敏
        return new User
        {
            Id = user.Id,
            Name = user.Name,
            Email = MaskEmail(user.Email), // 脱敏邮箱
            Phone = MaskPhone(user.Phone), // 脱敏手机号
            // 不返回密码等敏感信息
        };
    }

    private string MaskEmail(string email)
    {
        if (string.IsNullOrEmpty(email)) return email;
        
        var parts = email.Split('@');
        if (parts.Length != 2) return email;
        
        var localPart = parts[0];
        var domain = parts[1];
        
        if (localPart.Length <= 2) return email;
        
        return $"{localPart[0]}***{localPart[^1]}@{domain}";
    }
}
```

### 🛡️ 输入验证

```csharp
// ✅ 严格的输入验证
public class CreateUserRequest
{
    [Required(ErrorMessage = "用户名不能为空")]
    [StringLength(50, MinimumLength = 2, ErrorMessage = "用户名长度必须在2-50个字符之间")]
    [RegularExpression(@"^[a-zA-Z0-9\u4e00-\u9fa5]+$", ErrorMessage = "用户名只能包含字母、数字和中文")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "邮箱不能为空")]
    [EmailAddress(ErrorMessage = "邮箱格式不正确")]
    [StringLength(100, ErrorMessage = "邮箱长度不能超过100个字符")]
    public string Email { get; set; } = string.Empty;

    [Phone(ErrorMessage = "手机号格式不正确")]
    public string? Phone { get; set; }
}

// 自定义验证特性
public class NoSqlInjectionAttribute : ValidationAttribute
{
    public override bool IsValid(object? value)
    {
        if (value is string str)
        {
            var dangerousPatterns = new[] { "'", "\"", ";", "--", "/*", "*/" };
            return !dangerousPatterns.Any(pattern => str.Contains(pattern));
        }
        return true;
    }
}
```

## 监控与调试

### 📊 监控指标

```csharp
// ✅ 业务指标监控
public class OrderService : IOrderService
{
    private readonly IMetricsCollector _metrics;

    public async Task<Order> CreateOrderAsync(CreateOrderRequest request)
    {
        using var timer = _metrics.StartTimer("order_creation_duration");
        
        try
        {
            _metrics.IncrementCounter("order_creation_attempts");
            
            var order = await ProcessOrderAsync(request);
            
            _metrics.IncrementCounter("order_creation_success");
            _metrics.RecordHistogram("order_value", order.TotalAmount);
            
            return order;
        }
        catch (Exception ex)
        {
            _metrics.IncrementCounter("order_creation_failures", new Dictionary<string, string>
            {
                ["error_type"] = ex.GetType().Name
            });
            
            throw;
        }
    }
}
```

### 🔍 结构化日志

```csharp
public class UserService : IUserService
{
    private readonly ILogger<UserService> _logger;

    public async Task<User> UpdateUserAsync(int userId, UpdateUserRequest request)
    {
        // ✅ 结构化日志记录
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["UserId"] = userId,
            ["Operation"] = "UpdateUser",
            ["RequestId"] = Guid.NewGuid()
        });

        _logger.LogInformation("开始更新用户信息 {UserId}", userId);

        try
        {
            var user = await _repository.UpdateAsync(userId, request);
            
            _logger.LogInformation("用户信息更新成功 {UserId}, 更新字段: {UpdatedFields}", 
                userId, GetUpdatedFields(request));
            
            return user;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "用户信息更新失败 {UserId}", userId);
            throw;
        }
    }
}
```

### 🕵️ 链路追踪

```csharp
public class OrderService : IOrderService
{
    private readonly ITracer _tracer;

    public async Task<Order> CreateOrderAsync(CreateOrderRequest request)
    {
        using var span = _tracer.StartSpan("create_order");
        span.SetTag("user.id", request.UserId);
        span.SetTag("order.items_count", request.Items.Count);
        
        try
        {
            // 用户验证跨度
            var user = await _tracer.WithSpanAsync("validate_user", async () =>
            {
                return await _userService.GetUserAsync(request.UserId);
            }, span);

            // 库存检查跨度
            await _tracer.WithSpanAsync("check_inventory", async () =>
            {
                await ValidateInventoryAsync(request.Items);
            }, span);

            // 创建订单跨度
            var order = await _tracer.WithSpanAsync("persist_order", async () =>
            {
                return await _repository.CreateAsync(request);
            }, span);

            span.SetStatus(SpanStatus.Ok);
            return order;
        }
        catch (Exception ex)
        {
            span.RecordException(ex);
            throw;
        }
    }
}
```

## 部署实践

### 🐳 容器化部署

```dockerfile
# ✅ 优化的Dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

# 创建非root用户
RUN addgroup --system --gid 1001 dotnet
RUN adduser --system --uid 1001 --ingroup dotnet dotnet

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# 先复制项目文件，利用Docker缓存
COPY ["MyService/MyService.csproj", "MyService/"]
COPY ["MyService.Contracts/MyService.Contracts.csproj", "MyService.Contracts/"]
RUN dotnet restore "MyService/MyService.csproj"

# 复制源代码并构建
COPY . .
WORKDIR "/src/MyService"
RUN dotnet build "MyService.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "MyService.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# 使用非root用户运行
USER dotnet

ENTRYPOINT ["dotnet", "MyService.dll"]
```

### ☸️ Kubernetes部署

```yaml
# ✅ 生产级Kubernetes配置
apiVersion: apps/v1
kind: Deployment
metadata:
  name: user-service
  labels:
    app: user-service
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
      app: user-service
  template:
    metadata:
      labels:
        app: user-service
        version: v1.0.0
    spec:
      containers:
      - name: user-service
        image: user-service:v1.0.0
        ports:
        - containerPort: 8080
          name: http
        env:
        - name: ASPNETCORE_ENVIRONMENT
          value: "Production"
        - name: PULSERPC_SERVER_HOST
          value: "0.0.0.0"
        - name: PULSERPC_SERVER_PORT
          value: "8080"
        resources:
          requests:
            memory: "256Mi"
            cpu: "250m"
          limits:
            memory: "512Mi"
            cpu: "500m"
        livenessProbe:
          httpGet:
            path: /health
            port: 8080
          initialDelaySeconds: 30
          periodSeconds: 10
        readinessProbe:
          httpGet:
            path: /ready
            port: 8080
          initialDelaySeconds: 5
          periodSeconds: 5
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
  name: user-service
spec:
  selector:
    app: user-service
  ports:
  - name: http
    port: 80
    targetPort: 8080
  type: ClusterIP
```

### 📈 水平扩展

```csharp
// ✅ 无状态服务设计
public class UserService : IUserService
{
    // 避免使用实例变量保存状态
    private readonly IUserRepository _repository;
    private readonly IMemoryCache _cache; // 本地缓存，可接受数据不一致
    
    public UserService(IUserRepository repository, IMemoryCache cache)
    {
        _repository = repository;
        _cache = cache;
    }

    public async Task<User> GetUserAsync(int userId)
    {
        // 每个请求都是独立的，不依赖服务实例状态
        return await _repository.GetByIdAsync(userId);
    }
}

// HPA配置
apiVersion: autoscaling/v2
kind: HorizontalPodAutoscaler
metadata:
  name: user-service-hpa
spec:
  scaleTargetRef:
    apiVersion: apps/v1
    kind: Deployment
    name: user-service
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

## 维护指南

### 🔧 版本管理

```csharp
// ✅ 向后兼容的版本升级
namespace MyApp.Services.V1
{
    public interface IUserService
    {
        Task<User> GetUserAsync(int userId);
    }
    
    public class User
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
    }
}

namespace MyApp.Services.V2
{
    public interface IUserService
    {
        Task<UserV2> GetUserAsync(int userId);
        Task<UserProfile> GetUserProfileAsync(int userId); // 新方法
    }
    
    public class UserV2
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public DateTime CreatedAt { get; set; } // 新字段
        public UserStatus Status { get; set; } // 新字段
    }
}

// 服务端同时支持多版本
services.AddScoped<V1.IUserService, UserServiceV1>();
services.AddScoped<V2.IUserService, UserServiceV2>();
```

### 📊 性能监控

```csharp
// ✅ 建立性能基线
public class PerformanceMonitoringService
{
    private readonly IMetricsCollector _metrics;
    
    public async Task MonitorServicePerformance()
    {
        // 系统指标
        _metrics.RecordGauge("system.cpu_usage", GetCpuUsage());
        _metrics.RecordGauge("system.memory_usage", GetMemoryUsage());
        _metrics.RecordGauge("system.active_connections", GetActiveConnections());
        
        // 业务指标
        _metrics.RecordGauge("business.orders_per_minute", GetOrdersPerMinute());
        _metrics.RecordGauge("business.revenue_per_hour", GetRevenuePerHour());
        
        // SLA指标
        _metrics.RecordHistogram("sla.response_time_p95", GetResponseTimeP95());
        _metrics.RecordGauge("sla.error_rate", GetErrorRate());
        _metrics.RecordGauge("sla.availability", GetAvailability());
    }
}
```

### 🚨 告警配置

```yaml
# Prometheus告警规则
groups:
- name: pulserpc.rules
  rules:
  - alert: HighErrorRate
    expr: rate(pulserpc_requests_failed_total[5m]) > 0.1
    for: 2m
    labels:
      severity: warning
    annotations:
      summary: "PulseRPC服务错误率过高"
      description: "服务 {{ $labels.service }} 在过去5分钟内错误率超过10%"

  - alert: HighResponseTime
    expr: histogram_quantile(0.95, rate(pulserpc_request_duration_seconds_bucket[5m])) > 1.0
    for: 5m
    labels:
      severity: critical
    annotations:
      summary: "PulseRPC服务响应时间过长"
      description: "服务 {{ $labels.service }} P95响应时间超过1秒"

  - alert: ServiceDown
    expr: up{job="pulserpc"} == 0
    for: 1m
    labels:
      severity: critical
    annotations:
      summary: "PulseRPC服务不可用"
      description: "服务 {{ $labels.instance }} 已停止响应"
```

### 🔄 优雅关闭

```csharp
public class Program
{
    public static async Task Main(string[] args)
    {
        var host = CreateHostBuilder(args).Build();

        // ✅ 注册优雅关闭处理
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        
        lifetime.ApplicationStopping.Register(() =>
        {
            Console.WriteLine("应用正在停止，等待当前请求完成...");
        });

        try
        {
            await host.RunAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"应用启动失败: {ex.Message}");
            throw;
        }
    }
}

// 服务实现中的优雅关闭
public class OrderService : IOrderService, IDisposable
{
    private readonly CancellationTokenSource _shutdownToken = new();

    public async Task<Order> CreateOrderAsync(CreateOrderRequest request)
    {
        // 检查是否正在关闭
        _shutdownToken.Token.ThrowIfCancellationRequested();
        
        // 执行业务逻辑...
    }

    public void Dispose()
    {
        _shutdownToken.Cancel();
        _shutdownToken.Dispose();
    }
}
```

遵循这些最佳实践将帮助您构建健壮、高性能和可维护的PulseRPC应用程序。记住，最佳实践会随着技术发展而演进，请持续关注框架更新和社区经验分享。 