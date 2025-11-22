using Microsoft.Extensions.Logging;
using PulseRPC.Protocol;
using PulseRPC.Serialization;
using System.Buffers;

namespace PulseRPC.Server;

/// <summary>
/// 远程 Service 调用器
/// </summary>
/// <remarks>
/// 用于跨进程/跨节点的 Service 调用，需要序列化参数并通过网络传输
/// </remarks>
public sealed class RemoteServiceInvoker
{
    private readonly ILogger<RemoteServiceInvoker> _logger;
    private readonly ISerializer _serializer;
    private readonly ServiceNodeRegistry? _nodeRegistry;

    public RemoteServiceInvoker(
        ILogger<RemoteServiceInvoker> logger,
        ISerializer serializer,
        ServiceNodeRegistry? nodeRegistry = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _nodeRegistry = nodeRegistry;
    }

    /// <summary>
    /// 远程调用（无返回值）
    /// </summary>
    /// <param name="targetPID">目标 Service PID</param>
    /// <param name="protocolId">协议号</param>
    /// <param name="args">参数（需要序列化）</param>
    /// <param name="callerContext">调用者认证上下文</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task InvokeRemoteAsync(
        PID targetPID,
        ProtocolId protocolId,
        object?[] args,
        IServiceRequestContext callerContext,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "Remote invoke - Target: {TargetPID}, Protocol: {ProtocolId}, Caller: {CallerPID}",
            targetPID, protocolId, callerContext.ServicePID);

        // 查找目标节点
        var targetNode = await FindTargetNodeAsync(targetPID);

        if (targetNode == null)
        {
            _logger.LogError(
                "Target node not found for PID: {TargetPID}",
                targetPID);
            throw new InvalidOperationException($"Target node not found for Service: {targetPID}");
        }

        // 序列化参数
        var serializedArgs = SerializeArguments(args);

        // 构造远程调用请求
        var request = new RemoteServiceCallRequest
        {
            TargetPID = targetPID,
            ProtocolId = protocolId,
            SerializedArgs = serializedArgs,
            CallerPID = callerContext.ServicePID ?? throw new InvalidOperationException("Caller ServicePID is null"),
            CallerToken = callerContext.Token ?? string.Empty,
            CallId = Guid.NewGuid()
        };

        // 发送远程调用请求（通过网络传输）
        await SendRemoteCallAsync(targetNode, request, cancellationToken);

        _logger.LogDebug(
            "Remote invoke completed - CallId: {CallId}, Target: {TargetPID}",
            request.CallId, targetPID);
    }

    /// <summary>
    /// 远程调用（有返回值）
    /// </summary>
    /// <typeparam name="TResult">返回值类型</typeparam>
    /// <param name="targetPID">目标 Service PID</param>
    /// <param name="protocolId">协议号</param>
    /// <param name="args">参数（需要序列化）</param>
    /// <param name="callerContext">调用者认证上下文</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>返回值</returns>
    public async Task<TResult> InvokeRemoteAsync<TResult>(
        PID targetPID,
        ProtocolId protocolId,
        object?[] args,
        IServiceRequestContext callerContext,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "Remote invoke with result - Target: {TargetPID}, Protocol: {ProtocolId}, Caller: {CallerPID}",
            targetPID, protocolId, callerContext.ServicePID);

        // 查找目标节点
        var targetNode = await FindTargetNodeAsync(targetPID);

        if (targetNode == null)
        {
            _logger.LogError(
                "Target node not found for PID: {TargetPID}",
                targetPID);
            throw new InvalidOperationException($"Target node not found for Service: {targetPID}");
        }

        // 序列化参数
        var serializedArgs = SerializeArguments(args);

        // 构造远程调用请求
        var request = new RemoteServiceCallRequest
        {
            TargetPID = targetPID,
            ProtocolId = protocolId,
            SerializedArgs = serializedArgs,
            CallerPID = callerContext.ServicePID ?? throw new InvalidOperationException("Caller ServicePID is null"),
            CallerToken = callerContext.Token ?? string.Empty,
            CallId = Guid.NewGuid(),
            ExpectsResult = true
        };

        // 发送远程调用请求并等待响应
        var response = await SendRemoteCallWithResultAsync(targetNode, request, cancellationToken);

        // 反序列化结果
        var result = DeserializeResult<TResult>(response.SerializedResult);

        _logger.LogDebug(
            "Remote invoke with result completed - CallId: {CallId}, Target: {TargetPID}",
            request.CallId, targetPID);

        return result;
    }

    /// <summary>
    /// 检查 Service 是否是远程的（非本地）
    /// </summary>
    /// <param name="targetPID">目标 PID</param>
    /// <returns>是否远程</returns>
    public bool IsRemoteService(PID targetPID)
    {
        // TODO: 实现节点判断逻辑
        // 可以通过 PID 中的 NodeId 判断是否与当前节点相同
        return false; // 默认返回 false，表示当前未实现远程调用
    }

    // ========== 内部实现 ==========

    /// <summary>
    /// 查找目标 Service 所在的节点
    /// </summary>
    private async Task<ServiceNode?> FindTargetNodeAsync(PID targetPID)
    {
        if (_nodeRegistry == null)
        {
            _logger.LogWarning("ServiceNodeRegistry is not configured, remote calls are not supported");
            return null;
        }

        // 从节点注册表中查找目标 Service 所在的节点
        return await _nodeRegistry.FindNodeByPIDAsync(targetPID);
    }

    /// <summary>
    /// 序列化参数
    /// </summary>
    private byte[][] SerializeArguments(object?[] args)
    {
        var serializedArgs = new byte[args.Length][];
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] != null)
            {
                var buffer = new ArrayBufferWriter<byte>();
                _serializer.Serialize(buffer, args[i]!);
                serializedArgs[i] = buffer.WrittenMemory.ToArray();
            }
            else
            {
                serializedArgs[i] = Array.Empty<byte>();
            }
        }
        return serializedArgs;
    }

    /// <summary>
    /// 反序列化结果
    /// </summary>
    private TResult DeserializeResult<TResult>(byte[] serializedResult)
    {
        if (serializedResult == null || serializedResult.Length == 0)
            return default!;

        var sequence = new ReadOnlySequence<byte>(serializedResult);
        return _serializer.Deserialize<TResult>(sequence);
    }

    /// <summary>
    /// 发送远程调用（无返回值）
    /// </summary>
    private async Task SendRemoteCallAsync(
        ServiceNode targetNode,
        RemoteServiceCallRequest request,
        CancellationToken cancellationToken)
    {
        // TODO: 实现实际的网络传输
        // 这里需要通过 ITransport 或类似组件发送请求到目标节点
        _logger.LogWarning(
            "SendRemoteCallAsync is not fully implemented - Target: {NodeId}",
            targetNode.NodeId);

        await Task.CompletedTask;
    }

    /// <summary>
    /// 发送远程调用并等待结果
    /// </summary>
    private async Task<RemoteServiceCallResponse> SendRemoteCallWithResultAsync(
        ServiceNode targetNode,
        RemoteServiceCallRequest request,
        CancellationToken cancellationToken)
    {
        // TODO: 实现实际的网络传输和响应等待
        _logger.LogWarning(
            "SendRemoteCallWithResultAsync is not fully implemented - Target: {NodeId}",
            targetNode.NodeId);

        return new RemoteServiceCallResponse
        {
            CallId = request.CallId,
            Success = false,
            ErrorMessage = "Remote call not implemented",
            SerializedResult = Array.Empty<byte>()
        };
    }
}

