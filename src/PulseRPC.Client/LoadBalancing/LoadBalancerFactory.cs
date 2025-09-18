using Microsoft.Extensions.Logging;
using PulseRPC.Client.ServiceDiscovery;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC.Client.LoadBalancing;

/// <summary>
/// 负载均衡器工厂接口
/// </summary>
public interface ILoadBalancerFactory
{
    /// <summary>
    /// 创建负载均衡器
    /// </summary>
    ILoadBalancer CreateLoadBalancer(
        LoadBalancingStrategy strategy,
        LoadBalancingConfiguration? configuration = null,
        ILoggerFactory? loggerFactory = null);

    /// <summary>
    /// 获取或创建负载均衡器
    /// </summary>
    ILoadBalancer GetOrCreateLoadBalancer(
        string name,
        LoadBalancingStrategy strategy,
        LoadBalancingConfiguration? configuration = null,
        ILoggerFactory? loggerFactory = null);

    /// <summary>
    /// 获取支持的策略
    /// </summary>
    IReadOnlyList<LoadBalancingStrategy> GetSupportedStrategies();

    /// <summary>
    /// 释放负载均衡器
    /// </summary>
    void DisposeLoadBalancer(string name);

    /// <summary>
    /// 释放所有负载均衡器
    /// </summary>
    void DisposeAll();
}

/// <summary>
/// 负载均衡器工厂
/// </summary>
public sealed class LoadBalancerFactory : ILoadBalancerFactory, IDisposable
{
    private readonly ConcurrentDictionary<string, ILoadBalancer> _loadBalancers = new();
    private readonly ILoggerFactory? _defaultLoggerFactory;
    private readonly ILogger<LoadBalancerFactory> _logger;
    private volatile bool _disposed;

    /// <summary>
    /// 支持的负载均衡策略
    /// </summary>
    private static readonly IReadOnlyList<LoadBalancingStrategy> SupportedStrategies = new[]
    {
        LoadBalancingStrategy.Random,
        LoadBalancingStrategy.RoundRobin,
        LoadBalancingStrategy.WeightedRoundRobin,
        LoadBalancingStrategy.LeastConnections,
        LoadBalancingStrategy.WeightedLeastConnections,
        LoadBalancingStrategy.ConsistentHash,
        LoadBalancingStrategy.ResponseTime,
        LoadBalancingStrategy.LocalityAware,
        LoadBalancingStrategy.Adaptive
    };

    /// <summary>
    /// 构造函数
    /// </summary>
    public LoadBalancerFactory(ILoggerFactory? loggerFactory = null)
    {
        _defaultLoggerFactory = loggerFactory;
        _logger = loggerFactory?.CreateLogger<LoadBalancerFactory>()
            ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<LoadBalancerFactory>.Instance;

        _logger.LogDebug("负载均衡器工厂已创建");
    }

