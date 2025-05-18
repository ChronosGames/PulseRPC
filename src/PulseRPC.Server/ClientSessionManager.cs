using System.Collections.Concurrent;
using System.Reflection;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PulseRPC.Network;

namespace PulseRPC.Server;

/// <summary>
/// 会话管理器接口 - 用于管理客户端会话和通知发送
/// </summary>
public interface IClientSessionManager
{
    /// <summary>
    /// 注册客户端会话
    /// </summary>
    /// <param name="clientId">客户端ID</param>
    /// <param name="session">网络会话</param>
    void RegisterSession(string clientId, NetworkSession session);

    /// <summary>
    /// 注销客户端会话
    /// </summary>
    /// <param name="clientId">客户端ID</param>
    bool UnregisterSession(string clientId);

    /// <summary>
    /// 获取流式接收器代理
    /// </summary>
    /// <typeparam name="TReceiver">接收器类型</typeparam>
    /// <param name="clientId">客户端ID</param>
    /// <returns>接收器代理</returns>
    TReceiver GetReceiver<TReceiver>(string clientId) where TReceiver : class, IStreamingReceiver;

    /// <summary>
    /// 获取流式接收器代理（针对特定组）
    /// </summary>
    /// <typeparam name="TReceiver">接收器类型</typeparam>
    /// <param name="groupId">组ID</param>
    /// <returns>接收器代理</returns>
    TReceiver GetGroupReceiver<TReceiver>(string groupId) where TReceiver : class, IStreamingReceiver;

    /// <summary>
    /// 获取流式接收器代理（针对所有客户端）
    /// </summary>
    /// <typeparam name="TReceiver">接收器类型</typeparam>
    /// <returns>接收器代理</returns>
    TReceiver GetAllReceiver<TReceiver>() where TReceiver : class, IStreamingReceiver;

    /// <summary>
    /// 将客户端添加到组
    /// </summary>
    /// <param name="clientId">客户端ID</param>
    /// <param name="groupId">组ID</param>
    void AddClientToGroup(string clientId, string groupId);

    /// <summary>
    /// 将客户端从组中移除
    /// </summary>
    /// <param name="clientId">客户端ID</param>
    /// <param name="groupId">组ID</param>
    void RemoveClientFromGroup(string clientId, string groupId);

    /// <summary>
    /// 获取在线客户端数量
    /// </summary>
    /// <returns>客户端数量</returns>
    int GetOnlineClientCount();

    /// <summary>
    /// 获取组中客户端数量
    /// </summary>
    /// <param name="groupId">组ID</param>
    /// <returns>客户端数量</returns>
    int GetGroupClientCount(string groupId);

    /// <summary>
    /// 获取组列表
    /// </summary>
    /// <returns>组ID列表</returns>
    IEnumerable<string> GetGroups();

    /// <summary>
    /// 获取客户端ID列表
    /// </summary>
    /// <returns>客户端ID列表</returns>
    IEnumerable<string> GetClientIds();

    /// <summary>
    /// 获取组中的客户端ID列表
    /// </summary>
    /// <param name="groupId">组ID</param>
    /// <returns>客户端ID列表</returns>
    IEnumerable<string> GetGroupClientIds(string groupId);

    /// <summary>
    /// 检查客户端是否在线
    /// </summary>
    /// <param name="clientId">客户端ID</param>
    /// <returns>是否在线</returns>
    bool IsClientOnline(string clientId);
}

/// <summary>
/// 客户端会话管理器 - 管理客户端会话和通知发送
/// </summary>
public class ClientSessionManager : IClientSessionManager
{
private readonly ILogger<ClientSessionManager> _logger;
    private readonly IPulseService _pulseService;

    // 客户端会话映射
    private readonly ConcurrentDictionary<string, NetworkSession> _sessions = new();

    // 组到客户端映射
    private readonly ConcurrentDictionary<string, HashSet<string>> _groups = new();

    // 客户端所属的组
    private readonly ConcurrentDictionary<string, HashSet<string>> _clientGroups = new();

    // 接收器代理缓存
    private readonly ConcurrentDictionary<Type, ConcurrentDictionary<string, object>> _receiverProxies = new();

    // 组接收器代理缓存
    private readonly ConcurrentDictionary<Type, ConcurrentDictionary<string, object>> _groupReceiverProxies = new();

    // 全局接收器代理缓存
    private readonly ConcurrentDictionary<Type, object> _allReceiverProxies = new();

