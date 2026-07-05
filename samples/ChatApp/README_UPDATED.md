# ChatApp 旧 API 更新记录

> 文档状态：历史记录。当前 ChatApp 的准确说明请以 [README.md](README.md) 和 [README_CLIENT_APIS.md](README_CLIENT_APIS.md) 为准。

旧版本文档描述的是早期简化 API 方案，包含 `AddPulseRpcServer`、`AddPulseRpcService`、`IPlayerHub`、`PlayerHub` 等当前 ChatApp 主路径不再使用的写法。当前示例服务端使用 `services.AddPulseServer(...)`，业务入口为 `IChatRoomHub` / `ChatRoomHub` / `ChatRoomService`，客户端通过 `PulseClientBuilder` 建立连接并获取生成的 Hub 代理。
