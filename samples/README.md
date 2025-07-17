# PulseRPC 示例项目

本目录包含 PulseRPC 框架的各种使用示例，帮助开发者快速上手和理解框架的功能特性。

## 🚀 快速开始

### 环境要求

- .NET 8.0 或更高版本
- Visual Studio 2022 或 JetBrains Rider
- 可选：Docker（用于运行 Consul）

### 运行示例

1. **基础使用示例**
   ```bash
   cd samples/BasicUsage
   dotnet run
   ```

2. **Consul 服务发现示例**
   ```bash
   # 首先启动 Consul (使用 Docker)
   docker run -d --name consul -p 8500:8500 consul:latest agent -dev -client=0.0.0.0
   
   # 运行示例
   cd samples/ConsulExample
   dotnet run
   ```

## 📁 示例目录

### 1. BasicUsage - 基础使用示例

**功能演示：**
- ✅ 静态服务发现配置
- ✅ 轮询和故障转移负载均衡
- ✅ 健康检查机制
- ✅ 客户端工厂模式
- ✅ 依赖注入配置
- ✅ 性能统计收集

**核心特性：**
- 📋 完整的服务配置示例
- 🔍 服务发现和端点管理
- ⚖️ 多种负载均衡策略
- 🏥 健康检查和故障检测
- 📊 统计信息收集和展示

**运行输出：**
```
=== PulseRPC 基础使用示例 ===
📋 配置PulseRPC服务...
✅ PulseRPC服务配置完成

🔍 === 服务发现示例 ===
发现用户服务端点数量: 2
  - UserService-127.0.0.1:8001: 127.0.0.1:8001
  - UserService-127.0.0.1:8002: 127.0.0.1:8002
✅ 服务发现示例完成

⚖️ === 负载均衡示例 ===
使用轮询负载均衡策略:
  第1次选择: 127.0.0.1:8003
  第2次选择: 127.0.0.1:8004
  第3次选择: 127.0.0.1:8005
✅ 负载均衡示例完成
```

### 2. ConsulExample - Consul 服务发现示例

**功能演示：**
- ✅ Consul 服务注册和发现
- ✅ 服务健康检查集成
- ✅ 标签和元数据支持
- ✅ 故障转移和断路器
- ✅ 服务变化监听
- ✅ 动态配置管理

**核心特性：**
- 🏛️ Consul 集成配置
- 🏷️ 服务标签和元数据
- 👀 实时服务变化监听
- 🔄 高级故障转移机制
- 📡 分布式服务注册中心

**前置条件：**
```bash
# 使用 Docker 启动 Consul
docker run -d \
  --name consul-dev \
  -p 8500:8500 \
  consul:latest agent -dev -client=0.0.0.0

# 验证 Consul 是否运行
curl http://localhost:8500/v1/status/leader
```

## 🔧 配置选项

### 服务发现配置

```json
{
  "PulseRPC": {
    "Client": {
      "ServiceDiscoveryOptions": {
        "RefreshInterval": "00:00:30",
        "CacheTimeout": "00:05:00",
        "EnableCaching": true,
        "EnableHealthCheck": true
      }
    }
  }
}
```

### 负载均衡配置

```json
{
  "PulseRPC": {
    "Client": {
      "LoadBalancingOptions": {
        "Strategy": "RoundRobin",
        "EnableHealthCheck": true,
        "HealthCheckInterval": "00:00:30"
      }
    }
  }
}
```

### Consul 配置

```json
{
  "Consul": {
    "Address": "http://localhost:8500",
    "Datacenter": "dc1",
    "EnableHealthCheck": true,
    "HealthCheckInterval": "00:00:10",
    "DeregisterOnShutdown": true
  }
}
```

### 健康检查配置

```json
{
  "HealthCheck": {
    "DefaultTimeout": "00:00:05",
    "RetryCount": 2,
    "EnableConcurrentChecks": true,
    "MaxConcurrentChecks": 50
  }
}
```

### 故障转移配置

```json
{
  "Failover": {
    "FailureThreshold": 3,
    "RecoveryThreshold": 2,
    "CircuitBreakerOpenTime": "00:01:00",
    "EnableGracefulDegradation": true
  }
}
```

## 🎯 使用场景

### 1. 微服务架构

PulseRPC 特别适合微服务架构，提供：
- 🔍 **自动服务发现** - 无需手动配置端点
- ⚖️ **智能负载均衡** - 自动流量分发
- 🏥 **健康检查** - 实时监控服务状态
- 🔄 **故障转移** - 自动处理服务故障

