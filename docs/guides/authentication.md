# 认证与授权

PulseRPC 当前认证模型以连接级身份为主：业务登录或节点握手成功后，把身份写入对应连接的 `AuthenticationContext`，后续同一连接上的 RPC 可以读取该上下文。

## 当前事实

- `IAuthenticationContext` 定义认证身份、角色、声明和时间。
- 服务端通道通过 `SetAuthentication` / `ClearAuthentication` 管理连接身份。
- `[Authorize]` / `[AllowAnonymous]` 等特性会被源生成器捕获用于诊断和元数据。
- `PulseClientBuilder.WithAuthentication(...)` 当前尚未完整接入客户端传输握手，不能假设令牌会自动发送到服务端。

## 推荐模式

1. 登录 Hub 暴露匿名登录方法。
2. 登录成功后服务端生成或验证 token。
3. 服务端把认证上下文写入当前连接。
4. 后续业务 Hub 从 `PulseContext` 或服务端通道读取身份。
5. 对敏感方法仍在业务层显式校验角色、权限和资源归属。

## 示例

参考 [JwtAuthentication 示例](../../samples/JwtAuthentication/)。

## 安全边界

- 客户端用户认证和节点间认证分开设计。
- 不要把业务用户 token 当作节点认证密钥。
- 不要只依赖注解表达安全策略；当前运行时仍需要业务代码强制校验。

## 相关文档

- [客户端和服务端使用指南](client-server.md)
- [集群与路由](../concepts/clustering-and-routing.md)

