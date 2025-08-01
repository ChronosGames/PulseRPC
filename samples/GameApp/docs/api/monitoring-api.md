# 监控与性能 API 文档

## 概述

监控API提供系统性能监控、告警管理和运维仪表板功能。

**Base URL**: `http://localhost:5000/api`

## 性能监控 API

### 获取性能统计

**GET** `/performance/stats`

**Headers**: `Authorization: Bearer <token>`

获取系统性能统计数据。

#### 查询参数
- `hours`: 统计时间范围（小时，默认1小时）
- `category`: 性能类别筛选

#### 响应
```json
{
  "success": true,
  "timeRange": "01:00:00",
  "generatedAt": "2024-01-01T12:00:00Z",
  "stats": {
    "totalRequests": 12345,
    "averageResponseTime": 85.6,
    "errorRate": 0.02,
    "requestsPerSecond": 34.2,
    "operations": {
      "login": {
        "name": "login",
        "totalRequests": 1234,
        "successRate": 0.98,
        "errorRate": 0.02,
        "averageResponseTime": 120.5,
        "p95ResponseTime": 250.0,
        "minResponseTime": 45.2,
        "maxResponseTime": 500.0
      }
    }
  },
  "resourceUsage": {
    "cpuUsagePercent": 45.6,
    "memoryUsageBytes": 1024000000,
    "memoryUsagePercent": 65.4,
    "availableMemoryBytes": 512000000,
    "threadCount": 25,
    "gcCollectionCount": 123,
    "uptime": "2.15:30:45",
    "timestamp": "2024-01-01T12:00:00Z"
  }
}
```

### 运行性能基准测试

**POST** `/performance/benchmark`

**Headers**: `Authorization: Bearer <token>`

运行系统性能基准测试。

#### 请求体
```json
{
  "testType": "comprehensive",  // 测试类型: cpu, memory, disk, network, comprehensive
  "duration": 30,              // 测试持续时间(秒)
  "intensity": "medium"        // 测试强度: low, medium, high
}
```

#### 响应
```json
{
  "success": true,
  "testId": "benchmark_12345",
  "status": "running",
  "estimatedDuration": 30,
  "startTime": "2024-01-01T12:00:00Z"
}
```

### 获取基准测试结果

**GET** `/performance/benchmark/{testId}`

**Headers**: `Authorization: Bearer <token>`

获取基准测试结果。

#### 响应
```json
{
  "success": true,
  "testId": "benchmark_12345",
  "status": "completed",
  "results": {
    "cpuPerformance": {
      "score": 850,
      "operationsPerSecond": 12345,
      "averageLatency": 0.08
    },
    "memoryPerformance": {
      "score": 920,
      "throughputMBps": 2500,
      "latency": 0.001
    },
    "diskPerformance": {
      "score": 750,
      "readSpeedMBps": 150,
      "writeSpeedMBps": 100,
      "iops": 1000
    },
    "networkPerformance": {
      "score": 800,
      "bandwidthMbps": 100,
      "latency": 5.2,
      "packetLoss": 0.001
    },
    "overallScore": 830
  },
  "duration": "00:00:30",
  "completedAt": "2024-01-01T12:00:30Z"
}
```

## 监控仪表板 API

### 获取监控仪表板

**GET** `/monitoring/dashboard`

**Headers**: `Authorization: Bearer <token>`

获取完整的监控仪表板数据。

#### 查询参数
- `hours`: 时间范围（1-24小时，默认1小时）

#### 响应
```json
{
  "success": true,
  "timeRange": "01:00:00",
  "generatedAt": "2024-01-01T12:00:00Z",
  "systemHealth": "Healthy",  // Healthy, Degraded, Unhealthy
  "performanceStats": {
    "totalRequests": 12345,
    "averageResponseTime": 85.6,
    "errorRate": 0.02,
    "requestsPerSecond": 34.2
  },
  "resourceUsage": {
    "cpuUsagePercent": 45.6,
    "memoryUsagePercent": 65.4,
    "uptime": "2.15:30:45"
  },
  "activeAlerts": [
    {
      "id": "alert_123",
      "level": "Warning",
      "title": "高CPU使用率",
      "message": "CPU使用率达到80%",
      "createdAt": "2024-01-01T11:30:00Z",
      "status": "Active"
    }
  ],
  "summary": {
    "totalRequests": 12345,
    "averageResponseTime": 85.6,
    "errorRate": 0.02,
    "activeAlerts": 1,
    "cpuUsage": 45.6,
    "memoryUsage": 65.4,
    "uptime": "2.15:30:45"
  }
}
```