    /// <summary>
    /// 创建负载均衡器
    /// </summary>
    public ILoadBalancer CreateLoadBalancer(
        LoadBalancingStrategy strategy,
        LoadBalancingConfiguration? configuration = null,
        ILoggerFactory? loggerFactory = null)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(LoadBalancerFactory));
        }

        var actualLoggerFactory = loggerFactory ?? _defaultLoggerFactory;
        var actualConfiguration = configuration ?? new LoadBalancingConfiguration { Strategy = strategy };

        _logger.LogDebug("创建负载均衡器: {Strategy}", strategy);

        return strategy switch
        {
            LoadBalancingStrategy.Random => new RandomLoadBalancer(
                actualConfiguration,
                actualLoggerFactory?.CreateLogger<RandomLoadBalancer>()),

            LoadBalancingStrategy.RoundRobin => new RoundRobinLoadBalancer(
                actualConfiguration,
                actualLoggerFactory?.CreateLogger<RoundRobinLoadBalancer>()),

            LoadBalancingStrategy.WeightedRoundRobin => new WeightedRoundRobinLoadBalancer(
                actualConfiguration,
                actualLoggerFactory?.CreateLogger<WeightedRoundRobinLoadBalancer>()),

            LoadBalancingStrategy.LeastConnections => new LeastConnectionsLoadBalancer(
                actualConfiguration,
                actualLoggerFactory?.CreateLogger<LeastConnectionsLoadBalancer>()),

            LoadBalancingStrategy.WeightedLeastConnections => CreateWeightedLeastConnectionsLoadBalancer(
                actualConfiguration,
                actualLoggerFactory?.CreateLogger<WeightedLeastConnectionsLoadBalancer>()),

            LoadBalancingStrategy.ConsistentHash => new ConsistentHashLoadBalancer(
                actualConfiguration,
                actualLoggerFactory?.CreateLogger<ConsistentHashLoadBalancer>()),

            LoadBalancingStrategy.ResponseTime => CreateResponseTimeLoadBalancer(
                actualConfiguration,
                actualLoggerFactory?.CreateLogger<ResponseTimeLoadBalancer>()),

            LoadBalancingStrategy.LocalityAware => CreateLocalityAwareLoadBalancer(
                actualConfiguration,
                actualLoggerFactory?.CreateLogger<LocalityAwareLoadBalancer>()),

            LoadBalancingStrategy.Adaptive => CreateAdaptiveLoadBalancer(
                actualConfiguration,
                actualLoggerFactory?.CreateLogger<AdaptiveLoadBalancer>()),

            _ => throw new NotSupportedException($"不支持的负载均衡策略: {strategy}")
        };
    }

    /// <summary>
    /// 获取或创建负载均衡器
    /// </summary>
    public ILoadBalancer GetOrCreateLoadBalancer(
        string name,
        LoadBalancingStrategy strategy,
        LoadBalancingConfiguration? configuration = null,
        ILoggerFactory? loggerFactory = null)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(LoadBalancerFactory));
        }

        if (string.IsNullOrEmpty(name)) throw new ArgumentException(nameof(name));

        return _loadBalancers.GetOrAdd(name, _ =>
        {
            var loadBalancer = CreateLoadBalancer(strategy, configuration, loggerFactory);
            _logger.LogInformation("负载均衡器已创建并缓存: {Name} ({Strategy})", name, strategy);
            return loadBalancer;
        });
    }

    /// <summary>
    /// 获取支持的策略
    /// </summary>
    public IReadOnlyList<LoadBalancingStrategy> GetSupportedStrategies()
    {
        return SupportedStrategies;
    }

    /// <summary>
    /// 释放负载均衡器
    /// </summary>
    public void DisposeLoadBalancer(string name)
    {
        if (_disposed)
        {
            return;
        }

        if (string.IsNullOrEmpty(name)) throw new ArgumentException(nameof(name));

        if (_loadBalancers.TryRemove(name, out var loadBalancer))
        {
            try
            {
                if (loadBalancer is IDisposable disposable)
                {
                    disposable.Dispose();
                }

                _logger.LogInformation("负载均衡器已释放: {Name}", name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "释放负载均衡器失败: {Name}", name);
            }
        }
    }

    /// <summary>
    /// 释放所有负载均衡器
    /// </summary>
    public void DisposeAll()
    {
        if (_disposed)
        {
            return;
        }

        var loadBalancers = _loadBalancers.ToArray();
        _loadBalancers.Clear();

        foreach (var kvp in loadBalancers)
        {
            try
            {
                if (kvp.Value is IDisposable disposable)
                {
                    disposable.Dispose();
                }

                _logger.LogDebug("负载均衡器已释放: {Name}", kvp.Key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "释放负载均衡器失败: {Name}", kvp.Key);
            }
        }

        _logger.LogInformation("所有负载均衡器已释放: {Count} 个", loadBalancers.Length);
    }

    /// <summary>
    /// 创建加权最少连接负载均衡器
    /// </summary>
    private ILoadBalancer CreateWeightedLeastConnectionsLoadBalancer(
        LoadBalancingConfiguration configuration,
        ILogger? logger)
    {
        // 加权最少连接可以通过扩展最少连接实现
        return new WeightedLeastConnectionsLoadBalancer(configuration, logger);
    }

    /// <summary>
    /// 创建响应时间负载均衡器
    /// </summary>
    private ILoadBalancer CreateResponseTimeLoadBalancer(
        LoadBalancingConfiguration configuration,
        ILogger? logger)
    {
        return new ResponseTimeLoadBalancer(configuration, logger);
    }

    /// <summary>
    /// 创建本地感知负载均衡器
    /// </summary>
    private ILoadBalancer CreateLocalityAwareLoadBalancer(
        LoadBalancingConfiguration configuration,
        ILogger? logger)
    {
        return new LocalityAwareLoadBalancer(configuration, logger);
    }

    /// <summary>
    /// 创建自适应负载均衡器
    /// </summary>
    private ILoadBalancer CreateAdaptiveLoadBalancer(
        LoadBalancingConfiguration configuration,
        ILogger? logger)
    {
        return new AdaptiveLoadBalancer(configuration, logger);
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _logger.LogInformation("正在关闭负载均衡器工厂");

        DisposeAll();

        _logger.LogInformation("负载均衡器工厂已关闭");
    }
}

