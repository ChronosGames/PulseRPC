using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace ChatApp.Server;

/// <summary>
/// 分布式事务协调器接口
/// </summary>
public interface IDistributedTransactionCoordinator
{
    /// <summary>
    /// 开始一个分布式事务
    /// </summary>
    /// <param name="transactionId">事务ID</param>
    /// <param name="resources">参与事务的资源列表</param>
    /// <returns>事务上下文</returns>
    Task<TransactionContext> BeginTransactionAsync(string transactionId, List<ResourceIdentifier> resources);

    /// <summary>
    /// 提交事务
    /// </summary>
    /// <param name="transactionId">事务ID</param>
    Task CommitTransactionAsync(string transactionId);

    /// <summary>
    /// 回滚事务
    /// </summary>
    /// <param name="transactionId">事务ID</param>
    Task RollbackTransactionAsync(string transactionId);
}

// 分布式事务协调器
public class DistributedTransactionCoordinator : IDistributedTransactionCoordinator {
    private readonly IDistributedLockService _lockService;
    private readonly IRpcService _rpcService;
    private readonly ILogger _logger;

    // 事务状态存储
    private readonly IDistributedCache _transactionStore;

    public DistributedTransactionCoordinator(
        IDistributedLockService lockService,
        IRpcService rpcService,
        IDistributedCache transactionStore,
        ILogger logger) {

        _lockService = lockService;
        _rpcService = rpcService;
        _transactionStore = transactionStore;
        _logger = logger;
    }

    // 开始分布式事务
    public async Task<TransactionContext> BeginTransactionAsync(
        string transactionId, List<ResourceIdentifier> resources) {

        // 1. 记录事务信息
        var transaction = new TransactionRecord {
            TransactionId = transactionId,
            Status = TransactionStatus.Preparing,
            Resources = resources,
            StartTime = DateTime.UtcNow
        };

        await StoreTransaction(transaction);

        // 2. 给所有资源排序（防止死锁）
        var orderedResources = resources
            .OrderBy(r => r.ResourceKey)
            .ToList();

        // 3. 按顺序尝试锁定所有资源
        List<string> acquiredLocks = new List<string>();

        try {
            foreach (var resource in orderedResources) {
                var lockResult = await _lockService.AcquireGlobalLock(
                    resource.ResourceKey,
                    transactionId,
                    "TransactionCoordinator",
                    TimeSpan.FromSeconds(30));

                if (!lockResult.Success) {
                    // 锁获取失败，抛出异常
                    throw new ResourceLockException(
                        $"Failed to lock resource {resource.ResourceKey}, " +
                        $"owned by {lockResult.CurrentOwner?.RequestId}");
                }

                acquiredLocks.Add(resource.ResourceKey);
            }

            // 4. 所有资源锁定成功，更新事务状态
            transaction.Status = TransactionStatus.Prepared;
            await StoreTransaction(transaction);

            // 5. 返回事务上下文
            return new TransactionContext {
                TransactionId = transactionId,
                Resources = orderedResources,
                AcquiredLocks = acquiredLocks
            };
        }
        catch (Exception ex) {
            // 6. 发生异常，释放已获取的锁
            foreach (var resourceKey in acquiredLocks) {
                await _lockService.ReleaseGlobalLock(
                    resourceKey, transactionId, "TransactionCoordinator");
            }

            // 7. 更新事务状态为失败
            transaction.Status = TransactionStatus.Failed;
            transaction.ErrorMessage = ex.Message;
            await StoreTransaction(transaction);

            throw;
        }
    }

    // 提交事务
    public async Task CommitTransactionAsync(string transactionId) {
        // 1. 获取事务记录
        var transaction = await GetTransaction(transactionId);

        if (transaction == null || transaction.Status != TransactionStatus.Prepared) {
            throw new InvalidTransactionStateException(
                $"Transaction {transactionId} is not in prepared state");
        }

        try {
            // 2. 更新状态为提交中
            transaction.Status = TransactionStatus.Committing;
            await StoreTransaction(transaction);

            // 3. 通知每个参与者提交操作
            foreach (var resource in transaction.Resources) {
                try {
                    await NotifyParticipant(
                        resource.ServerId,
                        "CommitResource",
                        new ResourceCommitRequest {
                            TransactionId = transactionId,
                            ResourceKey = resource.ResourceKey
                        });
                }
                catch (Exception ex) {
                    _logger.Error(
                        $"Failed to notify participant {resource.ServerId} " +
                        $"for resource {resource.ResourceKey}: {ex.Message}");
                    // 继续通知其他参与者
                }
            }

            // 4. 更新状态为已提交
            transaction.Status = TransactionStatus.Committed;
            await StoreTransaction(transaction);
        }
        finally {
            // 5. 释放所有资源锁
            foreach (var resource in transaction.Resources) {
                await _lockService.ReleaseGlobalLock(
                    resource.ResourceKey, transactionId, "TransactionCoordinator");
            }
        }
    }