    /// <summary>
    /// 构造函数
    /// </summary>
    public ClientSessionManager(ILogger<ClientSessionManager> logger, IPulseService pulseService)
    {
        _logger = logger;
        _pulseService = pulseService;
    }

    /// <summary>
    /// 注册客户端会话
    /// </summary>
    public void RegisterSession(string clientId, NetworkSession session)
    {
        if (string.IsNullOrEmpty(clientId))
            throw new ArgumentNullException(nameof(clientId));

        if (session == null)
            throw new ArgumentNullException(nameof(session));

        _sessions[clientId] = session;

        // 监听连接断开
        session.Disconnected += OnSessionDisconnected;

        // 添加元数据
        session.SetItem("ClientId", clientId);

        _logger.LogInformation($"客户端 {clientId} 已注册");
    }

    /// <summary>
    /// 会话断开处理
    /// </summary>
    private void OnSessionDisconnected(NetworkSession session, Exception exception)
    {
        if (session.TryGetItem<string>("ClientId", out var clientId))
        {
            UnregisterSession(clientId);
        }
    }

    /// <summary>
    /// 注销客户端会话
    /// </summary>
    public bool UnregisterSession(string? clientId)
    {
        if (string.IsNullOrEmpty(clientId))
            throw new ArgumentNullException(nameof(clientId));

        if (_sessions.TryRemove(clientId, out var session))
        {
            // 移除断开事件监听
            session.Disconnected -= OnSessionDisconnected;

            // 从所有组中移除
            if (_clientGroups.TryRemove(clientId, out var groups))
            {
                foreach (var groupId in groups)
                {
                    if (_groups.TryGetValue(groupId, out var clients))
                    {
                        clients.Remove(clientId);
                    }
                }
            }

            _logger.LogInformation($"客户端 {clientId} 已注销");
            return true;
        }

        return false;
    }

    /// <summary>
    /// 获取流式接收器代理
    /// </summary>
    public TReceiver GetReceiver<TReceiver>(string clientId) where TReceiver : class, IStreamingReceiver
    {
        if (string.IsNullOrEmpty(clientId))
            throw new ArgumentNullException(nameof(clientId));

        var receiverType = typeof(TReceiver);

        // 获取或创建接收器代理字典
        var receivers = _receiverProxies.GetOrAdd(receiverType, _ => new ConcurrentDictionary<string, object>());

        // 获取或创建接收器代理
        return (TReceiver)receivers.GetOrAdd(clientId, _ => CreateReceiverProxy<TReceiver>(clientId));
    }

    /// <summary>
    /// 获取流式接收器代理（针对特定组）
    /// </summary>
    public TReceiver GetGroupReceiver<TReceiver>(string groupId) where TReceiver : class, IStreamingReceiver
    {
        if (string.IsNullOrEmpty(groupId))
            throw new ArgumentNullException(nameof(groupId));

        var receiverType = typeof(TReceiver);

        // 获取或创建组接收器代理字典
        var groupReceivers = _groupReceiverProxies.GetOrAdd(receiverType, _ => new ConcurrentDictionary<string, object>());

        // 获取或创建组接收器代理
        return (TReceiver)groupReceivers.GetOrAdd(groupId, _ => CreateGroupReceiverProxy<TReceiver>(groupId));
    }

    /// <summary>
    /// 获取流式接收器代理（针对所有客户端）
    /// </summary>
    public TReceiver GetAllReceiver<TReceiver>() where TReceiver : class, IStreamingReceiver
    {
        var receiverType = typeof(TReceiver);

        // 获取或创建全局接收器代理
        return (TReceiver)_allReceiverProxies.GetOrAdd(receiverType, _ => CreateAllReceiverProxy<TReceiver>());
    }

    /// <summary>
    /// 创建接收器代理
    /// </summary>
    private TReceiver CreateReceiverProxy<TReceiver>(string clientId) where TReceiver : class, IStreamingReceiver
    {
        // 使用动态代理创建接收器代理
        return (DispatchProxy.Create<TReceiver, ReceiverProxy<TReceiver>>() is ReceiverProxy<TReceiver> proxy
            ? proxy.Initialize(this, clientId) as TReceiver
            : throw new InvalidOperationException($"无法创建接收器代理: {typeof(TReceiver).Name}"))!;
    }

