using System.Text.Json;

namespace PulseRPC.Benchmark.Metrics.Abstractions;

/// <summary>
/// JSON序列化提供程序接口
/// </summary>
public interface IJsonSerializationProvider
{
    /// <summary>
    /// JSON序列化选项
    /// </summary>
    JsonSerializerOptions Options { get; }

    /// <summary>
    /// 同步序列化对象到JSON字符串
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <param name="value">要序列化的对象</param>
    /// <returns>JSON字符串</returns>
    string Serialize<T>(T value);

    /// <summary>
    /// 异步序列化对象到JSON字符串
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <param name="value">要序列化的对象</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>JSON字符串</returns>
    Task<string> SerializeAsync<T>(T value, CancellationToken cancellationToken = default);

    /// <summary>
    /// 序列化对象到流
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <param name="stream">目标流</param>
    /// <param name="value">要序列化的对象</param>
    void Serialize<T>(Stream stream, T value);

    /// <summary>
    /// 异步序列化对象到流
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <param name="stream">目标流</param>
    /// <param name="value">要序列化的对象</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>异步任务</returns>
    Task SerializeAsync<T>(Stream stream, T value, CancellationToken cancellationToken = default);

    /// <summary>
    /// 反序列化JSON字符串到对象
    /// </summary>
    /// <typeparam name="T">目标类型</typeparam>
    /// <param name="json">JSON字符串</param>
    /// <returns>反序列化的对象</returns>
    T? Deserialize<T>(string json);

    /// <summary>
    /// 异步反序列化流到对象
    /// </summary>
    /// <typeparam name="T">目标类型</typeparam>
    /// <param name="stream">JSON流</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>反序列化的对象</returns>
    ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken cancellationToken = default);

    /// <summary>
    /// 尝试序列化对象（不抛出异常）
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <param name="value">要序列化的对象</param>
    /// <param name="json">输出的JSON字符串</param>
    /// <returns>是否成功</returns>
    bool TrySerialize<T>(T value, out string json);

    /// <summary>
    /// 尝试反序列化JSON字符串（不抛出异常）
    /// </summary>
    /// <typeparam name="T">目标类型</typeparam>
    /// <param name="json">JSON字符串</param>
    /// <param name="result">输出的对象</param>
    /// <returns>是否成功</returns>
    bool TryDeserialize<T>(string json, out T? result);
}
