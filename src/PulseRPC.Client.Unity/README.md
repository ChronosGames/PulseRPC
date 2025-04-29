# PulseRPC.Client.Unity

PulseRPC的Unity客户端实现，提供了在Unity环境中使用PulseRPC框架的能力，不依赖于gRPC。这个库使用WebSocket作为传输协议，适用于所有Unity支持的平台，包括iOS、Android、WebGL、Windows和macOS。

## 特性

- 纯C#实现，无需原生插件
- 完全兼容IL2CPP平台
- 支持所有Unity支持的平台
- 基于WebSocket协议，不依赖gRPC
- 支持二进制序列化（使用MemoryPack）
- 支持请求/响应模式和实时事件通知
- 支持Unity主线程回调

## 安装

### 方法1：使用Unity包管理器

1. 在Unity中打开Package Manager (菜单: Window > Package Manager)
2. 点击"+"按钮并选择"Add package from git URL..."
3. 输入此仓库的URL: `https://github.com/YourOrganization/PulseRPC.git?path=src/PulseRPC.Client.Unity`
4. 点击"Add"按钮

### 方法2：手动导入

1. 从此仓库下载最新的发布版本
2. 解压缩文件
3. 将`PulseRPC.Client.Unity`文件夹复制到您的Unity项目的`Assets`目录中

## 快速入门

### 1. 定义服务接口

首先在共享代码中定义服务接口：

```csharp
// 共享代码
using System.Threading;
using System.Threading.Tasks;
using PulseRPC;

public interface ICalculatorService : IPulseService<ICalculatorService>
{
    Task<int> AddAsync(int a, int b, CancellationToken cancellationToken = default);
    Task<int> SubtractAsync(int a, int b, CancellationToken cancellationToken = default);
}
```

### 2. 创建连接

在Unity项目中创建一个到PulseRPC服务器的连接：

```csharp
using PulseRPC.Client.Unity;
using UnityEngine;

public class RPCManager : MonoBehaviour
{
    private PulseWebSocketConnection _connection;
    private ICalculatorService _calculatorService;

    public async void Connect()
    {
        // 创建WebSocket连接
        _connection = new PulseWebSocketConnection("ws://your-server:5000/pulse");

        // 连接到服务器
        await _connection.ConnectAsync();

        // 创建服务客户端
        _calculatorService = PulseClientFactory.Create<ICalculatorService>(_connection);

        Debug.Log("Connected to server");
    }

    public async void TestCalculator()
    {
        try
        {
            // 调用远程方法
            int result = await _calculatorService.AddAsync(10, 20);
            Debug.Log($"10 + 20 = {result}");

            result = await _calculatorService.SubtractAsync(30, 15);
            Debug.Log($"30 - 15 = {result}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"RPC error: {ex.Message}");
        }
    }

    private void OnDestroy()
    {
        // 断开连接
        if (_connection != null && _connection.IsConnected)
        {
            _ = _connection.DisconnectAsync();
        }
    }
}
```

### 3. 使用Hub（实时通讯）

首先定义Hub接口和接收器接口：

```csharp
// 共享代码
using System.Threading;
using System.Threading.Tasks;
using PulseRPC;

public interface IChatHub : IPulseHub<IChatHub, IChatReceiver>
{
    Task SendMessageAsync(string message, CancellationToken cancellationToken = default);
    Task JoinRoomAsync(string roomName, CancellationToken cancellationToken = default);
}

public interface IChatReceiver
{
    Task OnMessageReceived(string user, string message);
    Task OnUserJoined(string user);
    Task OnUserLeft(string user);
}
```

然后在Unity中实现和使用：

