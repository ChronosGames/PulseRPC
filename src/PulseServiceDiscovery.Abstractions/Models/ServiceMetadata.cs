using System.Collections.Concurrent;

namespace PulseServiceDiscovery.Abstractions.Models;

/// <summary>
/// 服务元数据
/// </summary>
public class ServiceMetadata
{
    private readonly ConcurrentDictionary<string, string> _metadata = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 初始化空的元数据
    /// </summary>
    public ServiceMetadata()
    {
    }

    /// <summary>
    /// 使用字典初始化元数据
    /// </summary>
    /// <param name="metadata">元数据字典</param>
    public ServiceMetadata(IDictionary<string, string> metadata)
    {
        if (metadata != null)
        {
            foreach (var kvp in metadata)
            {
                _metadata[kvp.Key] = kvp.Value;
            }
        }
    }

    /// <summary>
    /// 获取所有键
    /// </summary>
    public IEnumerable<string> Keys => _metadata.Keys;

    /// <summary>
    /// 获取所有值
    /// </summary>
    public IEnumerable<string> Values => _metadata.Values;

    /// <summary>
    /// 元数据项数量
    /// </summary>
    public int Count => _metadata.Count;

    /// <summary>
    /// 索引器访问
    /// </summary>
    /// <param name="key">键</param>
    /// <returns>值</returns>
    public string? this[string key]
    {
        get => _metadata.TryGetValue(key, out var value) ? value : null;
        set
        {
            if (value != null)
                _metadata[key] = value;
            else
                _metadata.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// 获取值
    /// </summary>
    /// <param name="key">键</param>
    /// <param name="defaultValue">默认值</param>
    /// <returns>值</returns>
    public string? GetValue(string key, string? defaultValue = null) =>
        _metadata.TryGetValue(key, out var value) ? value : defaultValue;

    /// <summary>
    /// 设置值
    /// </summary>
    /// <param name="key">键</param>
    /// <param name="value">值</param>
    public void SetValue(string key, string value) => _metadata[key] = value;

    /// <summary>
    /// 移除键值对
    /// </summary>
    /// <param name="key">键</param>
    /// <returns>是否成功移除</returns>
    public bool Remove(string key) => _metadata.TryRemove(key, out _);

    /// <summary>
    /// 检查是否包含键
    /// </summary>
    /// <param name="key">键</param>
    /// <returns>是否包含</returns>
    public bool ContainsKey(string key) => _metadata.ContainsKey(key);

    /// <summary>
    /// 清空所有元数据
    /// </summary>
    public void Clear() => _metadata.Clear();

    /// <summary>
    /// 转换为字典
    /// </summary>
    /// <returns>字典</returns>
    public Dictionary<string, string> ToDictionary() => new(_metadata);

    /// <summary>
    /// 创建副本
    /// </summary>
    /// <returns>元数据副本</returns>
    public ServiceMetadata Clone() => new(_metadata);
}
