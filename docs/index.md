# PulseRPC 文档索引

本文档索引按读者任务组织。当前事实优先参考 `getting-started/`、`concepts/`、`guides/` 和 `reference/`；历史设计、阶段总结和旧路线图统一放在 `archive/`，只作为背景资料。

## 推荐阅读路径

新用户：

1. [快速开始](getting-started/quickstart.md)
2. [客户端和服务端使用指南](guides/client-server.md)
3. [契约与序列化](guides/contracts-and-serialization.md)
4. [最佳实践](guides/best-practices.md)

Unity 客户端开发者：

1. [Unity 客户端教程](getting-started/unity-client-tutorial.md)
2. [Source Generator 模型](concepts/source-generation.md)
3. [迁移指南](guides/migration.md)

服务端/游戏后端开发者：

1. [RPC 模型](concepts/rpc-model.md)
2. [Actor 模型](concepts/actor-model.md)
3. [服务端运行时](concepts/server-runtime.md)
4. [Actor 服务开发](guides/actor-services.md)
5. [经 Gateway 调用 Actor](guides/gateway-actors.md)
6. [认证与授权](guides/authentication.md)

集群和部署维护者：

1. [架构总览](concepts/architecture.md)
2. [集群与路由](concepts/clustering-and-routing.md)
3. [经 Gateway 调用 Actor](guides/gateway-actors.md)
4. [传输模型](concepts/transport-model.md)
5. [部署指南](guides/deployment.md)
6. [性能指南](guides/performance.md)

维护者：

1. [测试指南](guides/testing.md)
2. [参考手册](reference/index.md)
3. [NuGet 包说明](nuget-readme.md)
4. [变更日志](changelog.md)
5. [历史归档](archive/)
6. [Proto.Actor .NET 架构对比研究](archive/research/proto-actor-dotnet-architecture-analysis.md)
7. [架构优化清单](reference/architecture-optimization-checklist.md)

AI Agent 或自动化维护任务请阅读仓库根目录下的 `.agent/README.md`。

## 文档状态规则

- 当前文档：描述当前源码和可维护 API，可作为使用依据。
- 参考文档：列出稳定事实、配置、协议、术语，不承担教程职责。
- 历史归档：记录方案背景、阶段总结、旧命名或旧路径，不作为当前 API 依据。
- 样例文档：只对对应样例负责；框架通用用法应回链到本目录。

