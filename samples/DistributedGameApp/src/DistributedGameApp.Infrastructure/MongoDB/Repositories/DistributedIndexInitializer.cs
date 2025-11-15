using Consul;
using Microsoft.Extensions.Logging;

namespace DistributedGameApp.Infrastructure.MongoDB.Repositories;

/// <summary>
/// 分布式索引初始化器 - 使用 Consul KV 确保集群中只有一个节点初始化索引
/// </summary>
public class DistributedIndexInitializer
{
    private readonly IConsulClient _consulClient;
    private readonly GuildRepositoryIndexInitializer _guildIndexInitializer;
    private readonly CharacterRepository _characterRepository;
    private readonly ILogger<DistributedIndexInitializer> _logger;
    private readonly string _lockKey;
    private readonly string _initFlagKey;

    public DistributedIndexInitializer(
        IConsulClient consulClient,
        GuildRepositoryIndexInitializer guildIndexInitializer,
        CharacterRepository characterRepository,
        ILogger<DistributedIndexInitializer> logger,
        string lockKey = "pulserpc/locks/mongodb-index-init",
        string initFlagKey = "pulserpc/indexes/initialized")
    {
        _consulClient = consulClient ?? throw new ArgumentNullException(nameof(consulClient));
        _guildIndexInitializer = guildIndexInitializer ?? throw new ArgumentNullException(nameof(guildIndexInitializer));
        _characterRepository = characterRepository ?? throw new ArgumentNullException(nameof(characterRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _lockKey = lockKey;
        _initFlagKey = initFlagKey;
    }

    /// <summary>
    /// 使用 Consul KV CAS (Check-And-Set) 机制初始化索引
    /// </summary>
    /// <remarks>
    /// <para><strong>工作流程</strong>：</para>
    /// <list type="number">
    /// <item><description>检查初始化标志（Consul KV）</description></item>
    /// <item><description>如果已初始化，直接返回</description></item>
    /// <item><description>如果未初始化，尝试获取锁（CAS 操作）</description></item>
    /// <item><description>获取锁成功后，执行索引初始化</description></item>
    /// <item><description>完成后设置初始化标志</description></item>
    /// </list>
    /// </remarks>
    public async Task EnsureIndexesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // 步骤 1: 检查是否已经初始化
            var initFlag = await _consulClient.KV.Get(_initFlagKey, cancellationToken);
            if (initFlag.Response != null)
            {
                _logger.LogInformation("✓ 索引已初始化（标志已存在），跳过");
                return;
            }

            _logger.LogInformation("尝试获取初始化锁: {LockKey}", _lockKey);

            // 步骤 2: 尝试获取锁（使用 CAS）
            var lockAcquired = await TryAcquireLockAsync(cancellationToken);

            if (lockAcquired)
            {
                _logger.LogInformation("✓ 获取初始化锁成功，开始初始化索引...");

                // 步骤 3: 执行索引初始化
                await InitializeAllIndexesAsync(cancellationToken);

                // 步骤 4: 设置初始化完成标志
                await SetInitializedFlagAsync(cancellationToken);

                _logger.LogInformation("✓ 索引初始化完成");
            }
            else
            {
                _logger.LogInformation("⊗ 其他节点正在初始化索引，当前节点跳过");

                // 等待其他节点完成初始化
                await WaitForInitializationAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "索引初始化过程中发生错误");
            throw;
        }
        finally
        {
            // 释放锁
            await ReleaseLockAsync(cancellationToken);
        }
    }

    /// <summary>
    /// 尝试获取锁（使用 Consul KV CAS）
    /// </summary>
    private async Task<bool> TryAcquireLockAsync(CancellationToken cancellationToken)
    {
        try
        {
            var lockValue = Environment.MachineName + "_" + Guid.NewGuid().ToString();
            var kvPair = new KVPair(_lockKey)
            {
                Value = System.Text.Encoding.UTF8.GetBytes(lockValue),
                Flags = 0
            };

            // CAS 操作：只有当 key 不存在时才创建
            var result = await _consulClient.KV.Acquire(kvPair, cancellationToken);
            return result.Response;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "获取锁失败");
            return false;
        }
    }

    /// <summary>
    /// 释放锁
    /// </summary>
    private async Task ReleaseLockAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _consulClient.KV.Delete(_lockKey, cancellationToken);
            _logger.LogDebug("锁已释放: {LockKey}", _lockKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "释放锁时发生错误");
        }
    }

    /// <summary>
    /// 设置初始化完成标志
    /// </summary>
    private async Task SetInitializedFlagAsync(CancellationToken cancellationToken)
    {
        var kvPair = new KVPair(_initFlagKey)
        {
            Value = System.Text.Encoding.UTF8.GetBytes(DateTime.UtcNow.ToString("O"))
        };

        await _consulClient.KV.Put(kvPair, cancellationToken);
        _logger.LogInformation("初始化标志已设置: {InitFlagKey}", _initFlagKey);
    }

    /// <summary>
    /// 等待其他节点完成初始化
    /// </summary>
    private async Task WaitForInitializationAsync(CancellationToken cancellationToken)
    {
        var maxWaitTime = TimeSpan.FromMinutes(5);
        var checkInterval = TimeSpan.FromSeconds(2);
        var startTime = DateTime.UtcNow;

        while ((DateTime.UtcNow - startTime) < maxWaitTime)
        {
            var initFlag = await _consulClient.KV.Get(_initFlagKey, cancellationToken);
            if (initFlag.Response != null)
            {
                _logger.LogInformation("✓ 其他节点已完成索引初始化");
                return;
            }

            _logger.LogDebug("等待其他节点完成初始化...");
            await Task.Delay(checkInterval, cancellationToken);
        }

        _logger.LogWarning("等待索引初始化超时（{Timeout} 分钟），继续启动", maxWaitTime.TotalMinutes);
    }

    /// <summary>
    /// 初始化所有集合的索引
    /// </summary>
    private async Task InitializeAllIndexesAsync(CancellationToken cancellationToken)
    {
        // 初始化 Guild 相关索引
        _logger.LogInformation("→ 初始化 Guild 相关索引...");
        await _guildIndexInitializer.EnsureIndexesAsync();

        // 初始化 Character 索引
        _logger.LogInformation("→ 初始化 Character 索引...");
        await _characterRepository.EnsureIndexesAsync();

        // TODO: 添加其他 Repository 的索引初始化
        // await _otherRepository.EnsureIndexesAsync();
    }

    /// <summary>
    /// 检查索引是否已初始化
    /// </summary>
    public async Task<bool> IsIndexesInitializedAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var initFlag = await _consulClient.KV.Get(_initFlagKey, cancellationToken);
            return initFlag.Response != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 重置初始化标志（用于测试或重建）
    /// </summary>
    public async Task ResetInitializationFlagAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("⚠️ 重置索引初始化标志");
        await _consulClient.KV.Delete(_initFlagKey, cancellationToken);
        await _consulClient.KV.Delete(_lockKey, cancellationToken);
    }
}
