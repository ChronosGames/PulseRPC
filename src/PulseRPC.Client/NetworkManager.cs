using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using System.Buffers;
using MemoryPack;
using PulseRPC.Network;

namespace PulseRPC.Client;

/// <summary>
/// 网络节点选项
/// </summary>
public class NodeOptions
{
    /// <summary>
    /// 是否启用自动重连
    /// </summary>
    public bool AutoReconnect { get; set; } = true;

    /// <summary>
    /// 重连间隔
    /// </summary>
    public TimeSpan ReconnectInterval { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// 最大重连次数，0表示无限次
    /// </summary>
    public int MaxReconnectAttempts { get; set; } = 0;

    /// <summary>
    /// 连接超时时间
    /// </summary>
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 空闲超时时间
    /// </summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 发送超时时间
    /// </summary>
    public TimeSpan SendTimeout { get; set; } = TimeSpan.FromSeconds(10);

    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(6);

    /// <summary>
    /// 序列化器工厂
    /// </summary>
    public Func<IPulseRPCSerializer> SerializerFactory { get; set; } = () => new MemoryPackRpcSerializer();
    
    /// <summary>
    /// 发送缓冲区大小
    /// </summary>
    public int SendBufferSize { get; set; } = 262144; // 256KB
    
    /// <summary>
    /// 接收缓冲区大小
    /// </summary>
    public int RecvBufferSize { get; set; } = 262144; // 256KB
    
    /// <summary>
    /// 最大数据包大小
    /// </summary>
    public int MaxPacketSize { get; set; } = 65535; // 64KB - 1
    
    /// <summary>
    /// 压缩阈值
    /// </summary>
    public int CompressionThreshold { get; set; } = 1024; // 1KB
    
    /// <summary>
    /// 请求超时时间(毫秒)
    /// </summary>
    public int RequestTimeout { get; set; } = 10000;
    
    /// <summary>
    /// 转换为NetworkOptions
    /// </summary>
    /// <returns>NetworkOptions对象</returns>
    public NetworkOptions ToNetworkOptions()
    {
        return new NetworkOptions
        {
            CompressionThreshold = CompressionThreshold,
            MaxPacketSize = MaxPacketSize,
            SendBufferSize = SendBufferSize,
            RecvBufferSize = RecvBufferSize,
            RequestTimeout = RequestTimeout,
            SendTimeout = (int)SendTimeout.TotalMilliseconds,
            ReceiveTimeout = (int)IdleTimeout.TotalMilliseconds,
            HeartbeatInterval = (int)HeartbeatInterval.TotalMilliseconds
        };
    }
}

/// <summary>
/// 网络节点信息
/// </summary>
public class NodeInfo
{
    /// <summary>
    /// 节点名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 服务器地址
    /// </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// 服务器端口
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    /// 节点选项
    /// </summary>
    public NodeOptions? Options { get; set; }

    /// <summary>
    /// 网络客户端
    /// </summary>
    public NetworkClient? Client { get; set; }

    /// <summary>
    /// 序列化器
    /// </summary>
    public IPulseRPCSerializer? Serializer { get; set; }
}

/// <summary>
/// 网络管理器，管理多个服务节点连接
/// </summary>
public static class NetworkManager
{
    private static readonly ILogger? _logger;
    private static readonly object _syncLock = new object();

    private static readonly ConcurrentDictionary<string, NodeInfo> _nodes = new();

    // 服务客户端缓存 - 服务类型 -> 客户端类型
    private static readonly ConcurrentDictionary<Type, Type> ServiceClientTypes = new();

    // 接收器处理器缓存 - 接收器类型 -> 处理器类型
    private static readonly Dictionary<Type, Type> ReceiverHandlerTypes = new();

    static NetworkManager()
    {
        // 创建日志工厂和日志记录器
        // _logger = LoggerFactory.CreateLogger(nameof(NetworkManager));

        // _logger = loggerFactory.CreateLogger(nameof(NetworkManager));

        // 扫描程序集，查找服务客户端和接收器处理器
        ScanAssemblies();
    }

    /// <summary>
    /// 注册服务节点
    /// </summary>
    /// <param name="nodeName">节点名称</param>
    /// <param name="host">服务器地址</param>
    /// <param name="port">服务器端口</param>
    /// <param name="options">节点选项</param>
    /// <returns>是否成功注册</returns>
    public static bool RegisterNode(string nodeName, string host, int port, NodeOptions? options = null)
    {
        if (string.IsNullOrEmpty(nodeName))
            throw new ArgumentNullException(nameof(nodeName), "节点名称不能为空");

        if (string.IsNullOrEmpty(host))
            throw new ArgumentNullException(nameof(host), "服务器地址不能为空");

        if (port is <= 0 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), "端口号必须在1-65535范围内");

