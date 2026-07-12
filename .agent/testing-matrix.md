# 测试矩阵

## 基础验证

```bash
dotnet build PulseRPC.sln
```

## 按修改范围选择测试

| 修改范围 | 命令 |
| --- | --- |
| 客户端 | `dotnet test tests/PulseRPC.Client.Tests/PulseRPC.Client.Tests.csproj` |
| 服务端 | `dotnet test tests/PulseRPC.Server.Tests/PulseRPC.Server.Tests.csproj` |
| Source Generator | `dotnet test tests/PulseRPC.SourceGenerator.Tests/PulseRPC.SourceGenerator.Tests.csproj` |
| Infrastructure | `dotnet test tests/PulseRPC.Infrastructure.Tests/PulseRPC.Infrastructure.Tests.csproj` |
| Redis backplane | `dotnet test tests/PulseRPC.Backplane.Redis.Tests/PulseRPC.Backplane.Redis.Tests.csproj` |

## 修改到验证映射

| 任务 | 必跑 |
| --- | --- |
| 文档结构/链接 | 链接搜索 + `dotnet build PulseRPC.sln` |
| 公共 API | build + Client.Tests + Server.Tests + PublicAPI 检查 |
| 包边界 analyzer | SourceGenerator.Tests + Abstractions/Shared build + pack |
| 客户端负载均衡 | Client.Tests + SourceGenerator.Tests + Client PublicAPI 检查 |
| 生成器 | SourceGenerator.Tests + 相关运行时测试 |
| 协议号 | SourceGenerator.Tests + Server.Tests |
| TCP/KCP | Client.Tests + Server.Tests + BenchmarkApp smoke |
| 传输/Actor 并发性能 | `architecture-baseline --smoke`；同机回归使用固定 workload JSON 比较 |
| 集群路由 | Server.Tests + Infrastructure.Tests |
| 认证 | Server.Tests + JwtAuthentication 样例 build |
| 样例 | 对应样例项目 build/run smoke |

## 外部依赖

Redis backplane 和部分集成测试可能需要 Docker/Testcontainers。没有外部依赖时，不要声称这些测试已通过；应说明未运行原因。