### 2. 分布式系统

在分布式环境中提供：
- 📡 **服务注册中心** - 集中管理服务实例
- 🏷️ **服务标签** - 灵活的服务分组和路由
- 👀 **动态更新** - 实时响应服务变化
- 📊 **监控指标** - 全面的性能统计

### 3. 云原生应用

支持云原生部署：
- 🐳 **容器化部署** - Docker 和 Kubernetes 集成
- 🔧 **配置管理** - 外部化配置支持
- 📈 **弹性伸缩** - 动态服务实例管理
- 🛡️ **高可用性** - 多实例容错机制

## 📚 进阶主题

### 自定义负载均衡策略

```csharp
public class CustomLoadBalancer : ILoadBalancer
{
    public LoadBalancingStrategy Strategy => LoadBalancingStrategy.Custom;
    
    public async Task<ServiceEndpoint?> SelectAsync(
        IReadOnlyList<ServiceEndpoint> endpoints, 
        LoadBalancingContext context)
    {
        // 自定义选择逻辑
        return endpoints.FirstOrDefault();
    }
    
    // ... 其他方法实现
}

// 注册自定义负载均衡器
services.AddCustomLoadBalancer<CustomLoadBalancer>();
```

### 自定义健康检查

```csharp
public class CustomHealthChecker : IHealthChecker
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        ServiceEndpoint endpoint, 
        CancellationToken cancellationToken = default)
    {
        // 自定义健康检查逻辑
        return new HealthCheckResult
        {
            ServiceId = endpoint.ServiceId,
            Status = HealthStatus.Healthy,
            CheckTime = DateTime.UtcNow,
            ResponseTime = TimeSpan.FromMilliseconds(50)
        };
    }
    
    // ... 其他方法实现
}

// 注册自定义健康检查器
services.AddCustomHealthChecker<CustomHealthChecker>();
```

### 自定义服务发现

```csharp
public class DatabaseServiceDiscovery : IServiceDiscovery
{
    public async Task<IReadOnlyList<ServiceEndpoint>> DiscoverAsync(
        string serviceName, 
        CancellationToken cancellationToken = default)
    {
        // 从数据库查询服务端点
        var endpoints = await QueryEndpointsFromDatabase(serviceName);
        return endpoints.AsReadOnly();
    }
    
    // ... 其他方法实现
}

// 注册自定义服务发现
services.AddCustomServiceDiscovery<DatabaseServiceDiscovery>();
```

## 🐛 故障排除

### 常见问题

1. **Consul 连接失败**
   ```
   错误: Unable to connect to Consul at http://localhost:8500
   解决: 确保 Consul 服务正在运行并监听正确端口
   ```

2. **服务发现返回空结果**
   ```
   问题: DiscoverAsync 返回空列表
   检查: 
   - 服务是否已正确注册
   - 服务名称是否匹配
   - 健康检查是否通过
   ```

3. **负载均衡器选择失败**
   ```
   问题: SelectAsync 返回 null
   检查:
   - 是否有健康的服务实例
   - 故障转移配置是否正确
   - 断路器状态是否正常
   ```

### 调试技巧

1. **启用详细日志**
   ```json
   {
     "Logging": {
       "LogLevel": {
         "PulseRPC": "Debug"
       }
     }
   }
   ```

2. **监控统计信息**
   ```csharp
   var stats = serviceDiscoveryClient.GetStatistics();
   foreach (var stat in stats)
   {
       logger.LogInformation("{Key}: {Value}", stat.Key, stat.Value);
   }
   ```

3. **健康检查诊断**
   ```csharp
   var healthResult = await healthChecker.CheckHealthAsync(endpoint);
   if (healthResult.Status != HealthStatus.Healthy)
   {
       logger.LogWarning("健康检查失败: {Details}", healthResult.Details);
   }
   ```

## 🤝 贡献指南

欢迎提交新的示例和改进现有示例：

1. Fork 项目仓库
2. 创建功能分支
3. 添加示例代码和文档
4. 提交 Pull Request

### 示例编写规范

- ✅ 包含完整的配置示例
- ✅ 提供详细的中文注释
- ✅ 添加错误处理和日志记录
- ✅ 包含性能统计和监控
- ✅ 提供运行说明和故障排除

## 📄 许可证

本示例项目遵循 MIT 许可证。详情请参考 [LICENSE](../LICENSE) 文件。 