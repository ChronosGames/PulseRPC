using System;
using System.Threading;
using System.Threading.Tasks;
using PulseRPC.Server.Abstractions;

namespace PulseRPC.Server;

/// <summary>
/// PulseReceiver Hub 基类 - 提供类似 MagicOnion StreamingHub 的服务端推送能力
/// </summary>
/// <remarks>
/// <para>
/// 参考 <see href="https://cysharp.github.io/MagicOnion/streaminghub/call-client">MagicOnion StreamingHub</see> 设计，
/// 提供在 Hub 方法内直接访问客户端和组的能力。
/// </para>
/// <para>
/// <strong>使用示例</strong>：
/// </para>
/// <code>
/// public class GameHub : PulseReceiverHub&lt;IGameReceiver&gt;
/// {
///     private IGroup&lt;IGameReceiver&gt;? _room;
///
///     public async Task JoinRoomAsync(string roomName)
///     {
///         // 加入房间（如果不存在则创建）
///         _room = await Group.AddAsync(roomName);
///
///         // 通知房间内其他人
///         _room.Except(ConnectionId).OnPlayerJoined(playerInfo);
///     }
///
///     public async Task SendMessageAsync(string message)
///     {
///         // 向房间内所有人发送消息
///         _room?.All.OnChatMessage(UserId, message);
///     }
///
///     public async Task WhisperAsync(string targetConnectionId, string message)
///     {
///         // 向单个客户端发送私聊消息
///         Client.OnPrivateMessage(UserId, message);
///         // 或使用 Clients.Single(targetConnectionId).OnPrivateMessage(...)
///     }
/// }
/// </code>
/// </remarks>
/// <typeparam name="TReceiver">客户端接收器接口</typeparam>
[Obsolete("Use PulseHubBase<TReceiver> from PulseRPC.Server.Hubs namespace instead. This class will be removed in a future version.")]
public abstract class PulseReceiverHub<TReceiver> : IDisposable 
    where TReceiver : class, IPulseReceiver
{
    private IHubContext<TReceiver>? _hubContext;
    private IGroupProvider<TReceiver>? _groupProvider;
    private string? _connectionId;
    private string? _userId;
    private bool _disposed;
    
    /// <summary>
    /// 当前连接ID
    /// </summary>
    /// <remarks>
    /// 与 MagicOnion 的 <c>ConnectionId</c> 对应。
    /// </remarks>
    protected string ConnectionId => _connectionId ?? throw new InvalidOperationException("Hub not initialized");
    
    /// <summary>
    /// 当前用户ID（已认证用户）
    /// </summary>
    protected string? UserId => _userId;
    
    /// <summary>
    /// 获取当前连接客户端的代理
    /// </summary>
    /// <remarks>
    /// <para>
    /// 与 MagicOnion 的 <c>this.Client</c> 对应。
    /// </para>
    /// <para>
    /// 用于向当前连接的客户端发送消息：
    /// </para>
    /// <code>
    /// Client.OnMessage("Hello!");
    /// </code>
    /// </remarks>
    protected TReceiver Client => Clients.Single(ConnectionId);
    
    /// <summary>
    /// 获取客户端选择器
    /// </summary>
    /// <remarks>
    /// 提供多种客户端选择方式：
    /// <code>
    /// // 向所有客户端发送
    /// Clients.All.OnMessage("Broadcast!");
    /// 
    /// // 向单个客户端发送
    /// Clients.Single(connectionId).OnMessage("Hello!");
    /// 
    /// // 向多个客户端发送
    /// Clients.Only(connectionIds).OnMessage("Hello team!");
    /// 
    /// // 向除自己外的所有客户端发送
    /// Clients.Except(ConnectionId).OnMessage("Hello others!");
    /// </code>
    /// </remarks>
    protected IHubClients<TReceiver> Clients => 
        _hubContext?.Clients ?? throw new InvalidOperationException("Hub not initialized");
    
    /// <summary>
    /// 获取组提供器
    /// </summary>
    /// <remarks>
    /// <para>
    /// 与 MagicOnion 的 <c>Group</c> 属性对应。
    /// </para>
    /// <para>
    /// 用于创建/获取组实例：
    /// </para>
    /// <code>
    /// // 加入组（如果不存在则创建）
    /// var room = await Group.AddAsync("room-123");
    /// 
    /// // 向组内发送消息
    /// room.All.OnMessage("Hello room!");
    /// </code>
    /// </remarks>
    protected IGroupProvider<TReceiver> Group => 
        _groupProvider ?? throw new InvalidOperationException("Hub not initialized");
    
    /// <summary>
    /// 初始化 Hub（由框架调用）
    /// </summary>
    /// <param name="hubContext">Hub 上下文</param>
    /// <param name="groupProvider">组提供器</param>
    /// <param name="connectionId">连接ID</param>
    /// <param name="userId">用户ID</param>
    internal void Initialize(
        IHubContext<TReceiver> hubContext,
        IGroupProvider<TReceiver> groupProvider,
        string connectionId,
        string? userId)
    {
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
        _groupProvider = groupProvider ?? throw new ArgumentNullException(nameof(groupProvider));
        _connectionId = connectionId ?? throw new ArgumentNullException(nameof(connectionId));
        _userId = userId;
    }
    
    /// <summary>
    /// 当客户端连接时调用
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    protected virtual Task OnConnectedAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
    
    /// <summary>
    /// 当客户端断开连接时调用
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    protected virtual Task OnDisconnectedAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
    
    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        
        Dispose(true);
        GC.SuppressFinalize(this);
        _disposed = true;
    }
    
    /// <summary>
    /// 释放资源
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        // 子类可以重写此方法释放资源
    }
}

