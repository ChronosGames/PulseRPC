using Microsoft.Extensions.Logging;
using PulseRPC.Protocol;

namespace PulseRPC.Server;

/// <summary>
/// 本地 Service 调用器 - 零拷贝优化
/// </summary>
/// <remarks>
/// 本地调用不需要序列化，直接传递对象引用到目标 Service 的消息队列
/// </remarks>
public sealed class LocalServiceInvoker
{
    private readonly ServiceLocator _serviceLocator;
    private readonly ILogger<LocalServiceInvoker> _logger;

    public LocalServiceInvoker(
        ServiceLocator serviceLocator,
        ILogger<LocalServiceInvoker> logger)
    {
        _serviceLocator = serviceLocator ?? throw new ArgumentNullException(nameof(serviceLocator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 本地调用（无返回值）
    /// </summary>
    /// <param name="targetPID">目标 Service PID</param>
    /// <param name="protocolId">协议号</param>
    /// <param name="args">参数（对象引用，零拷贝）</param>
    /// <param name="callerContext">调用者认证上下文</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task InvokeLocalAsync(
        PID targetPID,
        ProtocolId protocolId,
        object?[] args,
        ServiceAuthenticationContext callerContext,
        CancellationToken cancellationToken = default)
    {
        var targetService = _serviceLocator.GetServiceByPID(targetPID);

        if (targetService == null)
        {
            _logger.LogError(
                "Local service not found - TargetPID: {TargetPID}, ProtocolId: {ProtocolId}",
                targetPID, protocolId);
            throw new InvalidOperationException($"Service not found: {targetPID}");
        }

        // IService 接口没有 InvokeAsync 方法，需要转换为 BaseService
        if (targetService is not BaseService baseService)
        {
            _logger.LogError(
                "Target service does not inherit from BaseService - TargetPID: {TargetPID}",
                targetPID);
            throw new InvalidOperationException($"Service does not inherit from BaseService: {targetPID}");
        }

        _logger.LogDebug(
            "Local invoke (zero-copy) - Target: {TargetPID}, Protocol: {ProtocolId}, Caller: {CallerPID}",
            targetPID, protocolId, callerContext.ServicePID);

        // 零拷贝：直接传递对象引用，不序列化
        // 通过 ServiceAuthenticationContextProvider 传递认证上下文
        using (ServiceAuthenticationContextProvider.SetContext(callerContext))
        {
            await baseService.InvokeAsync(protocolId, args, cancellationToken);
        }
    }

    /// <summary>
    /// 本地调用（有返回值）
    /// </summary>
    /// <typeparam name="TResult">返回值类型</typeparam>
    /// <param name="targetPID">目标 Service PID</param>
    /// <param name="protocolId">协议号</param>
    /// <param name="args">参数（对象引用，零拷贝）</param>
    /// <param name="callerContext">调用者认证上下文</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>返回值</returns>
    public async Task<TResult> InvokeLocalAsync<TResult>(
        PID targetPID,
        ProtocolId protocolId,
        object?[] args,
        ServiceAuthenticationContext callerContext,
        CancellationToken cancellationToken = default)
    {
        var targetService = _serviceLocator.GetServiceByPID(targetPID);

        if (targetService == null)
        {
            _logger.LogError(
                "Local service not found - TargetPID: {TargetPID}, ProtocolId: {ProtocolId}",
                targetPID, protocolId);
            throw new InvalidOperationException($"Service not found: {targetPID}");
        }

        // IService 接口没有 InvokeAsync 方法，需要转换为 BaseService
        if (targetService is not BaseService baseService)
        {
            _logger.LogError(
                "Target service does not inherit from BaseService - TargetPID: {TargetPID}",
                targetPID);
            throw new InvalidOperationException($"Service does not inherit from BaseService: {targetPID}");
        }

        _logger.LogDebug(
            "Local invoke with result (zero-copy) - Target: {TargetPID}, Protocol: {ProtocolId}, Caller: {CallerPID}",
            targetPID, protocolId, callerContext.ServicePID);

        // 零拷贝：直接传递对象引用
        using (ServiceAuthenticationContextProvider.SetContext(callerContext))
        {
            return await baseService.InvokeAsync<TResult>(protocolId, args, cancellationToken);
        }
    }

    /// <summary>
    /// 检查 Service 是否在本地
    /// </summary>
    /// <param name="targetPID">目标 PID</param>
    /// <returns>是否本地</returns>
    public bool IsLocalService(PID targetPID)
    {
        return _serviceLocator.GetServiceByPID(targetPID) != null;
    }
}
