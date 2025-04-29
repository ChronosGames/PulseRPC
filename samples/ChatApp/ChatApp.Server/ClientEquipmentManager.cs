using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ChatApp.Server;

// 客户端装备操作管理器
public class ClientEquipmentManager {
    private readonly IGameNetworkClient _networkClient;
    private readonly IUserSession _userSession;
    private readonly IClientLogger _logger;

    // 操作队列和排队标志
    private readonly Queue<PendingOperation> _operationQueue = new Queue<PendingOperation>();
    private bool _isProcessingQueue = false;

    // 序列号计数器
    private long _sequenceCounter = 0;

    public ClientEquipmentManager(
        IGameNetworkClient networkClient,
        IUserSession userSession,
        IClientLogger logger) {

        _networkClient = networkClient;
        _userSession = userSession;
        _logger = logger;
    }

    // 请求装备强化
    public async Task<EnhanceResult> RequestEnhanceEquipment(
        Guid equipmentId, int targetLevel, bool useProtection = false) {

        // 创建操作请求
        var request = new EnhanceRequest {
            PlayerId = _userSession.PlayerId,
            EquipmentId = equipmentId,
            TargetLevel = targetLevel,
            UseProtection = useProtection,
            RequestId = Guid.NewGuid().ToString(),
            ClientSequence = GetNextSequence()
        };

        // 创建任务完成源
        var completionSource = new TaskCompletionSource<EnhanceResult>();

        // 加入操作队列
        _operationQueue.Enqueue(new PendingOperation {
            Type = OperationType.Enhance,
            Request = request,
            CompletionSource = completionSource,
            RetryCount = 0,
            LastAttemptTime = null
        });

        // 开始处理队列（如果尚未开始）
        if (!_isProcessingQueue) {
            _ = ProcessOperationQueue();
        }

        // 返回任务，由队列处理机制完成
        return await completionSource.Task;
    }

    // 请求装备分解
    public async Task<DismantleResult> RequestDismantleEquipment(Guid equipmentId) {
        // 类似实现...
        var request = new DismantleRequest {
            PlayerId = _userSession.PlayerId,
            EquipmentId = equipmentId,
            RequestId = Guid.NewGuid().ToString(),
            ClientSequence = GetNextSequence()
        };

        var completionSource = new TaskCompletionSource<DismantleResult>();

        _operationQueue.Enqueue(new PendingOperation {
            Type = OperationType.Dismantle,
            Request = request,
            CompletionSource = completionSource,
            RetryCount = 0,
            LastAttemptTime = null
        });

        if (!_isProcessingQueue) {
            _ = ProcessOperationQueue();
        }

        return await completionSource.Task;
    }

    // 请求装备穿戴
    public async Task<EquipResult> RequestEquipItem(Guid equipmentId, string slotName) {
        // 类似实现...
        var request = new EquipRequest {
            PlayerId = _userSession.PlayerId,
            EquipmentId = equipmentId,
            SlotName = slotName,
            RequestId = Guid.NewGuid().ToString(),
            ClientSequence = GetNextSequence()
        };

        var completionSource = new TaskCompletionSource<EquipResult>();

        _operationQueue.Enqueue(new PendingOperation {
            Type = OperationType.Equip,
            Request = request,
            CompletionSource = completionSource,
            RetryCount = 0,
            LastAttemptTime = null
        });

        if (!_isProcessingQueue) {
            _ = ProcessOperationQueue();
        }

        return await completionSource.Task;
    }