    /// <summary>
    /// 创建组接收器代理
    /// </summary>
    private TReceiver CreateGroupReceiverProxy<TReceiver>(string groupId) where TReceiver : class, IStreamingReceiver
    {
        // 使用动态代理创建组接收器代理
        return (DispatchProxy.Create<TReceiver, GroupReceiverProxy<TReceiver>>() is GroupReceiverProxy<TReceiver> proxy
            ? proxy.Initialize(this, groupId) as TReceiver
            : throw new InvalidOperationException($"无法创建组接收器代理: {typeof(TReceiver).Name}"))!;
    }

    /// <summary>
    /// 创建全局接收器代理
    /// </summary>
    private TReceiver CreateAllReceiverProxy<TReceiver>() where TReceiver : class, IStreamingReceiver
    {
        // 使用动态代理创建全局接收器代理
        return (DispatchProxy.Create<TReceiver, AllReceiverProxy<TReceiver>>() is AllReceiverProxy<TReceiver> proxy
            ? proxy.Initialize(this) as TReceiver
            : throw new InvalidOperationException($"无法创建全局接收器代理: {typeof(TReceiver).Name}"))!;
    }

    /// <summary>
    /// 将客户端添加到组
    /// </summary>
    public void AddClientToGroup(string clientId, string groupId)
    {
        if (string.IsNullOrEmpty(clientId))
            throw new ArgumentNullException(nameof(clientId));

        if (string.IsNullOrEmpty(groupId))
            throw new ArgumentNullException(nameof(groupId));

        // 检查客户端是否存在
        if (!_sessions.ContainsKey(clientId))
        {
            _logger.LogWarning($"客户端 {clientId} 不存在，无法添加到组 {groupId}");
            return;
        }

        // 获取或创建组
        var clients = _groups.GetOrAdd(groupId, _ => new HashSet<string>());

        // 获取或创建客户端的组列表
        var groups = _clientGroups.GetOrAdd(clientId, _ => new HashSet<string>());

        // 添加客户端到组
        lock (clients)
        {
            clients.Add(clientId);
        }

        // 添加组到客户端的组列表
        lock (groups)
        {
            groups.Add(groupId);
        }

        _logger.LogDebug($"客户端 {clientId} 已添加到组 {groupId}");
    }

    /// <summary>
    /// 将客户端从组中移除
    /// </summary>
    public void RemoveClientFromGroup(string clientId, string groupId)
    {
        if (string.IsNullOrEmpty(clientId))
            throw new ArgumentNullException(nameof(clientId));

        if (string.IsNullOrEmpty(groupId))
            throw new ArgumentNullException(nameof(groupId));

        // 检查组是否存在
        if (_groups.TryGetValue(groupId, out var clients))
        {
            // 从组中移除客户端
            lock (clients)
            {
                clients.Remove(clientId);
            }
        }

        // 检查客户端是否有组信息
        if (_clientGroups.TryGetValue(clientId, out var groups))
        {
            // 从客户端的组列表中移除组
            lock (groups)
            {
                groups.Remove(groupId);
            }
        }

        _logger.LogDebug($"客户端 {clientId} 已从组 {groupId} 中移除");
    }

    /// <summary>
    /// 获取在线客户端数量
    /// </summary>
    public int GetOnlineClientCount()
    {
        return _sessions.Count;
    }

    /// <summary>
    /// 获取组中客户端数量
    /// </summary>
    public int GetGroupClientCount(string groupId)
    {
        if (string.IsNullOrEmpty(groupId))
            throw new ArgumentNullException(nameof(groupId));

        if (_groups.TryGetValue(groupId, out var clients))
        {
            return clients.Count;
        }

        return 0;
    }

    /// <summary>
    /// 获取组列表
    /// </summary>
    public IEnumerable<string> GetGroups()
    {
        return _groups.Keys.ToArray();
    }

    /// <summary>
    /// 获取客户端ID列表
    /// </summary>
    public IEnumerable<string> GetClientIds()
    {
        return _sessions.Keys.ToArray();
    }

    /// <summary>
    /// 获取组中的客户端ID列表
    /// </summary>
    public IEnumerable<string> GetGroupClientIds(string groupId)
    {
        if (string.IsNullOrEmpty(groupId))
            throw new ArgumentNullException(nameof(groupId));

        if (_groups.TryGetValue(groupId, out var clients))
        {
            lock (clients)
            {
                return clients.ToArray();
            }
        }

        return Array.Empty<string>();
    }

    /// <summary>
    /// 检查客户端是否在线
    /// </summary>
    public bool IsClientOnline(string clientId)
    {
        if (string.IsNullOrEmpty(clientId))
            throw new ArgumentNullException(nameof(clientId));

        return _sessions.ContainsKey(clientId);
    }

