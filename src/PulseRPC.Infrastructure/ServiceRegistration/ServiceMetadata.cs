using System.Collections.Generic;

namespace PulseRPC.Infrastructure;

/// <summary>
/// 服务元数据
/// </summary>
public class ServiceMetadata
{
    /// <summary>
    /// 元数据字典
    /// </summary>
    public Dictionary<string, object> Data { get; set; } = new();

    /// <summary>
    /// 创建空元数据
    /// </summary>
    public ServiceMetadata()
    {
    }

    /// <summary>
    /// 从字典创建元数据
    /// </summary>
    public ServiceMetadata(Dictionary<string, object> data)
    {
        Data = data ?? new Dictionary<string, object>();
    }

    /// <summary>
    /// 从字符串字典创建元数据
    /// </summary>
    public ServiceMetadata(Dictionary<string, string> data)
    {
        Data = new Dictionary<string, object>();
        if (data != null)
        {
            foreach (var kvp in data)
            {
                Data[kvp.Key] = kvp.Value;
            }
        }
    }

    /// <summary>
    /// 获取元数据值
    /// </summary>
    public T? GetValue<T>(string key)
    {
        if (Data.TryGetValue(key, out var value) && value is T typedValue)
        {
            return typedValue;
        }
        return default(T);
    }

    /// <summary>
    /// 设置元数据值
    /// </summary>
    public void SetValue(string key, object value)
    {
        Data[key] = value;
    }

    /// <summary>
    /// 检查是否包含指定键
    /// </summary>
    public bool ContainsKey(string key)
    {
        return Data.ContainsKey(key);
    }
}