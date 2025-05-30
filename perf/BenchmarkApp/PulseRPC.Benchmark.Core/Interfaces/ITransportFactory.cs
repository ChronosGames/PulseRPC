using System;
using System.Collections.Generic;
using PulseRPC.Benchmark.Core.Models;

namespace PulseRPC.Benchmark.Core.Interfaces
{
    /// <summary>
    /// 传输层工厂接口，负责创建不同类型的传输层实例
    /// </summary>
    public interface ITransportFactory
    {
        /// <summary>
        /// 支持的传输类型列表
        /// </summary>
        IReadOnlyList<string> SupportedTransportTypes { get; }

        /// <summary>
        /// 创建指定类型的传输层实例
        /// </summary>
        /// <param name="transportType">传输类型（如："tcp", "kcp", "websocket"）</param>
        /// <param name="options">传输层选项</param>
        /// <returns>传输层实例</returns>
        IBenchmarkTransport CreateTransport(string transportType, TransportOptions? options = null);

        /// <summary>
        /// 检查是否支持指定的传输类型
        /// </summary>
        /// <param name="transportType">传输类型</param>
        /// <returns>是否支持</returns>
        bool IsSupported(string transportType);

        /// <summary>
        /// 注册新的传输类型创建器
        /// </summary>
        /// <param name="transportType">传输类型</param>
        /// <param name="creator">创建器函数</param>
        void RegisterTransportCreator(string transportType, Func<TransportOptions?, IBenchmarkTransport> creator);

        /// <summary>
        /// 注销传输类型创建器
        /// </summary>
        /// <param name="transportType">传输类型</param>
        /// <returns>是否成功注销</returns>
        bool UnregisterTransportCreator(string transportType);

        /// <summary>
        /// 获取传输类型的默认选项
        /// </summary>
        /// <param name="transportType">传输类型</param>
        /// <returns>默认选项</returns>
        TransportOptions GetDefaultOptions(string transportType);
    }

    /// <summary>
    /// 传输层选项配置
    /// </summary>
    public class TransportOptions
    {
        /// <summary>
        /// 连接超时时间（毫秒）
        /// </summary>
        public int ConnectionTimeoutMs { get; set; } = 5000;

        /// <summary>
        /// 发送超时时间（毫秒）
        /// </summary>
        public int SendTimeoutMs { get; set; } = 10000;

        /// <summary>
        /// 接收超时时间（毫秒）
        /// </summary>
        public int ReceiveTimeoutMs { get; set; } = 10000;

        /// <summary>
        /// 发送缓冲区大小（字节）
        /// </summary>
        public int SendBufferSize { get; set; } = 65536;

        /// <summary>
        /// 接收缓冲区大小（字节）
        /// </summary>
        public int ReceiveBufferSize { get; set; } = 65536;

        /// <summary>
        /// 是否启用 Nagle 算法
        /// </summary>
        public bool NoDelay { get; set; } = true;

        /// <summary>
        /// 保持连接存活
        /// </summary>
        public bool KeepAlive { get; set; } = true;

        /// <summary>
        /// 重试次数
        /// </summary>
        public int MaxRetryAttempts { get; set; } = 3;

        /// <summary>
        /// 重试间隔（毫秒）
        /// </summary>
        public int RetryIntervalMs { get; set; } = 1000;

        /// <summary>
        /// 自定义配置项
        /// </summary>
        public Dictionary<string, object> CustomProperties { get; set; } = new();

        /// <summary>
        /// 获取自定义配置项
        /// </summary>
        /// <typeparam name="T">配置项类型</typeparam>
        /// <param name="key">配置键</param>
        /// <param name="defaultValue">默认值</param>
        /// <returns>配置值</returns>
        public T GetCustomProperty<T>(string key, T defaultValue = default!)
        {
            if (CustomProperties.TryGetValue(key, out var value) && value is T typedValue)
            {
                return typedValue;
            }
            return defaultValue;
        }

        /// <summary>
        /// 设置自定义配置项
        /// </summary>
        /// <param name="key">配置键</param>
        /// <param name="value">配置值</param>
        public void SetCustomProperty(string key, object value)
        {
            CustomProperties[key] = value;
        }
    }
}
