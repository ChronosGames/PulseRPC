# PulseRPC 源代码生成器

PulseRPC.Generators 是一个源代码生成器，它可以自动为 PulseRPC 服务接口和 Hub 接口生成客户端代理实现。这样可以避免手动编写重复的代理代码，减少错误，并提高开发效率。

## 功能特点

- 在编译时自动生成代码，而不是运行时
- 基于接口定义生成具体实现代码
- 支持服务接口（类似 WCF/RESTful API）
- 支持 Hub 接口（类似 SignalR/Socket.io）
- 与 Unity 项目无缝集成
- 支持不同的序列化方式，默认使用 MemoryPack

## 安装

### 在标准 .NET 项目中使用

将以下包引用添加到您的项目文件中：

```xml
<ItemGroup>
  <PackageReference Include="PulseRPC.Generators" Version="1.0.0" PrivateAssets="all" />
</ItemGroup>
```

### 在 Unity 项目中使用

1. 将 `PulseRPC.Client.Unity.Generator.dll` 添加到 Unity 项目的 `Assets/Plugins` 目录中
2. 确保 Unity 项目中已经引用了 `PulseRPC.Core` 和 `PulseRPC.Client.Unity`

## 用法

### 1. 定义服务接口

在共享代码中定义您的服务接口，确保它继承自 `IPulseService<T>`：

```csharp
using System.Threading;
using System.Threading.Tasks;
using PulseRPC;

namespace MyApp.Shared
{
    // 定义服务接口，作为客户端和服务器的通信协议
    public interface ICalculatorService : IPulseService<ICalculatorService>
    {
        Task<int> AddAsync(int a, int b, CancellationToken cancellationToken = default);
        Task<int> SubtractAsync(int a, int b, CancellationToken cancellationToken = default);
        Task<int> MultiplyAsync(int a, int b, CancellationToken cancellationToken = default);
        Task<double> DivideAsync(int a, int b, CancellationToken cancellationToken = default);
    }
}
```

### 2. 定义 Hub 接口

如果您需要双向通信（例如，服务器可以主动推送消息给客户端），可以定义 Hub 接口和接收器接口：

```csharp
using System.Threading;
using System.Threading.Tasks;
using PulseRPC;

namespace MyApp.Shared
{
    // Hub 接口定义服务器可以接收的方法
    public interface IChatHub : IPulseHub<IChatHub, IChatReceiver>
    {
        Task SendMessageAsync(string message, CancellationToken cancellationToken = default);
        Task JoinRoomAsync(string roomName, CancellationToken cancellationToken = default);
        Task LeaveRoomAsync(string roomName, CancellationToken cancellationToken = default);
    }

    // 接收器接口定义客户端可以接收的事件
    public interface IChatReceiver
    {
        Task OnMessageReceivedAsync(string user, string message);
        Task OnUserJoinedAsync(string user, string roomName);
        Task OnUserLeftAsync(string user, string roomName);
    }
}
```

### 3. 在客户端使用生成的代理

源代码生成器会自动为您的接口生成客户端代理实现。您可以使用 `PulseClientFactory` 创建这些代理：

```csharp
// 创建连接
var connection = new PulseWebSocketConnection("ws://localhost:5000/pulse");
await connection.ConnectAsync();

// 创建服务客户端
var calculatorService = PulseClientFactory.Create<ICalculatorService>(connection);

// 调用远程方法
int sum = await calculatorService.AddAsync(10, 20);
Console.WriteLine($"10 + 20 = {sum}");

// 创建并使用Hub客户端
var chatReceiver = new MyChatReceiver(); // 实现 IChatReceiver 的类
var chatHub = PulseClientFactory.ConnectToHub<IChatHub, IChatReceiver>(connection, chatReceiver);

// 调用Hub方法
await chatHub.JoinRoomAsync("general");
await chatHub.SendMessageAsync("Hello, everyone!");
```

## 源代码生成器是如何工作的

1. 在编译时，源代码生成器会扫描您的代码中所有继承自 `IPulseService<T>` 和 `IPulseHub<,>` 的接口
2. 对于找到的每个接口，它会生成一个客户端代理类
3. 这些生成的类会实现接口方法，处理序列化/反序列化和网络通信
4. 生成的代码会放在 `PulseRPC.Client.Generated` 命名空间中

## 调试源代码生成器

如果您需要调试源代码生成器，可以在项目文件中添加以下设置：

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
  <CompilerGeneratedFilesOutputPath>$(BaseIntermediateOutputPath)Generated</CompilerGeneratedFilesOutputPath>
</PropertyGroup>
```

这将使编译器将生成的源代码保存到指定目录中，以便您可以检查它们。

## 限制和注意事项

- 接口方法必须返回 `Task` 或 `Task<T>`
- 参数和返回类型必须是可序列化的类型
- 不支持泛型方法
- 源代码生成器仅在编译时工作，因此不适用于动态加载的程序集 