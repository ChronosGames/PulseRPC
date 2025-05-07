# PulseRPC Unity 客户端

PulseRPC Unity 客户端是一个专为Unity环境优化的RPC客户端库，特别支持IL2CPP和AOT环境。

## 特性

- 支持Unity IL2CPP和AOT编译环境
- 提供值类型的零拷贝序列化
- 基于消息和消息处理器的模式
- 简单易用的Unity组件接口
- 支持异步RPC调用
- 高性能TcpClient通信

## 安装

1. 在Unity项目中导入此包
2. 确保项目中已安装MemoryPack序列化库

## 使用方法

### 1. 添加UnityClient组件

在场景中创建一个GameObject，并添加`UnityClient`组件：

```csharp
// 在Inspector中设置服务器地址和端口
UnityClient client = gameObject.AddComponent<UnityClient>();
client.serverAddress = "127.0.0.1";
client.serverPort = 5000;
```

### 2. 定义消息类型

定义你的请求和响应消息类型，使用`[MemoryPackable]`特性标记：

```csharp
// 请求消息
[MemoryPackable]
public partial class GreetingRequest
{
    public string Name { get; set; }
}

// 响应消息
[MemoryPackable]
public partial class GreetingResponse
{
    public string Message { get; set; }
    public DateTime Timestamp { get; set; }
}

// 值类型消息（使用结构体获得更好的性能）
[MemoryPackable]
public partial struct CalculationRequest
{
    public int A { get; set; }
    public int B { get; set; }
}

[MemoryPackable]
public partial struct CalculationResponse
{
    public int Sum { get; set; }
    public int Product { get; set; }
    public float Division { get; set; }
}
```

### 3. 发送请求和处理响应

```csharp
// 创建请求消息
var request = new GreetingRequest { Name = "Unity" };

// 发送请求并等待响应
try
{
    var response = await client.SendRequest<GreetingRequest, GreetingResponse>(request);
    Debug.Log($"服务器回应: {response.Message}");
}
catch (Exception ex)
{
    Debug.LogError($"RPC调用失败: {ex.Message}");
}
```

### 4. 处理服务器推送的消息（可选）

如果需要处理服务器主动推送的消息，可以注册消息处理器：

```csharp
// 定义消息处理器
public class NotificationHandler : MessageHandler<ServerNotification>
{
    public override void Handle(ServerNotification message)
    {
        Debug.Log($"收到服务器通知: {message.Content}");
    }
}

// 在启动时注册处理器
private void Start()
{
    // 注册消息处理器
    client.RegisterHandler(new NotificationHandler());

    // 连接服务器
    client.Connect();
}
```

## AOT支持

对于IL2CPP环境，库会自动注册所有必要的类型，以确保序列化和反序列化正常工作。这是通过以下方式实现的：

1. 在编译时自动生成AOT注册代码
2. 使用`AOTSupport`类预先注册基本类型
3. 提供特殊的序列化器，支持值类型的零拷贝序列化

## 值类型优化

对于值类型（结构体），PulseRPC Unity客户端使用零拷贝序列化技术，可以显著提高性能：

```csharp
// 定义值类型消息
[MemoryPackable]
public partial struct Vector3Message
{
    public float X;
    public float Y;
    public float Z;
}

// 使用时自动应用零拷贝序列化
var vector = new Vector3Message { X = 1, Y = 2, Z = 3 };
var data = UnityMessageSerializer.Serialize(vector); // 使用零拷贝
```

## 消息与处理器模式

PulseRPC使用消息和消息处理器模式，具有以下优势：

1. **解耦**：消息发送者和接收者之间完全解耦
2. **可扩展**：轻松添加新的消息类型和处理器
3. **类型安全**：基于强类型的消息处理
4. **AOT友好**：适合IL2CPP环境

基本流程：

1. 客户端发送消息到服务器
2. 服务器处理消息并返回响应
3. 客户端接收响应并处理
4. 服务器也可以主动推送消息到客户端

## 故障排除

### IL2CPP编译错误

如果在IL2CPP环境中遇到序列化相关的错误，请确保：

1. 所有消息类型都使用了`[MemoryPackable]`特性
2. 检查是否有需要手动注册的特殊类型
3. 在`AOTSupport.cs`中添加这些特殊类型的注册

### 连接问题

如果无法连接到服务器：

1. 检查服务器地址和端口是否正确
2. 确认服务器是否正在运行
3. 检查是否有防火墙阻止连接
4. 在移动设备上，确保有适当的网络权限

## 示例

请参考`Examples`目录中的示例代码，了解如何使用PulseRPC Unity客户端。