    /// <summary>
    /// 向客户端发送通知
    /// </summary>
    internal async Task SendToClientAsync<T>(string clientId, T message) where T : IMemoryPackable<T>
    {
        if (string.IsNullOrEmpty(clientId))
            throw new ArgumentNullException(nameof(clientId));

        if (message == null)
            throw new ArgumentNullException(nameof(message));

        if (_sessions.TryGetValue(clientId, out var session))
        {
            try
            {
                var sequenceId = session.GetNextSequenceId();
                await session.SendPacketAsync(message, sequenceId);
                _logger.LogDebug($"已向客户端 {clientId} 发送 {typeof(T).Name} 消息");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"向客户端 {clientId} 发送消息时出错");
            }
        }
        else
        {
            _logger.LogWarning($"客户端 {clientId} 不在线，无法发送消息");
        }
    }

    /// <summary>
    /// 向组中的所有客户端发送通知
    /// </summary>
    internal async Task SendToGroupAsync<T>(string groupId, T message) where T : IMemoryPackable<T>
    {
        if (string.IsNullOrEmpty(groupId))
            throw new ArgumentNullException(nameof(groupId));

        if (message == null)
            throw new ArgumentNullException(nameof(message));

        if (_groups.TryGetValue(groupId, out var clients))
        {
            string[] clientIds;
            lock (clients)
            {
                clientIds = clients.ToArray();
            }

            if (clientIds.Length == 0)
            {
                _logger.LogWarning($"组 {groupId} 中没有客户端，无法发送消息");
                return;
            }

            var tasks = new List<Task>();
            foreach (var clientId in clientIds)
            {
                if (_sessions.TryGetValue(clientId, out var session))
                {
                    try
                    {
                        var sequenceId = session.GetNextSequenceId();
                        tasks.Add(session.SendPacketAsync(message, sequenceId));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"向客户端 {clientId} 发送组消息时出错");
                    }
                }
            }

            if (tasks.Count > 0)
            {
                await Task.WhenAll(tasks);
                _logger.LogDebug($"已向组 {groupId} 中的 {tasks.Count} 个客户端发送 {typeof(T).Name} 消息");
            }
            else
            {
                _logger.LogWarning($"组 {groupId} 中没有在线客户端，无法发送消息");
            }
        }
        else
        {
            _logger.LogWarning($"组 {groupId} 不存在，无法发送消息");
        }
    }

    /// <summary>
    /// 向所有客户端发送通知
    /// </summary>
    public async Task SendToAllAsync<T>(T message) where T : IMemoryPackable<T>
    {
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        var sessions = _sessions.Values.ToArray();
        if (sessions.Length == 0)
        {
            _logger.LogWarning("没有在线客户端，无法发送消息");
            return;
        }

        var tasks = new List<Task>();
        foreach (var session in sessions)
        {
            try
            {
                var sequenceId = session.GetNextSequenceId();
                tasks.Add(session.SendPacketAsync(message, sequenceId));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"向客户端发送全局消息时出错");
            }
        }

        if (tasks.Count > 0)
        {
            await Task.WhenAll(tasks);
            _logger.LogDebug($"已向 {tasks.Count} 个客户端发送 {typeof(T).Name} 消息");
        }
        else
        {
            _logger.LogWarning("没有在线客户端，无法发送消息");
        }
    }
}

/// <summary>
/// 接收器代理基类
/// </summary>
public abstract class ReceiverProxyBase<TReceiver> : DispatchProxy where TReceiver : class, IStreamingReceiver
{
    protected ClientSessionManager? SessionManager { get; private set; }

    /// <summary>
    /// 初始化代理
    /// </summary>
    public object Initialize(ClientSessionManager sessionManager)
    {
        SessionManager = sessionManager;
        return this;
    }

    /// <summary>
    /// 调用处理
    /// </summary>
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod == null)
            throw new ArgumentNullException(nameof(targetMethod));

        var returnType = targetMethod.ReturnType;

        // 检查方法参数
        if (args == null || args.Length == 0 || args[0] == null)
        {
            throw new ArgumentException("接收器方法必须至少有一个参数");
        }

        // 处理返回类型
        if (returnType == typeof(void))
        {
            // 发送通知
            _ = SendNotificationAsync(targetMethod, args!);
            return null;
        }
        else if (returnType == typeof(Task))
        {
            // 异步发送通知，返回任务
            return SendNotificationAsync(targetMethod, args!);
        }
        else
        {
            throw new NotSupportedException($"接收器方法 {targetMethod.Name} 必须返回 void 或 Task");
        }
    }

    /// <summary>
    /// 发送通知 - 派生类实现
    /// </summary>
    protected abstract Task SendNotificationAsync(MethodInfo method, object[] args);

    protected static bool IsMemoryPackable(Type type)
    {
        return type.GetInterfaces()
            .Any(i => i.IsGenericType &&
                      i.GetGenericTypeDefinition() == typeof(IMemoryPackable<>) &&
                      i.GetGenericArguments()[0] == type);
    }
}