        options ??= new NodeOptions();

        var nodeInfo = new NodeInfo
        {
            Name = nodeName, Host = host, Port = port, Options = options,
        };

        lock (_syncLock)
        {
            return _nodes.TryAdd(nodeName, nodeInfo);
        }
    }

    /// <summary>
    /// 移除服务节点
    /// </summary>
    /// <param name="nodeName">节点名称</param>
    /// <returns>是否成功移除</returns>
    public static bool RemoveNode(string nodeName)
    {
        if (string.IsNullOrEmpty(nodeName))
            throw new ArgumentNullException(nameof(nodeName), "节点名称不能为空");

        if (_nodes.TryRemove(nodeName, out var nodeInfo))
        {
            try
            {
                nodeInfo.Client?.Dispose();
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "关闭节点 {NodeName} 的连接时出错", nodeName);
            }
        }

        return false;
    }

    /// <summary>
    /// 获取或创建网络客户端
    /// </summary>
    /// <param name="nodeName">节点名称</param>
    /// <returns>网络客户端</returns>
    public static NetworkClient GetOrCreateClient(string nodeName)
    {
        if (string.IsNullOrEmpty(nodeName))
            throw new ArgumentNullException(nameof(nodeName), "节点名称不能为空");

        if (!_nodes.TryGetValue(nodeName, out var nodeInfo))
            throw new KeyNotFoundException($"未找到名为 {nodeName} 的服务节点");

        // 如果客户端已存在，直接返回
        if (nodeInfo.Client != null)
            return nodeInfo.Client;

        // 创建IPulseService实例
        var pulseService = new PulseService();

        // 创建新的网络客户端
        var client = new NetworkClient(
            _logger!,
            nodeInfo.Host,
            nodeInfo.Port,
            pulseService,
            nodeInfo.Options);

        // 配置客户端选项
        ConfigureClient(client, nodeInfo.Options!);

        // 创建序列化器
        nodeInfo.Serializer = nodeInfo.Options!.SerializerFactory();

        // 更新节点信息
        nodeInfo.Client = client;

        // 启动自动重连
        if (nodeInfo.Options.AutoReconnect)
        {
            StartAutoReconnect(nodeInfo);
        }

        return client;
    }

    /// <summary>
    /// 扫描程序集，查找所有生成的客户端和处理器
    /// </summary>
    public static void ScanAssemblies(params Assembly[] assemblies)
    {
        if (assemblies == null || assemblies.Length == 0)
        {
            assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && ShouldScanAssembly(a.GetName().Name!))
                .ToArray();
        }

        foreach (var assembly in assemblies)
        {
            try
            {
                ScanAssembly(assembly);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"扫描程序集 {assembly.GetName().Name} 时出错: {ex.Message}");
            }
        }
    }

    private static bool ShouldScanAssembly(string name)
    {
        // 跳过系统和第三方程序集
        return !name.StartsWith("System.", StringComparison.Ordinal) &&
               !name.StartsWith("Microsoft.", StringComparison.Ordinal) &&
               name != "netstandard" &&
               name != "mscorlib";
    }

    private static void ScanAssembly(Assembly assembly)
    {
        foreach (var type in assembly.GetTypes())
        {
            // 查找服务客户端类型
            if (type.Name.EndsWith("Client") && !type.IsInterface && !type.IsAbstract)
            {
                var interfaces = type.GetInterfaces();
                foreach (var intf in interfaces)
                {
                    if (intf.IsGenericType && intf.GetGenericTypeDefinition() == typeof(IStreamingHub<>))
                    {
                        var serviceType = intf.GetGenericArguments()[0];
                        ServiceClientTypes[serviceType] = type;
                        break;
                    }
                }
            }

            // 查找接收器处理器类型
            if (type.Name.EndsWith("Handler") && !type.IsInterface && !type.IsAbstract)
            {
                var handlerInterfaces = type.GetInterfaces();
                if (handlerInterfaces.Any(i => i.Name == "IMessageHandler"))
                {
                    // 获取处理器支持的接收器类型
                    var receiverTypeProperty = type.GetProperty("ReceiverType");
                    if (receiverTypeProperty != null)
                    {
                        var receiverTypeInstance =
                            Activator.CreateInstance(type.Assembly.GetType($"{type.Namespace}.ReceiverType")!);
                        if (receiverTypeInstance != null)
                        {
                            var receiverInterfaces = receiverTypeInstance.GetType().GetInterfaces();
                            foreach (var intf in receiverInterfaces)
                            {
                                if (intf.IsGenericType &&
                                    intf.GetGenericTypeDefinition() == typeof(IStreamingReceiver<>))
                                {
                                    var receiverType = intf.GetGenericArguments()[0];
                                    ReceiverHandlerTypes[receiverType] = type;
                                    break;
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// 创建服务客户端
    /// </summary>
    /// <typeparam name="TService">服务接口类型</typeparam>
    /// <param name="nodeName">节点名称</param>
    /// <returns>服务客户端</returns>
    public static TService CreateServiceClient<TService>(string nodeName)
        where TService : class, IStreamingHub<TService>
    {
        if (string.IsNullOrEmpty(nodeName))
            throw new ArgumentNullException(nameof(nodeName), "节点名称不能为空");

        if (!_nodes.TryGetValue(nodeName, out var nodeInfo))
            throw new KeyNotFoundException($"未找到名为 {nodeName} 的服务节点");

        // 确保客户端已创建
        var client = GetOrCreateClient(nodeName);

        // 创建服务客户端
        return CreateServiceClient<TService>(client);
    }

    /// <summary>
    /// 注册接收器处理器
    /// </summary>
    /// <typeparam name="TReceiver">接收器接口类型</typeparam>
    /// <param name="nodeName">节点名称</param>
    /// <param name="receiver">接收器实例</param>
    /// <returns>是否成功注册</returns>
    public static bool RegisterReceiverHandler<TReceiver>(string nodeName, TReceiver receiver)
        where TReceiver : class, IStreamingReceiver
    {
        if (string.IsNullOrEmpty(nodeName))
            throw new ArgumentNullException(nameof(nodeName), "节点名称不能为空");

        if (receiver == null)
            throw new ArgumentNullException(nameof(receiver), "接收器实例不能为空");

        if (!_nodes.TryGetValue(nodeName, out var nodeInfo))
            throw new KeyNotFoundException($"未找到名为 {nodeName} 的服务节点");

        // 确保客户端已创建
        var client = GetOrCreateClient(nodeName);

        // 注册接收器处理器
        return RegisterReceiverHandler(client, nodeInfo.Serializer!, receiver);
    }

    private static TService CreateServiceClient<TService>(NetworkClient client)
        where TService : class, IStreamingHub<TService>
    {
        // 查找生成的客户端类型
        var serviceType = typeof(TService);
        if (!ServiceClientTypes.TryGetValue(serviceType, out var clientType))
        {
            throw new InvalidOperationException($"未找到服务类型 {serviceType.Name} 的客户端实现");
        }

        // 创建客户端实例
        try
        {
            return (TService)Activator.CreateInstance(clientType, client)!;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, $"创建服务客户端 {serviceType.Name} 失败");
            throw;
        }
    }

    private static bool RegisterReceiverHandler(NetworkClient client, IPulseRPCSerializer serializer, object receiver)
    {
        // 获取接收器类型
        var receiverType = receiver.GetType();

        // 查找处理器类型
        if (!ReceiverHandlerTypes.TryGetValue(receiverType, out var handlerType))
        {
            _logger?.LogWarning($"未找到接收器类型 {receiverType.Name} 的处理器实现");
            return false;
        }

        // 创建处理器实例
        try
        {
            var handler = Activator.CreateInstance(handlerType, receiver);
            // 注册处理器到客户端会话
            if (client.Session != null)
            {
                // 这里需要实现会话如何处理消息处理器的逻辑
                // client.Session.RegisterHandler(handler);
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, $"创建接收器处理器 {receiverType.Name} 失败");
            return false;
        }
    }

    /// <summary>
    /// 连接所有注册的节点
    /// </summary>
    /// <returns>异步任务</returns>
    public static async Task ConnectAllAsync()
    {
        var tasks = new List<Task>();
        var exceptions = new List<Exception>();

        foreach (var node in _nodes.Values)
        {
            try
            {
                // 确保客户端已创建
                var client = GetOrCreateClient(node.Name);

                // 连接客户端
                tasks.Add(ConnectClientAsync(node));
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"准备连接节点 {node.Name} 时出错");
                exceptions.Add(ex);
            }
        }

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "连接节点时出错");
            exceptions.Add(ex);
        }

        if (exceptions.Count > 0)
        {
            throw new AggregateException("连接节点时发生一个或多个错误", exceptions);
        }
    }

    /// <summary>
    /// 断开所有连接
    /// </summary>
    /// <returns>异步任务</returns>
    public static async Task DisconnectAllAsync()
    {
        var tasks = new List<Task>();

        foreach (var node in _nodes.Values)
        {
            if (node.Client != null)
            {
                tasks.Add(Task.Run(() => node.Client.Dispose()));
            }
        }

        await Task.WhenAll(tasks);
    }

    // 私有方法部分

    private static async Task ConnectClientAsync(NodeInfo nodeInfo)
    {
        if (nodeInfo.Client == null)
            return;

        try
        {
            await nodeInfo.Client.ConnectAsync();
            _logger?.LogInformation("已连接到服务节点 {NodeName} ({Host}:{Port})",
                nodeInfo.Name, nodeInfo.Host, nodeInfo.Port);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "连接服务节点 {NodeName} ({Host}:{Port}) 时出错",
                nodeInfo.Name, nodeInfo.Host, nodeInfo.Port);

            // 如果启用了自动重连，不抛出异常
            if (!nodeInfo.Options?.AutoReconnect ?? false)
                throw;
        }
    }

    private static void ConfigureClient(NetworkClient client, NodeOptions options)
    {
        client.ConnectionTimeout = options.ConnectionTimeout;
        client.IdleTimeout = options.IdleTimeout;
        client.SendTimeout = options.SendTimeout;
    }

    private static void StartAutoReconnect(NodeInfo nodeInfo)
    {
        if (nodeInfo.Client == null)
            return;

        // 添加断开连接事件处理
        nodeInfo.Client.Disconnected += async (sender, e) =>
        {
            _logger?.LogWarning("与服务节点 {NodeName} ({Host}:{Port}) 的连接已断开，准备重连",
                nodeInfo.Name, nodeInfo.Host, nodeInfo.Port);

            int attemptCount = 0;

            while ((nodeInfo.Options?.MaxReconnectAttempts ?? 0) == 0 ||
                   attemptCount < (nodeInfo.Options?.MaxReconnectAttempts ?? 0))
            {
                attemptCount++;

                try
                {
                    // 等待重连间隔
                    await Task.Delay(nodeInfo.Options?.ReconnectInterval ?? TimeSpan.FromSeconds(3));

                    // 尝试重新连接
                    if (nodeInfo.Client?.IsConnected != true)
                    {
                        _logger?.LogInformation("正在重新连接到服务节点 {NodeName} ({Host}:{Port})，第 {Attempt} 次尝试",
                            nodeInfo.Name, nodeInfo.Host, nodeInfo.Port, attemptCount);

                        await nodeInfo.Client?.ConnectAsync()!;

                        _logger?.LogInformation("已重新连接到服务节点 {NodeName} ({Host}:{Port})",
                            nodeInfo.Name, nodeInfo.Host, nodeInfo.Port);

                        // 重连成功，退出循环
                        break;
                    }
                    else
                    {
                        // 已经连接，退出循环
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "重新连接到服务节点 {NodeName} ({Host}:{Port}) 失败，第 {Attempt} 次尝试",
                        nodeInfo.Name, nodeInfo.Host, nodeInfo.Port, attemptCount);
                }
            }

            if (nodeInfo.Client?.IsConnected != true &&
                (nodeInfo.Options?.MaxReconnectAttempts ?? 0) > 0 &&
                attemptCount >= (nodeInfo.Options?.MaxReconnectAttempts ?? 0))
            {
                _logger?.LogError("重新连接到服务节点 {NodeName} ({Host}:{Port}) 失败，已达到最大重试次数 {MaxAttempts}",
                    nodeInfo.Name, nodeInfo.Host, nodeInfo.Port, nodeInfo.Options?.MaxReconnectAttempts);
            }
        };
    }
}

/// <summary>
/// MemoryPack序列化器
/// </summary>
public class MemoryPackRpcSerializer : IPulseRPCSerializer
{
    public void Serialize<T>(IBufferWriter<byte> writer, in T value) where T : IMemoryPackable<T>
    {
        MemoryPackSerializer.Serialize(writer, value);
    }

    public int ProcessMessage(ref ReadOnlySequence<byte> buffer)
    {
        // 简单实现，在实际应用中需要根据具体协议处理
        return (int)buffer.Length;
    }
}