/// <summary>
/// 加权最少连接负载均衡器
/// </summary>
internal sealed class WeightedLeastConnectionsLoadBalancer : LoadBalancerBase
{
    public override string Name => "WeightedLeastConnections";
    public override LoadBalancingStrategy Strategy => LoadBalancingStrategy.WeightedLeastConnections;

    public WeightedLeastConnectionsLoadBalancer(
        LoadBalancingConfiguration? configuration = null,
        ILogger? logger = null)
        : base(configuration, logger)
    {
    }

    protected override Task<LoadBalancingResult> SelectInstanceInternalAsync(
        LoadBalancingContext context,
        IReadOnlyList<ServiceInstance> eligibleInstances)
    {
        if (eligibleInstances.Count == 0)
        {
            return Task.FromResult(LoadBalancingResult.Failure("没有符合条件的实例", 0));
        }

        if (eligibleInstances.Count == 1)
        {
            return Task.FromResult(LoadBalancingResult.Success(
                eligibleInstances[0], "唯一可用实例", 1));
        }

        // 计算加权连接密度 (连接数 / 权重)，选择密度最低的实例
        ServiceInstance? selectedInstance = null;
        double minWeightedDensity = double.MaxValue;

        foreach (var instance in eligibleInstances)
        {
            var connections = GetCurrentConnections(instance.Id);
            var weight = Math.Max(1, instance.Weight); // 避免除零
            var weightedDensity = (double)connections / weight;

            if (weightedDensity < minWeightedDensity)
            {
                minWeightedDensity = weightedDensity;
                selectedInstance = instance;
            }
        }

        var reason = $"加权最少连接选择 (加权密度: {minWeightedDensity:F2})";
        return Task.FromResult(LoadBalancingResult.Success(selectedInstance!, reason, eligibleInstances.Count));
    }

    private int GetCurrentConnections(string instanceId)
    {
        return _instanceHealth.TryGetValue(instanceId, out var healthInfo) ? healthInfo.CurrentConnections : 0;
    }
}

/// <summary>
/// 响应时间负载均衡器
/// </summary>
internal sealed class ResponseTimeLoadBalancer : LoadBalancerBase
{
    public override string Name => "ResponseTime";
    public override LoadBalancingStrategy Strategy => LoadBalancingStrategy.ResponseTime;

    public ResponseTimeLoadBalancer(
        LoadBalancingConfiguration? configuration = null,
        ILogger? logger = null)
        : base(configuration, logger)
    {
    }

    protected override Task<LoadBalancingResult> SelectInstanceInternalAsync(
        LoadBalancingContext context,
        IReadOnlyList<ServiceInstance> eligibleInstances)
    {
        if (eligibleInstances.Count == 0)
        {
            return Task.FromResult(LoadBalancingResult.Failure("没有符合条件的实例", 0));
        }

        if (eligibleInstances.Count == 1)
        {
            return Task.FromResult(LoadBalancingResult.Success(
                eligibleInstances[0], "唯一可用实例", 1));
        }

        // 选择平均响应时间最短的实例
        ServiceInstance? selectedInstance = null;
        double minResponseTime = double.MaxValue;

        foreach (var instance in eligibleInstances)
        {
            var responseTime = GetAverageResponseTime(instance.Id);

            if (responseTime < minResponseTime)
            {
                minResponseTime = responseTime;
                selectedInstance = instance;
            }
        }

        var reason = $"响应时间选择 (平均响应时间: {minResponseTime:F2}ms)";
        return Task.FromResult(LoadBalancingResult.Success(selectedInstance!, reason, eligibleInstances.Count));
    }

