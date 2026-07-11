# 认证与授权

PulseRPC 当前认证模型以连接级身份为主：业务登录或节点握手成功后，把身份写入对应连接的 `AuthenticationContext`，后续同一连接上的 RPC 可以读取该上下文。

## 当前事实

- `IAuthenticationContext` 定义认证身份、角色、声明和时间。
- 服务端通道通过 `SetAuthentication` / `ClearAuthentication` 管理连接身份。
- `[Authorize]` / `[AllowAnonymous]` 等特性由源生成路由表在运行时强制执行。
- `PulseClientBuilder.WithAuthentication(...)` 当前尚未完整接入客户端传输握手，不能假设令牌会自动发送到服务端。

## 推荐模式

1. 登录 Hub 暴露匿名登录方法。
2. 登录成功后服务端生成或验证 token。
3. 服务端把认证上下文写入当前连接。
4. 后续业务 Hub 从 `PulseContext` 或服务端通道读取身份。
5. 用授权注解表达认证、角色、权限和调用来源要求；资源归属等依赖业务数据的规则仍在业务层校验。

## 授权强制链

协议号路由在请求反序列化和 keyed 服务激活前依次执行客户端可见性与授权检查。当前强制支持：

- `[Authorize]` 与具名 `Policy`
- `[AllowAnonymous]`
- `[RequireRole]`、`[RequirePermission]` 和 scope
- `[Internal]`、`[ExternalOnly]`

接口与方法上的要求会合并；`[AllowAnonymous]` 只取消隐式“必须已认证”，不会移除显式角色、权限、policy 或来源限制。具名 policy 通过 `IPulseAuthorizationPolicyEvaluator` 求值，未注册 evaluator 时拒绝调用。

多节点 Gateway 调用使用 node wire v2 传播完整 `ClaimsPrincipal`、权限、角色和过期时间。接收节点不会收到原始用户 token；未协商 claims 能力或快照已过期时调用会 fail closed。

## 示例

参考 [JwtAuthentication 示例](../../samples/JwtAuthentication/)。

## 安全边界

- 客户端用户认证和节点间认证分开设计。
- 不要把业务用户 token 当作节点认证密钥。
- 注解适合方法级静态要求；租户、资源归属和实时风控等数据相关规则仍需业务代码强制校验。
- 节点 claims 传播依赖已认证节点信任边界，并不替代线路加密；生产节点 TCP 必须置于私网和 mTLS/TLS 保护下。

## 相关文档

- [客户端和服务端使用指南](client-server.md)
- [集群与路由](../concepts/clustering-and-routing.md)