/// <summary>
/// 服务节点信息
/// </summary>
public sealed class ServiceNode
{
    /// <summary>节点 ID</summary>
    public required ushort NodeId { get; init; }

    /// <summary>节点地址</summary>
    public required string Address { get; init; }

    /// <summary>节点端口</summary>
    public required int Port { get; init; }

    /// <summary>是否可用</summary>
    public bool IsAvailable { get; set; } = true;

    public override string ToString() => $"Node[{NodeId}] {Address}:{Port}";
}

/// <summary>
/// 服务节点注册表（用于服务发现）
/// </summary>
public abstract class ServiceNodeRegistry
{
    /// <summary>
    /// 根据 PID 查找目标节点
    /// </summary>
    public abstract Task<ServiceNode?> FindNodeByPIDAsync(PID targetPID);

    /// <summary>
    /// 注册节点
    /// </summary>
    public abstract Task RegisterNodeAsync(ServiceNode node);

    /// <summary>
    /// 注销节点
    /// </summary>
    public abstract Task UnregisterNodeAsync(ushort nodeId);

    /// <summary>
    /// 获取所有节点
    /// </summary>
    public abstract Task<IReadOnlyList<ServiceNode>> GetAllNodesAsync();
}

/// <summary>
/// 远程服务调用请求
/// </summary>
internal sealed class RemoteServiceCallRequest
{
    /// <summary>目标 Service PID</summary>
    public required PID TargetPID { get; init; }

    /// <summary>协议号</summary>
    public required ProtocolId ProtocolId { get; init; }

    /// <summary>序列化后的参数</summary>
    public required byte[][] SerializedArgs { get; init; }

    /// <summary>调用者 PID</summary>
    public required PID CallerPID { get; init; }

    /// <summary>调用者令牌</summary>
    public required string CallerToken { get; init; }

    /// <summary>调用 ID（用于追踪）</summary>
    public required Guid CallId { get; init; }

    /// <summary>是否期望返回值</summary>
    public bool ExpectsResult { get; init; }
}

/// <summary>
/// 远程服务调用响应
/// </summary>
internal sealed class RemoteServiceCallResponse
{
    /// <summary>调用 ID</summary>
    public required Guid CallId { get; init; }

    /// <summary>是否成功</summary>
    public required bool Success { get; init; }

    /// <summary>错误消息</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>序列化后的结果</summary>
    public required byte[] SerializedResult { get; init; }
}
