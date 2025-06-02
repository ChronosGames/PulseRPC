# PulseRPC 配置参考

本文档详细介绍了PulseRPC框架的所有配置选项，包括服务端、客户端、服务发现、负载均衡、监控和链路追踪等组件的配置。

## 📋 目录

- [基础配置](#基础配置)
- [服务端配置](#服务端配置)
- [客户端配置](#客户端配置)
- [服务发现配置](#服务发现配置)
- [负载均衡配置](#负载均衡配置)
- [健康检查配置](#健康检查配置)
- [监控配置](#监控配置)
- [链路追踪配置](#链路追踪配置)
- [日志配置](#日志配置)
- [环境变量](#环境变量)

## 基础配置

### appsettings.json 结构

```json
{
  "Logging": {
    // 日志配置
  },
  "PulseRPC": {
    "Server": {
      // 服务端配置
    },
    "Client": {
      // 客户端配置
    },
    "ServiceDiscovery": {
      // 服务发现配置
    },
    "ServiceRegistration": {
      // 服务注册配置
    },
    "LoadBalancing": {
      // 负载均衡配置
    },
    "HealthCheck": {
      // 健康检查配置
    },
    "Monitoring": {
      // 监控配置
    },
    "Tracing": {
      // 链路追踪配置
    }
  }
}
```

### 配置绑定

```csharp
// 使用IConfiguration
services.AddPulseRpcServer(context.Configuration.GetSection("PulseRPC:Server"));

// 使用委托配置
services.AddPulseRpcServer(options =>
{
    options.Host = "localhost";
    options.Port = 8080;
});

// 使用Options模式
services.Configure<ServerOptions>(context.Configuration.GetSection("PulseRPC:Server"));
```

## 服务端配置

### ServerOptions

```json
{
  "PulseRPC": {
    "Server": {
      "Host": "localhost",
      "Port": 8080,
      "MaxConnections": 1000,
      "KeepAliveInterval": "00:01:00",
      "RequestTimeout": "00:00:30",
      "BacklogSize": 128,
      "ReceiveBufferSize": 8192,
      "SendBufferSize": 8192,
      "TcpNoDelay": true,
      "ReuseAddress": true,
      "Authentication": {
        "Enabled": false,
        "Scheme": "ApiKey",
        "ValidateIssuer": true,
        "ValidateAudience": true
      },
      "Compression": {
        "Enabled": false,
        "Algorithm": "Gzip",
        "Level": "Optimal",
        "MinBytes": 1024
      },
      "Security": {
        "EnableTls": false,
        "CertificatePath": "",
        "CertificatePassword": "",
        "RequireClientCertificate": false
      }
    }
  }
}
```

### 配置说明

| 属性 | 类型 | 默认值 | 说明 |
|-----|------|--------|------|
| `Host` | string | "localhost" | 服务绑定的主机地址 |
| `Port` | int | 8080 | 服务监听端口 |
| `MaxConnections` | int | 1000 | 最大并发连接数 |
| `KeepAliveInterval` | TimeSpan | 00:01:00 | TCP Keep-Alive间隔 |
| `RequestTimeout` | TimeSpan | 00:00:30 | 请求处理超时时间 |
| `BacklogSize` | int | 128 | 监听队列大小 |
| `ReceiveBufferSize` | int | 8192 | 接收缓冲区大小 |
| `SendBufferSize` | int | 8192 | 发送缓冲区大小 |
| `TcpNoDelay` | bool | true | 是否禁用Nagle算法 |
| `ReuseAddress` | bool | true | 是否重用地址 |

### 认证配置

```json
{
  "Authentication": {
    "Enabled": true,
    "Scheme": "Bearer",
    "Authority": "https://your-identity-server.com",
    "Audience": "pulse-rpc-api",
    "ValidateIssuer": true,
    "ValidateAudience": true,
    "ValidateLifetime": true,
    "RequireHttpsMetadata": true,
    "ClockSkew": "00:05:00"
  }
}
```

### TLS配置

```json
{
  "Security": {
    "EnableTls": true,
    "CertificatePath": "/path/to/certificate.pfx",
    "CertificatePassword": "your-password",
    "RequireClientCertificate": false,
    "CheckCertificateRevocation": true,
    "SslProtocols": ["Tls12", "Tls13"]
  }
}
```

## 客户端配置

### ClientOptions

```json
{
  "PulseRPC": {
    "Client": {
      "DefaultTimeout": "00:00:30",
      "RetryCount": 3,
      "RetryInterval": "00:00:01",
      "RetryPolicy": "ExponentialBackoff",
      "MaxRetryInterval": "00:00:30",
      "CircuitBreakerOptions": {
        "Enabled": true,
        "FailureThreshold": 5,
        "RecoveryTimeout": "00:01:00",
        "SamplingDuration": "00:01:00",
        "MinimumThroughput": 10
      },
      "ConnectionPool": {
        "MaxConnections": 100,
        "MaxIdleTime": "00:05:00",
        "ConnectionTimeout": "00:00:30",
        "KeepAliveInterval": "00:01:00",
        "EnableConnectionPooling": true
      },
      "Compression": {
        "Enabled": false,
        "Algorithm": "Gzip",
        "Level": "Optimal",
        "MinBytes": 1024
      },
      "Authentication": {
        "Enabled": false,
        "TokenEndpoint": "https://your-identity-server.com/token",
        "ClientId": "pulse-rpc-client",
        "ClientSecret": "your-client-secret",
        "Scope": "pulse-rpc-api"
      }
    }
  }
}
```

### 重试策略

```json
{
  "RetryPolicy": "ExponentialBackoff",
  "RetryOptions": {
    "ExponentialBackoff": {
      "BaseDelay": "00:00:01",
      "MaxDelay": "00:00:30",
      "Multiplier": 2.0,
      "Jitter": true
    },
    "LinearBackoff": {
      "BaseDelay": "00:00:01",
      "Increment": "00:00:01"
    },
    "FixedInterval": {
      "Interval": "00:00:05"
    }
  }
}
```

### 服务特定配置

```json
{
  "Services": {
    "UserService": {
      "Timeout": "00:00:15",
      "RetryCount": 5,
      "LoadBalancingStrategy": "WeightedRoundRobin",
      "CircuitBreaker": {
        "Enabled": true,
        "FailureThreshold": 3
      }
    },
    "OrderService": {
      "Timeout": "00:01:00",
      "RetryCount": 2,
      "LoadBalancingStrategy": "LeastConnections"
    }
  }
}
```

## 服务发现配置

### ServiceDiscoveryOptions

```json
{
  "PulseRPC": {
    "ServiceDiscovery": {
      "DefaultType": "Consul",
      "RefreshInterval": "00:00:30",
      "EnableCaching": true,
      "CacheTtl": "00:05:00",
      "Consul": {
        "Host": "localhost",
        "Port": 8500,
        "Scheme": "http",
        "Datacenter": "dc1",
        "Token": "",
        "EnableTls": false,
        "Timeout": "00:00:10"
      },
      "Etcd": {
        "Endpoints": ["http://localhost:2379"],
        "Username": "",
        "Password": "",
        "Timeout": "00:00:10",
        "KeepAliveInterval": "00:00:30"
      },
      "Zookeeper": {
        "ConnectionString": "localhost:2181",
        "SessionTimeout": "00:00:30",
        "ConnectionTimeout": "00:00:10",
        "BasePath": "/pulserpc/services"
      },
      "Dns": {
        "Domain": "service.local",
        "Port": 8080,
        "RefreshInterval": "00:01:00"
      },
      "InMemory": {
        "Services": [
          {
            "ServiceName": "UserService",
            "Endpoints": [
              {
                "Host": "localhost",
                "Port": 8080,
                "Weight": 100,
                "Metadata": {
                  "version": "1.0.0",
                  "region": "us-west-1"
                }
              }
            ]
          }
        ]
      }
    }
  }
}
```

### Consul特定配置

```json
{
  "Consul": {
    "Host": "consul.example.com",
    "Port": 8500,
    "Scheme": "https",
    "Datacenter": "dc1",
    "Token": "your-consul-token",
    "EnableTls": true,
    "TlsConfig": {
      "CertificatePath": "/path/to/client.crt",
      "KeyPath": "/path/to/client.key",
      "CaPath": "/path/to/ca.crt",
      "SkipVerify": false
    },
    "QueryOptions": {
      "WaitTime": "00:00:30",
      "Stale": true,
      "RequireConsistent": false
    }
  }
}
```

## 负载均衡配置

### LoadBalancingOptions

```json
{
  "PulseRPC": {
    "LoadBalancing": {
      "DefaultStrategy": "RoundRobin",
      "HealthCheckEnabled": true,
      "UnhealthyThreshold": 3,
      "HealthyThreshold": 2,
      "Strategies": {
        "RoundRobin": {
          "EnableStickySession": false
        },
        "WeightedRoundRobin": {
          "DefaultWeight": 100,
          "WeightUpdateInterval": "00:01:00"
        },
        "LeastConnections": {
          "ConnectionCountWeight": 1.0,
          "ResponseTimeWeight": 0.5
        },
        "Random": {
          "Seed": null
        },
        "ConsistentHash": {
          "VirtualNodes": 150,
          "HashFunction": "SHA1"
        }
      }
    }
  }
}
```

### 策略特定配置

```json
{
  "ServiceSpecificStrategies": {
    "UserService": {
      "Strategy": "ConsistentHash",
      "HashKey": "UserId",
      "Options": {
        "VirtualNodes": 200
      }
    },
    "OrderService": {
      "Strategy": "WeightedRoundRobin",
      "Options": {
        "WeightSource": "Dynamic"
      }
    }
  }
}
```

## 健康检查配置

### HealthCheckOptions

```json
{
  "PulseRPC": {
    "HealthCheck": {
      "Enabled": true,
      "Interval": "00:00:30",
      "Timeout": "00:00:05",
      "FailureThreshold": 3,
      "RecoveryThreshold": 2,
      "GracefulShutdownTimeout": "00:00:30",
      "Checks": [
        {
          "Name": "tcp",
          "Type": "Tcp",
          "Enabled": true,
          "Interval": "00:00:15",
          "Timeout": "00:00:03"
        },
        {
          "Name": "http",
          "Type": "Http",
          "Enabled": false,
          "Url": "/health",
          "Method": "GET",
          "ExpectedStatusCode": 200,
          "Interval": "00:00:30",
          "Timeout": "00:00:05"
        },
        {
          "Name": "custom",
          "Type": "Custom",
          "Enabled": false,
          "Assembly": "MyApp.HealthChecks",
          "TypeName": "MyCustomHealthCheck"
        }
      ]
    }
  }
}
```

### 自定义健康检查

```csharp
public class DatabaseHealthCheck : IHealthCheck
{
    private readonly IDbConnection _connection;

    public DatabaseHealthCheck(IDbConnection connection)
    {
        _connection = connection;
    }

    public async Task<HealthCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _connection.OpenAsync(cancellationToken);
            return HealthCheckResult.Healthy("Database connection successful");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database connection failed", ex);
        }
        finally
        {
            _connection.Close();
        }
    }
}
```

## 监控配置

### MonitoringOptions

```json
{
  "PulseRPC": {
    "Monitoring": {
      "Enabled": true,
      "Performance": {
        "Enabled": true,
        "SamplingInterval": "00:00:10",
        "BufferSize": 1000,
        "FlushInterval": "00:01:00"
      },
      "Metrics": {
        "Enabled": true,
        "Categories": ["rpc", "system", "business"],
        "Exporters": [
          {
            "Type": "Prometheus",
            "Enabled": true,
            "Endpoint": "/metrics",
            "Port": 9090
          },
          {
            "Type": "Console",
            "Enabled": false,
            "Interval": "00:00:30"
          }
        ]
      },
      "System": {
        "EnableCpuMetrics": true,
        "EnableMemoryMetrics": true,
        "EnableDiskMetrics": false,
        "EnableNetworkMetrics": true,
        "SamplingInterval": "00:00:05"
      },
      "Business": {
        "EnableCustomMetrics": true,
        "MetricPrefix": "pulserpc",
        "TagDefaults": {
          "service": "user-service",
          "environment": "production"
        }
      }
    }
  }
}
```

### Prometheus配置

```json
{
  "Prometheus": {
    "Endpoint": "/metrics",
    "Port": 9090,
    "Hostname": "0.0.0.0",
    "CollectDefaultMetrics": true,
    "ScrapeTimeout": "00:00:10",
    "MetricPrefix": "pulserpc_",
    "Labels": {
      "instance": "server-01",
      "region": "us-west-1"
    }
  }
}
```

## 链路追踪配置

### TracingOptions

```json
{
  "PulseRPC": {
    "Tracing": {
      "Enabled": true,
      "ServiceName": "user-service",
      "ServiceVersion": "1.0.0",
      "Environment": "production",
      "SamplingRate": 0.1,
      "ForceTracing": false,
      "MaxSpansPerTrace": 1000,
      "MaxSpanAttributes": 128,
      "MaxSpanEvents": 128,
      "MaxSpanLinks": 128,
      "SpanExpirationTime": "00:05:00",
      "TraceRpcCalls": true,
      "TraceDatabaseOperations": false,
      "TraceHttpRequests": true,
      "TraceMessageQueue": false,
      "RecordExceptions": true,
      "RecordRpcArguments": false,
      "RecordRpcReturnValues": false,
      "MaxArgumentLength": 1024,
      "Exporter": {
        "Type": "Jaeger",
        "Endpoint": "http://localhost:14268",
        "Timeout": "00:00:30",
        "Compression": "Gzip",
        "Headers": {
          "Authorization": "Bearer your-token"
        },
        "Jaeger": {
          "AgentHost": "localhost",
          "AgentPort": 6831,
          "CollectorEndpoint": "http://localhost:14268/api/traces",
          "Username": "",
          "Password": ""
        },
        "Zipkin": {
          "Endpoint": "http://localhost:9411/api/v2/spans",
          "UseShortTraceIds": false
        },
        "Otlp": {
          "Endpoint": "http://localhost:4317",
          "Protocol": "Grpc",
          "UseTls": false,
          "CertificatePath": ""
        }
      },
      "Filter": {
        "Enabled": true,
        "MinDurationThreshold": "00:00:00",
        "MaxDurationThreshold": "00:10:00",
        "FilterSuccessfulOperations": false,
        "FilterErrorOperations": false,
        "AllowedOperations": [],
        "BlockedOperations": ["health_check", "metrics"],
        "AllowedTags": {},
        "BlockedTags": {}
      },
      "Batch": {
        "Enabled": true,
        "BatchSize": 512,
        "BatchTimeout": "00:00:05",
        "MaxQueueSize": 2048,
        "ExportTimeout": "00:00:30"
      },
      "ResourceTags": {
        "cluster": "us-west-2",
        "pod": "user-service-1",
        "version": "v1.0.0"
      },
      "DefaultSpanTags": {
        "team": "platform",
        "component": "api-gateway"
      },
      "IgnoredOperations": [
        "health_check",
        "metrics"
      ],
      "IgnoredUserAgents": [
        "kube-probe",
        "health-checker"
      ]
    }
  }
}
```

### OpenTelemetry集成

```json
{
  "Otlp": {
    "Endpoint": "http://otel-collector:4317",
    "Protocol": "Grpc",
    "UseTls": false,
    "Headers": {
      "api-key": "your-api-key"
    },
    "Compression": "Gzip",
    "Timeout": "00:00:30"
  }
}
```

## 日志配置

### Logging配置

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "PulseRPC": "Debug",
      "PulseRPC.Client": "Information",
      "PulseRPC.Server": "Information",
      "PulseRPC.ServiceDiscovery": "Warning",
      "PulseRPC.LoadBalancing": "Warning",
      "PulseRPC.Monitoring": "Information",
      "PulseRPC.Tracing": "Information",
      "System": "Warning",
      "Microsoft": "Warning"
    },
    "Console": {
      "FormatterName": "simple",
      "LogToStandardErrorThreshold": "Error",
      "IncludeScopes": true,
      "TimestampFormat": "yyyy-MM-dd HH:mm:ss.fff "
    },
    "File": {
      "Path": "logs/pulserpc-.log",
      "RollingInterval": "Day",
      "RetainedFileCountLimit": 30,
      "FileSizeLimitBytes": 104857600,
      "BufferSize": 4096
    },
    "Structured": {
      "Enabled": true,
      "Format": "Json",
      "IncludeFields": ["Timestamp", "Level", "Message", "Exception", "Properties"]
    }
  }
}
```

### Serilog集成

```json
{
  "Serilog": {
    "Using": ["Serilog.Sinks.Console", "Serilog.Sinks.File"],
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "PulseRPC": "Debug"
      }
    },
    "WriteTo": [
      {
        "Name": "Console",
        "Args": {
          "outputTemplate": "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"
        }
      },
      {
        "Name": "File",
        "Args": {
          "path": "logs/pulserpc-.log",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 30
        }
      }
    ],
    "Enrich": ["FromLogContext", "WithMachineName", "WithThreadId"]
  }
}
```

## 环境变量

### 支持的环境变量

| 环境变量 | 配置路径 | 默认值 | 说明 |
|---------|----------|--------|------|
| `PULSERPC_SERVER_HOST` | `PulseRPC:Server:Host` | localhost | 服务端绑定地址 |
| `PULSERPC_SERVER_PORT` | `PulseRPC:Server:Port` | 8080 | 服务端监听端口 |
| `PULSERPC_CONSUL_HOST` | `PulseRPC:ServiceDiscovery:Consul:Host` | localhost | Consul地址 |
| `PULSERPC_CONSUL_PORT` | `PulseRPC:ServiceDiscovery:Consul:Port` | 8500 | Consul端口 |
| `PULSERPC_TRACING_ENABLED` | `PulseRPC:Tracing:Enabled` | false | 是否启用链路追踪 |
| `PULSERPC_TRACING_SAMPLING_RATE` | `PulseRPC:Tracing:SamplingRate` | 0.1 | 追踪采样率 |
| `PULSERPC_JAEGER_ENDPOINT` | `PulseRPC:Tracing:Exporter:Jaeger:AgentHost` | localhost | Jaeger Agent地址 |

### 环境变量覆盖

```bash
# 开发环境
export PULSERPC_SERVER_HOST=0.0.0.0
export PULSERPC_SERVER_PORT=8080
export PULSERPC_TRACING_ENABLED=true
export PULSERPC_TRACING_SAMPLING_RATE=1.0

# 生产环境
export PULSERPC_SERVER_HOST=10.0.0.100
export PULSERPC_SERVER_PORT=80
export PULSERPC_CONSUL_HOST=consul.internal
export PULSERPC_TRACING_ENABLED=true
export PULSERPC_TRACING_SAMPLING_RATE=0.01
export PULSERPC_JAEGER_ENDPOINT=jaeger.internal
```

### Docker配置

```dockerfile
# Dockerfile
ENV PULSERPC_SERVER_HOST=0.0.0.0
ENV PULSERPC_SERVER_PORT=8080
ENV PULSERPC_CONSUL_HOST=consul
ENV PULSERPC_TRACING_ENABLED=true

# docker-compose.yml
version: '3.8'
services:
  user-service:
    image: user-service:latest
    environment:
      - PULSERPC_SERVER_HOST=0.0.0.0
      - PULSERPC_SERVER_PORT=8080
      - PULSERPC_CONSUL_HOST=consul
      - PULSERPC_TRACING_ENABLED=true
      - PULSERPC_JAEGER_ENDPOINT=jaeger
    ports:
      - "8080:8080"
```

### Kubernetes配置

```yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: pulserpc-config
data:
  appsettings.json: |
    {
      "PulseRPC": {
        "Server": {
          "Host": "0.0.0.0",
          "Port": 8080
        }
      }
    }
---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: user-service
spec:
  replicas: 3
  selector:
    matchLabels:
      app: user-service
  template:
    metadata:
      labels:
        app: user-service
    spec:
      containers:
      - name: user-service
        image: user-service:latest
        ports:
        - containerPort: 8080
        env:
        - name: PULSERPC_SERVER_HOST
          value: "0.0.0.0"
        - name: PULSERPC_SERVER_PORT
          value: "8080"
        - name: PULSERPC_CONSUL_HOST
          value: "consul.default.svc.cluster.local"
        volumeMounts:
        - name: config
          mountPath: /app/appsettings.json
          subPath: appsettings.json
      volumes:
      - name: config
        configMap:
          name: pulserpc-config
```

## 配置验证

### 配置验证特性

```csharp
public class ServerOptions
{
    [Required]
    [RegularExpression(@"^[a-zA-Z0-9.-]+$")]
    public string Host { get; set; } = "localhost";

    [Range(1, 65535)]
    public int Port { get; set; } = 8080;

    [Range(1, 10000)]
    public int MaxConnections { get; set; } = 1000;

    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromMinutes(1);
}
```

### 配置验证注册

```csharp
services.AddOptions<ServerOptions>()
    .Bind(configuration.GetSection("PulseRPC:Server"))
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

### 自定义验证

```csharp
services.AddOptions<TracingOptions>()
    .Bind(configuration.GetSection("PulseRPC:Tracing"))
    .Validate(options =>
    {
        if (options.Enabled && options.SamplingRate < 0 || options.SamplingRate > 1)
        {
            return false;
        }
        return true;
    }, "采样率必须在0到1之间");
```

这份配置参考文档涵盖了PulseRPC框架的所有主要配置选项，可以帮助用户根据自己的需求进行精确配置。 