    // 回滚事务
    public async Task RollbackTransactionAsync(string transactionId) {
        // 1. 获取事务记录
        var transaction = await GetTransaction(transactionId);

        if (transaction == null) {
            throw new TransactionNotFoundException(transactionId);
        }

        try {
            // 2. 更新状态为回滚中
            transaction.Status = TransactionStatus.RollingBack;
            await StoreTransaction(transaction);

            // 3. 通知每个参与者回滚操作
            foreach (var resource in transaction.Resources) {
                try {
                    await NotifyParticipant(
                        resource.ServerId,
                        "RollbackResource",
                        new ResourceRollbackRequest {
                            TransactionId = transactionId,
                            ResourceKey = resource.ResourceKey
                        });
                }
                catch (Exception ex) {
                    _logger.Error(
                        $"Failed to notify participant {resource.ServerId} " +
                        $"for resource {resource.ResourceKey} rollback: {ex.Message}");
                    // 继续通知其他参与者
                }
            }

            // 4. 更新状态为已回滚
            transaction.Status = TransactionStatus.RolledBack;
            await StoreTransaction(transaction);
        }
        finally {
            // 5. 释放所有资源锁
            foreach (var resource in transaction.Resources) {
                try {
                    await _lockService.ReleaseGlobalLock(
                        resource.ResourceKey, transactionId, "TransactionCoordinator");
                }
                catch (Exception ex) {
                    _logger.Error($"Error releasing lock for {resource.ResourceKey}: {ex.Message}");
                }
            }
        }
    }

    // 通知事务参与者
    private async Task NotifyParticipant<T>(string serverId, string endpoint, T request) {
        try {
            await _rpcService.CallRemoteAsync<bool>(serverId, endpoint, request);
        }
        catch (Exception ex) {
            _logger.Error($"Error notifying participant {serverId}/{endpoint}: {ex.Message}");
            throw;
        }
    }

    // 存储事务记录
    private async Task StoreTransaction(TransactionRecord transaction) {
        string key = $"transaction:{transaction.TransactionId}";
        string json = JsonSerializer.Serialize(transaction);

        await _transactionStore.SetStringAsync(key, json, new DistributedCacheEntryOptions {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)
        });
    }

    // 获取事务记录
    private async Task<TransactionRecord> GetTransaction(string transactionId) {
        string key = $"transaction:{transactionId}";
        string json = await _transactionStore.GetStringAsync(key);

        if (string.IsNullOrEmpty(json)) {
            return null;
        }

        return JsonSerializer.Deserialize<TransactionRecord>(json);
    }
}

// 事务记录
public class TransactionRecord {
    public string TransactionId { get; set; }
    public TransactionStatus Status { get; set; }
    public List<ResourceIdentifier> Resources { get; set; }
    public DateTime StartTime { get; set; }
    public string ErrorMessage { get; set; }
}

// 资源标识符
public class ResourceIdentifier {
    public string ServerId { get; set; }
    public string ResourceKey { get; set; }
}

// 事务上下文
public class TransactionContext {
    public string TransactionId { get; set; }
    public List<ResourceIdentifier> Resources { get; set; }
    public List<string> AcquiredLocks { get; set; }
}

// 事务状态
public enum TransactionStatus {
    Preparing,
    Prepared,
    Committing,
    Committed,
    RollingBack,
    RolledBack,
    Failed
}

// 资源提交请求
public class ResourceCommitRequest {
    public string TransactionId { get; set; }
    public string ResourceKey { get; set; }
}

// 资源回滚请求
public class ResourceRollbackRequest {
    public string TransactionId { get; set; }
    public string ResourceKey { get; set; }
}