    private double GetAverageResponseTime(string instanceId)
    {
        return _instanceHealth.TryGetValue(instanceId, out var healthInfo) ? healthInfo.AverageResponseTime : 0;
    }
}

/// <summary>
/// 本地感知负载均衡器
/// </summary>
internal sealed class LocalityAwareLoadBalancer : LoadBalancerBase
{
    public override string Name => "LocalityAware";
    public override LoadBalancingStrategy Strategy => LoadBalancingStrategy.LocalityAware;

    public LocalityAwareLoadBalancer(
        LoadBalancingConfiguration? configuration = null,
        ILogger? logger = null)
        : base(configuration, logger)
    {
    }

    protected override Task<LoadBalancingResult> SelectInstanceInternalAsync(
        LoadBalancingContext context,
        IReadOnlyList<ServiceInstance> eligibleInstances)
    {
        if (eligibleInstances.Count == 0)
        {
            return Task.FromResult(LoadBalancingResult.Failure("没有符合条件的实例", 0));
        }

        // 优先选择本地区域的实例
        var localInstances = eligibleInstances.Where(i =>
            !string.IsNullOrEmpty(context.PreferredRegion) &&
            string.Equals(i.Region, context.PreferredRegion, StringComparison.OrdinalIgnoreCase)).ToList();

        var candidateInstances = localInstances.Any() ? localInstances : eligibleInstances.ToList();

        // 在候选实例中使用轮询选择
        var index = Environment.TickCount % candidateInstances.Count;
        var selectedInstance = candidateInstances[index];

        var reason = localInstances.Any() ? "本地优先选择" : "非本地轮询选择";
        return Task.FromResult(LoadBalancingResult.Success(selectedInstance, reason, candidateInstances.Count));
    }
}

/// <summary>
/// 自适应负载均衡器
/// </summary>
internal sealed class AdaptiveLoadBalancer : LoadBalancerBase
{
    public override string Name => "Adaptive";
    public override LoadBalancingStrategy Strategy => LoadBalancingStrategy.Adaptive;

    public AdaptiveLoadBalancer(
        LoadBalancingConfiguration? configuration = null,
        ILogger? logger = null)
        : base(configuration, logger)
    {
    }

    protected override Task<LoadBalancingResult> SelectInstanceInternalAsync(
        LoadBalancingContext context,
        IReadOnlyList<ServiceInstance> eligibleInstances)
    {
        if (eligibleInstances.Count == 0)
        {
            return Task.FromResult(LoadBalancingResult.Failure("没有符合条件的实例", 0));
        }

        if (eligibleInstances.Count == 1)
        {
            return Task.FromResult(LoadBalancingResult.Success(
                eligibleInstances[0], "唯一可用实例", 1));
        }

        // 综合考虑多个因素计算自适应评分
        ServiceInstance? selectedInstance = null;
        double bestScore = double.MaxValue;

        var config = _configuration.AdaptiveConfiguration;

        foreach (var instance in eligibleInstances)
        {
            var score = CalculateAdaptiveScore(instance.Id, config);

            if (score < bestScore)
            {
                bestScore = score;
                selectedInstance = instance;
            }
        }

        var reason = $"自适应选择 (综合评分: {bestScore:F2})";
        return Task.FromResult(LoadBalancingResult.Success(selectedInstance!, reason, eligibleInstances.Count));
    }

    private double CalculateAdaptiveScore(string instanceId, AdaptiveLoadBalancingConfiguration config)
    {
        if (!_instanceHealth.TryGetValue(instanceId, out var healthInfo))
        {
            return 100.0; // 如果没有健康信息，给一个较高的分数
        }

        var score = 0.0;

        // 响应时间因子
        score += healthInfo.AverageResponseTime * config.ResponseTimeWeight;

        // 错误率因子
        score += healthInfo.ErrorRate * config.ErrorRateWeight;

        // 连接数因子
        score += healthInfo.CurrentConnections * config.ConnectionCountWeight;

        // 资源使用率因子
        if (healthInfo.CpuUsage.HasValue && healthInfo.MemoryUsage.HasValue)
        {
            var resourceUsage = (healthInfo.CpuUsage.Value + healthInfo.MemoryUsage.Value) / 2.0;
            score += resourceUsage * config.ResourceUsageWeight;
        }

        return score;
    }
}