```csharp
using PulseRPC.Client.Unity;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

public class ChatClient : MonoBehaviour, IChatReceiver
{
    [SerializeField] private Text chatText;

    private PulseWebSocketConnection _connection;
    private IChatHub _chatHub;

    public async void Connect()
    {
        // 创建WebSocket连接
        _connection = new PulseWebSocketConnection("ws://your-server:5000/pulse");

        // 连接到服务器
        await _connection.ConnectAsync();

        // 连接到Hub
        _chatHub = PulseClientFactory.ConnectToHub<IChatHub, IChatReceiver>(_connection, this);

        // 加入聊天室
        await _chatHub.JoinRoomAsync("general");

        Debug.Log("Connected to chat");
    }

    public async void SendMessage(string message)
    {
        if (_chatHub != null)
        {
            await _chatHub.SendMessageAsync(message);
        }
    }

    // IChatReceiver实现
    public Task OnMessageReceived(string user, string message)
    {
        // 注意：这个回调会在Unity主线程上执行
        chatText.text += $"\n{user}: {message}";
        return Task.CompletedTask;
    }

    public Task OnUserJoined(string user)
    {
        chatText.text += $"\n*** {user} 加入了聊天 ***";
        return Task.CompletedTask;
    }

    public Task OnUserLeft(string user)
    {
        chatText.text += $"\n*** {user} 离开了聊天 ***";
        return Task.CompletedTask;
    }

    private void OnDestroy()
    {
        // 断开连接
        if (_connection != null && _connection.IsConnected)
        {
            _ = _connection.DisconnectAsync();
        }
    }
}
```

## WebGL支持

PulseRPC.Client.Unity自动处理WebGL平台的特殊要求。在WebGL平台上，它使用浏览器的原生WebSocket API，而在其他平台上使用.NET的WebSocket实现。您无需为WebGL平台编写特殊代码，库会自动处理平台差异。

## 高级配置

### 自定义连接选项

```csharp
// 创建带配置的WebSocket连接
var connection = new PulseWebSocketConnection(
    url: "ws://your-server:5000/pulse",
    reconnectAttempts: 3,                      // 重连尝试次数
    reconnectDelay: TimeSpan.FromSeconds(3),   // 重连延迟
    requestTimeout: TimeSpan.FromSeconds(30)   // 请求超时
);
```

### 错误处理

```csharp
try
{
    await _connection.ConnectAsync();
    // ...
}
catch (Exception ex)
{
    Debug.LogError($"连接错误: {ex.Message}");
}

try
{
    var result = await _service.SomeMethodAsync();
    // ...
}
catch (RpcException ex) // 特定RPC异常
{
    Debug.LogError($"RPC错误: {ex.Message}");
}
catch (TimeoutException ex) // 超时异常
{
    Debug.LogError($"请求超时: {ex.Message}");
}
catch (Exception ex) // 其他异常
{
    Debug.LogError($"未知错误: {ex.Message}");
}
```

## 性能考虑

- 使用MemoryPack序列化以获得最佳性能
- 对于大量频繁的消息，考虑批处理处理或使用事件防抖动
- 在WebGL平台上，消息大小可能会影响性能

## 故障排除

### 常见问题

1. **连接问题**
   - 确保服务器URL正确
   - 检查服务器防火墙设置
   - WebGL平台必须使用SSL (wss://) 如果你的页面使用HTTPS

2. **序列化错误**
   - 确保所有类型都正确标记了序列化属性
   - 检查复杂对象的空引用

3. **WebGL相关问题**
   - 确保服务器支持标准WebSocket协议
   - 检查CORS设置（跨域请求）

### 日志

开启详细日志以帮助调试：

```csharp
// 添加到您的初始化代码中
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    Debug.unityLogger.filterLogType = LogType.Log;
#else
    Debug.unityLogger.filterLogType = LogType.Warning;
#endif
```

## 示例

完整示例可在 `Assets/Scripts/PulseRPC/PulseRPCExample.cs` 中找到，展示了如何：

- 连接到服务器
- 调用服务方法
- 使用Hub进行实时通信
- 处理错误和断开连接

## 贡献

欢迎提交问题报告和拉取请求！
