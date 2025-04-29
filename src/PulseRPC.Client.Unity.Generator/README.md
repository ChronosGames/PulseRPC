# PulseRPC Unity 源代码生成器

这个包提供了 PulseRPC 源代码生成器的 Unity 适配版本，使您可以在 Unity 项目中轻松使用自动生成的 PulseRPC 客户端代理。

## 功能特点

- 在编译时自动生成代码，而不是运行时
- 完全支持 IL2CPP 平台
- 针对 Unity 环境优化
- 与 PulseRPC.Client.Unity 无缝集成
- 减少手工编写重复代码

## 安装

1. 在您的 Unity 项目中，将以下文件复制到 `Assets/Plugins` 目录：
   - `PulseRPC.Client.Unity.Generator.dll`
   - `PulseRPC.Generators.dll`
   - `Microsoft.CodeAnalysis.dll`（如果尚未包含）
   - `Microsoft.CodeAnalysis.CSharp.dll`（如果尚未包含）

2. 确保您的项目已经安装 `PulseRPC.Core` 和 `PulseRPC.Client.Unity`

## 使用方法

### 1. 定义服务接口

在共享代码中定义服务接口，确保它继承自 `IPulseService<T>`：

```csharp
using System.Threading;
using System.Threading.Tasks;
using PulseRPC;

// 定义计算器服务接口
public interface ICalculatorService : IPulseService<ICalculatorService>
{
    Task<int> AddAsync(int a, int b, CancellationToken cancellationToken = default);
    Task<int> SubtractAsync(int a, int b, CancellationToken cancellationToken = default);
}
```

### 2. 定义 Hub 接口（可选）

如果需要实时通信，定义 Hub 接口和接收器接口：

```csharp
using System.Threading;
using System.Threading.Tasks;
using PulseRPC;

// 定义聊天Hub接口
public interface IChatHub : IPulseHub<IChatHub, IChatReceiver>
{
    Task SendMessageAsync(string message, CancellationToken cancellationToken = default);
    Task JoinRoomAsync(string roomName, CancellationToken cancellationToken = default);
}

// 定义接收器接口
public interface IChatReceiver
{
    Task OnMessageAsync(string user, string message);
    Task OnUserJoinedAsync(string user);
    Task OnUserLeftAsync(string user);
}
```

### 3. 使用生成的客户端代理

生成器会自动为您的接口创建代理类。您可以在 Unity 脚本中这样使用它们：

```csharp
using UnityEngine;
using PulseRPC.Client.Unity;
using System.Threading.Tasks;

public class NetworkManager : MonoBehaviour, IChatReceiver
{
    private PulseWebSocketConnection _connection;
    private ICalculatorService _calculatorService;
    private IChatHub _chatHub;

    public async void Start()
    {
        // 创建连接
        _connection = new PulseWebSocketConnection("ws://your-server:5000/pulse");
        await _connection.ConnectAsync();

        // 创建服务客户端
        _calculatorService = PulseClientFactory.Create<ICalculatorService>(_connection);

        // 创建Hub客户端
        _chatHub = PulseClientFactory.ConnectToHub<IChatHub, IChatReceiver>(_connection, this);

        // 示例调用
        int result = await _calculatorService.AddAsync(5, 3);
        Debug.Log($"5 + 3 = {result}");

        await _chatHub.JoinRoomAsync("general");
    }

    // 接收器方法实现
    public Task OnMessageAsync(string user, string message)
    {
        Debug.Log($"{user}: {message}");
        return Task.CompletedTask;
    }

    public Task OnUserJoinedAsync(string user)
    {
        Debug.Log($"{user} joined");
        return Task.CompletedTask;
    }

    public Task OnUserLeftAsync(string user)
    {
        Debug.Log($"{user} left");
        return Task.CompletedTask;
    }

    public void OnDestroy()
    {
        _connection?.DisconnectAsync().ConfigureAwait(false);
    }
}
```

## Unity 特有的注意事项

### 1. IL2CPP 兼容性

源代码生成器完全兼容 IL2CPP，因为所有代码都是提前生成的，不依赖于运行时代码生成或反射。

### 2. 线程处理

所有回调方法都会在主线程上执行，因此您可以直接修改 Unity 对象，而不需要使用 `UnityMainThreadDispatcher` 等工具。

### 3. 生成的代码的位置

生成的代码被放置在 `PulseRPC.Client.Generated` 命名空间中，并在编译时自动添加到您的程序集中。您不需要手动包含这些文件。

### 4. 调试生成的代码

如果您想查看生成的代码，可以在 Unity 项目的 `Assembly-CSharp.dll.PulseRPC.generated` 目录中找到它们（如果您启用了生成调试选项）。您可以在项目设置中添加 `PULSERPC_DEBUG` 编译符号以启用此功能。

## 疑难解答

### 生成器未运行

- 确保已正确安装所有必需的DLL
- 检查Unity控制台中的编译错误
- 重新启动Unity编辑器以刷新程序集缓存

### 代理类找不到

- 确保接口正确继承了 `IPulseService<T>` 或 `IPulseHub<THubInterface, TReceiver>`
- 检查命名空间是否匹配
- 尝试在 Unity 编辑器中重新编译项目

## 性能考虑

- 在WebGL项目中，使用较小的消息体积以获得更好的性能
- 对于移动平台，建议启用消息压缩选项