/// <summary>
/// 单客户端接收器代理
/// </summary>
public class ReceiverProxy<TReceiver> : ReceiverProxyBase<TReceiver> where TReceiver : class, IStreamingReceiver
{
    private string? _clientId;

    /// <summary>
    /// 初始化代理
    /// </summary>
    public object Initialize(ClientSessionManager sessionManager, string clientId)
    {
        base.Initialize(sessionManager);
        _clientId = clientId;
        return this;
    }

    /// <summary>
    /// 发送通知
    /// </summary>
    protected override async Task SendNotificationAsync(MethodInfo method, object[] args)
    {
        // 获取第一个参数作为消息
        var message = args[0];

        if (IsMemoryPackable(message.GetType()))
        {
            var sendMethod = typeof(ClientSessionManager).GetMethod(nameof(ClientSessionManager.SendToClientAsync))
                ?.MakeGenericMethod(message.GetType());

            if (sendMethod != null)
            {
                await (Task)sendMethod.Invoke(SessionManager, new[] { _clientId, message })!;
            }
            else
            {
                throw new InvalidOperationException($"无法获取发送方法");
            }
        }
        else
        {
            throw new NotSupportedException($"消息类型 {message.GetType().Name} 必须实现 IMemoryPackable 接口");
        }
    }
}

/// <summary>
/// 组接收器代理
/// </summary>
public class GroupReceiverProxy<TReceiver> : ReceiverProxyBase<TReceiver> where TReceiver : class, IStreamingReceiver
{
    private string? _groupId;

    /// <summary>
    /// 初始化代理
    /// </summary>
    public object Initialize(ClientSessionManager sessionManager, string groupId)
    {
        base.Initialize(sessionManager);
        _groupId = groupId;
        return this;
    }

    /// <summary>
    /// 发送通知
    /// </summary>
    protected override async Task SendNotificationAsync(MethodInfo method, object[] args)
    {
        // 获取第一个参数作为消息
        var message = args[0];

        if (IsMemoryPackable(message.GetType()))
        {
            var sendMethod = typeof(ClientSessionManager).GetMethod(nameof(ClientSessionManager.SendToGroupAsync))
                ?.MakeGenericMethod(message.GetType());

            if (sendMethod != null)
            {
                await (Task)sendMethod.Invoke(SessionManager, new[] { _groupId, message })!;
            }
            else
            {
                throw new InvalidOperationException($"无法获取发送方法");
            }
        }
        else
        {
            throw new NotSupportedException($"消息类型 {message.GetType().Name} 必须实现 IMemoryPackable 接口");
        }
    }
}

/// <summary>
/// 全局接收器代理
/// </summary>
public class AllReceiverProxy<TReceiver> : ReceiverProxyBase<TReceiver> where TReceiver : class, IStreamingReceiver
{
    /// <summary>
    /// 发送通知
    /// </summary>
    protected override async Task SendNotificationAsync(MethodInfo method, object[] args)
    {
        // 获取第一个参数作为消息
        var message = args[0];

        if (IsMemoryPackable(message.GetType()))
        {
            var sendMethod = typeof(ClientSessionManager).GetMethod(nameof(ClientSessionManager.SendToAllAsync))
                ?.MakeGenericMethod(message.GetType());

            if (sendMethod != null)
            {
                await (Task)sendMethod.Invoke(SessionManager, new[] { message })!;
            }
            else
            {
                throw new InvalidOperationException($"无法获取发送方法");
            }
        }
        else
        {
            throw new NotSupportedException($"消息类型 {message.GetType().Name} 必须实现 IMemoryPackable 接口");
        }
    }
}

/// <summary>
/// 扩展方法 - 用于依赖注入
/// </summary>
public static class StreamingExtensions
{
    /// <summary>
    /// 注册流式接收器服务
    /// </summary>
    public static IServiceCollection AddStreamingServices(this IServiceCollection services)
    {
        // 注册客户端会话管理器
        services.AddSingleton<IClientSessionManager, ClientSessionManager>();

        return services;
    }
}