### 获取系统健康状态

**GET** `/monitoring/health`

检查系统整体健康状态。

#### 响应
```json
{
  "status": "Healthy",
  "checkedAt": "2024-01-01T12:00:00Z",
  "details": {
    "cpuUsage": 45.6,
    "memoryUsage": 65.4,
    "errorRate": 0.02,
    "averageResponseTime": 85.6,
    "activeAlerts": 1,
    "criticalAlerts": 0,
    "uptime": 2.6
  }
}
```

**HTTP状态码**:
- `200 OK`: 系统健康或轻微降级
- `503 Service Unavailable`: 系统不健康

### 获取性能指标趋势

**GET** `/monitoring/metrics/trends`

**Headers**: `Authorization: Bearer <token>`

获取性能指标的时间趋势数据。

#### 查询参数
- `hours`: 时间范围（1-24小时，默认6小时）

#### 响应
```json
{
  "success": true,
  "timeRange": "06:00:00",
  "intervals": 12,
  "dataPoints": [
    {
      "timestamp": "2024-01-01T06:00:00Z",
      "cpuUsage": 45.6,
      "memoryUsage": 65.4,
      "requestsPerSecond": 34.2,
      "averageResponseTime": 85.6,
      "errorRate": 0.02
    }
  ]
}
```

## 告警管理 API

### 获取告警列表

**GET** `/monitoring/alerts`

**Headers**: `Authorization: Bearer <token>`

获取系统告警列表。

#### 查询参数
- `status`: 告警状态筛选 (`Active`, `Resolved`, `Suppressed`)
- `level`: 告警级别筛选 (`Info`, `Warning`, `Critical`, `Emergency`)

#### 响应
```json
{
  "success": true,
  "alerts": [
    {
      "id": "alert_123",
      "level": "Warning",
      "status": "Active",
      "title": "高CPU使用率",
      "message": "CPU使用率超过80%，当前值: 85.6%, 阈值: 80%",
      "createdAt": "2024-01-01T11:30:00Z",
      "resolvedAt": null,
      "resolvedBy": null,
      "resolution": null,
      "metadata": {
        "ruleId": "cpu_rule_001",
        "metricName": "system.cpu_usage",
        "currentValue": 85.6,
        "threshold": 80.0,
        "condition": "GreaterThan"
      },
      "notificationCount": 3,
      "lastNotificationAt": "2024-01-01T11:45:00Z"
    }
  ]
}
```

### 解决告警

**POST** `/monitoring/alerts/{alertId}/resolve`

**Headers**: `Authorization: Bearer <token>`

解决指定的告警。

#### 请求体
```json
{
  "resolvedBy": "运维团队",
  "resolution": "已优化代码，CPU使用率已降低"
}
```

#### 响应
```json
{
  "success": true,
  "message": "告警已解决"
}
```

### 创建自定义告警

**POST** `/monitoring/alerts`

**Headers**: `Authorization: Bearer <token>`

创建自定义告警。

#### 请求体
```json
{
  "level": "Warning",         // Info, Warning, Critical, Emergency
  "title": "业务异常告警",
  "message": "检测到订单处理异常",
  "metadata": {
    "component": "OrderService",
    "errorCode": "ORD_001",
    "affectedUsers": 10
  }
}
```

#### 响应
```json
{
  "success": true,
  "message": "告警已创建",
  "alertId": "alert_456"
}
```

## 操作统计 API

### 获取操作统计

**GET** `/monitoring/operations`

**Headers**: `Authorization: Bearer <token>`

获取各个操作的详细统计信息。

#### 查询参数
- `hours`: 统计时间范围（默认1小时）
- `sortBy`: 排序字段 (`totalRequests`, `errorRate`, `responseTime`)

