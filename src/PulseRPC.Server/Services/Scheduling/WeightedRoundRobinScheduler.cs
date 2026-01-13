using System;
using System.Threading;

namespace PulseRPC.Server.Services.Scheduling;

/// <summary>
/// 加权轮询调度器 - 实现基于权重的公平调度
/// </summary>
public sealed class WeightedRoundRobinScheduler
{
    private readonly int[] _weights;
    private readonly int[] _currentWeights;
    private readonly int _totalWeight;
    private readonly object _lock = new object();

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="weights">权重数组，索引对应优先级</param>
    public WeightedRoundRobinScheduler(int[] weights)
    {
        if (weights == null || weights.Length == 0)
            throw new ArgumentException("权重数组不能为空", nameof(weights));

        foreach (var weight in weights)
        {
            if (weight < 0)
                throw new ArgumentException("权重不能为负数", nameof(weights));
        }

        _weights = new int[weights.Length];
        _currentWeights = new int[weights.Length];
        Array.Copy(weights, _weights, weights.Length);
        Array.Copy(weights, _currentWeights, weights.Length);

        _totalWeight = 0;
        foreach (var weight in _weights)
        {
            _totalWeight += weight;
        }

        if (_totalWeight == 0)
            throw new ArgumentException("权重总和不能为0", nameof(weights));
    }

    /// <summary>
    /// 获取下一个要处理的优先级索引
    /// 使用平滑加权轮询算法 (Smooth Weighted Round Robin)
    /// </summary>
    /// <returns>优先级索引 (0=最高优先级)</returns>
    public int GetNext()
    {
        lock (_lock)
        {
            int selectedIndex = -1;
            int maxCurrentWeight = int.MinValue;

            // 找出当前权重最大的项
            for (int i = 0; i < _currentWeights.Length; i++)
            {
                if (_currentWeights[i] > maxCurrentWeight)
                {
                    maxCurrentWeight = _currentWeights[i];
                    selectedIndex = i;
                }
            }

            if (selectedIndex == -1)
            {
                // 所有权重都为0，重置为初始权重
                Array.Copy(_weights, _currentWeights, _weights.Length);
                selectedIndex = 0;
            }

            // 被选中的项减去总权重
            _currentWeights[selectedIndex] -= _totalWeight;

            // 所有项都加上自己的权重
            for (int i = 0; i < _currentWeights.Length; i++)
            {
                _currentWeights[i] += _weights[i];
            }

            return selectedIndex;
        }
    }

    /// <summary>
    /// 重置调度器状态
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            Array.Copy(_weights, _currentWeights, _weights.Length);
        }
    }

    /// <summary>
    /// 获取权重信息
    /// </summary>
    public (int[] weights, int[] currentWeights, int totalWeight) GetWeightInfo()
    {
        lock (_lock)
        {
            var weights = new int[_weights.Length];
            var currentWeights = new int[_currentWeights.Length];
            Array.Copy(_weights, weights, _weights.Length);
            Array.Copy(_currentWeights, currentWeights, _currentWeights.Length);
            return (weights, currentWeights, _totalWeight);
        }
    }
}