    // 处理操作队列
    private async Task ProcessOperationQueue() {
        _isProcessingQueue = true;

        try {
            while (_operationQueue.Count > 0) {
                var operation = _operationQueue.Peek();

                try {
                    // 尝试执行操作
                    object result = await ExecuteOperation(operation);

                    // 操作成功，从队列移除
                    _operationQueue.Dequeue();

                    // 完成任务
                    switch (operation.Type) {
                        case OperationType.Enhance:
                            ((TaskCompletionSource<EnhanceResult>)operation.CompletionSource)
                                .SetResult((EnhanceResult)result);
                            break;
                        case OperationType.Dismantle:
                            ((TaskCompletionSource<DismantleResult>)operation.CompletionSource)
                                .SetResult((DismantleResult)result);
                            break;
                        case OperationType.Equip:
                            ((TaskCompletionSource<EquipResult>)operation.CompletionSource)
                                .SetResult((EquipResult)result);
                            break;
                    }
                }
                catch (OperationRetryException) {
                    // 需要重试，但暂时不处理队列中的其他请求
                    await Task.Delay(CalculateRetryDelay(operation.RetryCount));
                    continue;
                }
                catch (Exception ex) {
                    // 操作失败，从队列移除并报告错误
                    _operationQueue.Dequeue();

                    _logger.Error($"Operation failed: {ex.Message}");

                    // 设置异常
                    operation.CompletionSource.SetException(ex);
                }
            }
        }
        finally {
            _isProcessingQueue = false;

            // 如果在处理过程中有新请求入队，则继续处理
            if (_operationQueue.Count > 0) {
                _ = ProcessOperationQueue();
            }
        }
    }

    // 执行具体操作
    private async Task<object> ExecuteOperation(PendingOperation operation) {
        operation.LastAttemptTime = DateTime.UtcNow;
        operation.RetryCount++;

        // 重试次数限制
        if (operation.RetryCount > 3) {
            throw new MaxRetriesExceededException("Operation failed after maximum retry attempts");
        }

        try {
            // 根据操作类型发送不同的请求
            switch (operation.Type) {
                case OperationType.Enhance:
                    var enhanceRequest = (EnhanceRequest)operation.Request;
                    return await _networkClient.SendRequest<EnhanceRequest, EnhanceResult>(
                        "equipment/enhance", enhanceRequest);

                case OperationType.Dismantle:
                    var dismantleRequest = (DismantleRequest)operation.Request;
                    return await _networkClient.SendRequest<DismantleRequest, DismantleResult>(
                        "equipment/dismantle", dismantleRequest);

                case OperationType.Equip:
                    var equipRequest = (EquipRequest)operation.Request;
                    return await _networkClient.SendRequest<EquipRequest, EquipResult>(
                        "equipment/equip", equipRequest);

                default:
                    throw new NotSupportedException($"Unsupported operation type: {operation.Type}");
            }
        }
        catch (NetworkTimeoutException) {
            // 网络超时，可以重试
            _logger.Warning($"Network timeout for {operation.Type} operation. Retry: {operation.RetryCount}");
            throw new OperationRetryException("Network timeout");
        }
        catch (ServerUnavailableException) {
            // 服务器不可用，可以重试
            _logger.Warning($"Server unavailable for {operation.Type} operation. Retry: {operation.RetryCount}");
            throw new OperationRetryException("Server unavailable");
        }
    }

    // 计算重试延迟（指数退避策略）
    private TimeSpan CalculateRetryDelay(int retryCount) {
        // 基础延迟300ms，每次重试翻倍，最大10秒
        int delayMs = Math.Min(300 * (int)Math.Pow(2, retryCount - 1), 10000);

        // 添加随机抖动，避免多个客户端同时重试
        Random random = new Random();
        delayMs += random.Next(delayMs / 5);

        return TimeSpan.FromMilliseconds(delayMs);
    }

    // 获取下一个序列号
    private long GetNextSequence() {
        return Interlocked.Increment(ref _sequenceCounter);
    }
}

// 待处理操作
public class PendingOperation {
    public OperationType Type { get; set; }
    public object Request { get; set; }
    public TaskCompletionSource<object> CompletionSource { get; set; }
    public int RetryCount { get; set; }
    public DateTime? LastAttemptTime { get; set; }
}

// 操作类型
public enum OperationType {
    Enhance,
    Dismantle,
    Equip
}

// 操作重试异常
public class OperationRetryException : Exception {
    public OperationRetryException(string message) : base(message) { }
}

// 最大重试次数异常
public class MaxRetriesExceededException : Exception {
    public MaxRetriesExceededException(string message) : base(message) { }
}

// 网络客户端接口
public interface IGameNetworkClient {
    Task<TResponse> SendRequest<TRequest, TResponse>(string endpoint, TRequest request);
}