#### 响应
```json
{
  "success": true,
  "operations": [
    {
      "name": "login",
      "totalRequests": 1234,
      "successRate": 98.0,
      "errorRate": 2.0,
      "averageResponseTime": 120.5,
      "p95ResponseTime": 250.0,
      "minResponseTime": 45.2,
      "maxResponseTime": 500.0
    },
    {
      "name": "register",
      "totalRequests": 456,
      "successRate": 95.5,
      "errorRate": 4.5,
      "averageResponseTime": 180.2,
      "p95ResponseTime": 350.0,
      "minResponseTime": 80.1,
      "maxResponseTime": 800.0
    }
  ]
}
```

### 获取系统事件日志

**GET** `/monitoring/events`

**Headers**: `Authorization: Bearer <token>`

获取系统事件日志。

#### 查询参数
- `limit`: 返回记录数量限制（默认100）
- `type`: 事件类型筛选 (`LOGIN`, `ERROR`, `PERFORMANCE`, `SECURITY`, `SYSTEM`)
- `severity`: 严重程度筛选 (`Info`, `Warning`, `Error`, `Critical`)

#### 响应
```json
{
  "success": true,
  "events": [
    {
      "id": "event_123",
      "type": "LOGIN",
      "message": "用户登录成功",
      "timestamp": "2024-01-01T12:00:00Z",
      "severity": "Info"
    },
    {
      "id": "event_124",
      "type": "ERROR",
      "message": "数据库连接失败",
      "timestamp": "2024-01-01T11:58:00Z",
      "severity": "Error"
    }
  ]
}
```

## 错误码

| 错误码 | HTTP状态 | 描述 |
|--------|----------|------|
| 6001 | 400 | 时间范围参数无效 |
| 6002 | 400 | 基准测试参数无效 |
| 6003 | 404 | 基准测试不存在 |
| 6004 | 404 | 告警不存在 |
| 6005 | 403 | 权限不足 |
| 6006 | 429 | 请求频率过高 |
| 6500 | 500 | 性能监控服务异常 |

## 监控指标说明

### 系统资源指标
- **CPU使用率**: 系统CPU占用百分比
- **内存使用率**: 物理内存占用百分比
- **线程数**: 当前活跃线程数量
- **GC回收次数**: 垃圾回收执行次数
- **系统运行时间**: 服务启动后的运行时间

### 应用性能指标
- **请求总数**: 处理的HTTP请求总数
- **平均响应时间**: 请求处理平均耗时（毫秒）
- **错误率**: 请求失败比例
- **每秒请求数**: 平均每秒处理的请求数量
- **P95响应时间**: 95%的请求响应时间

### 业务指标
- **登录成功率**: 登录请求成功比例
- **注册转化率**: 注册请求完成比例
- **并发用户数**: 同时在线用户数量
- **活跃会话数**: 当前活跃的用户会话数

## 告警规则配置

### 默认告警规则
- **高CPU使用率**: CPU > 80%，连续2次检查失败
- **高内存使用率**: 内存 > 85%，连续1次检查失败
- **高错误率**: 错误率 > 5%，连续1次检查失败
- **高响应时间**: 平均响应时间 > 1秒，连续3次检查失败

### 自定义告警规则
支持通过配置文件或API动态添加自定义告警规则，包括：
- 指标名称和阈值
- 检查条件和连续失败次数
- 告警级别和抑制时间
- 通知渠道和接收人

## 使用示例

### 获取系统状态
```bash
curl -H "Authorization: Bearer <token>" \
     http://localhost:5000/api/monitoring/health
```

### 查看性能趋势
```bash
curl -H "Authorization: Bearer <token>" \
     "http://localhost:5000/api/monitoring/metrics/trends?hours=12"
```

### 解决告警
```bash
curl -X POST \
     -H "Authorization: Bearer <token>" \
     -H "Content-Type: application/json" \
     -d '{"resolvedBy": "DevOps", "resolution": "问题已修复"}' \
     http://localhost:5000/api/monitoring/alerts/alert_123/resolve
```
