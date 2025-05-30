using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using PulseRPC.Benchmark.Core.Models;

namespace PulseRPC.Benchmark.Configuration
{
    /// <summary>
    /// 基准测试配置构建器
    /// </summary>
    public class BenchmarkConfigurationBuilder
    {
        private readonly List<IConfigurationSource> _sources = new();
        private BenchmarkConfiguration _configuration = new();

        /// <summary>
        /// 添加配置源
        /// </summary>
        /// <param name="source">配置源</param>
        /// <returns>构建器</returns>
        public BenchmarkConfigurationBuilder Add(IConfigurationSource source)
        {
            _sources.Add(source);
            return this;
        }

        /// <summary>
        /// 添加JSON文件配置
        /// </summary>
        /// <param name="path">文件路径</param>
        /// <param name="optional">是否可选</param>
        /// <param name="reloadOnChange">是否监听文件变化</param>
        /// <returns>构建器</returns>
        public BenchmarkConfigurationBuilder AddJsonFile(string path, bool optional = false, bool reloadOnChange = false)
        {
            var configBuilder = new ConfigurationBuilder();
            configBuilder.AddJsonFile(path, optional, reloadOnChange);

            var config = configBuilder.Build();
            ApplyConfiguration(config);

            return this;
        }

        /// <summary>
        /// 添加环境变量配置
        /// </summary>
        /// <param name="prefix">前缀</param>
        /// <returns>构建器</returns>
        public BenchmarkConfigurationBuilder AddEnvironmentVariables(string? prefix = null)
        {
            var configBuilder = new ConfigurationBuilder();
            configBuilder.AddEnvironmentVariables(prefix);

            var config = configBuilder.Build();
            ApplyConfiguration(config);

            return this;
        }

        /// <summary>
        /// 添加命令行参数配置
        /// </summary>
        /// <param name="args">命令行参数</param>
        /// <returns>构建器</returns>
        public BenchmarkConfigurationBuilder AddCommandLine(string[] args)
        {
            var configBuilder = new ConfigurationBuilder();
            configBuilder.AddCommandLine(args);

            var config = configBuilder.Build();
            ApplyConfiguration(config);

            return this;
        }

        /// <summary>
        /// 设置主机地址
        /// </summary>
        /// <param name="host">主机地址</param>
        /// <returns>构建器</returns>
        public BenchmarkConfigurationBuilder SetHost(string host)
        {
            _configuration.Host = host;
            return this;
        }

        /// <summary>
        /// 设置端口
        /// </summary>
        /// <param name="port">端口号</param>
        /// <returns>构建器</returns>
        public BenchmarkConfigurationBuilder SetPort(int port)
        {
            _configuration.Port = port;
            return this;
        }

        /// <summary>
        /// 设置传输类型
        /// </summary>
        /// <param name="transportType">传输类型</param>
        /// <returns>构建器</returns>
        public BenchmarkConfigurationBuilder SetTransportType(string transportType)
        {
            _configuration.TransportType = transportType;
            return this;
        }

        /// <summary>
        /// 设置并发连接数
        /// </summary>
        /// <param name="connections">并发连接数</param>
        /// <returns>构建器</returns>
        public BenchmarkConfigurationBuilder SetConcurrentConnections(int connections)
        {
            _configuration.ConcurrentConnections = connections;
            return this;
        }

        /// <summary>
        /// 设置测试持续时间
        /// </summary>
        /// <param name="durationSeconds">持续时间（秒）</param>
        /// <returns>构建器</returns>
        public BenchmarkConfigurationBuilder SetDuration(int durationSeconds)
        {
            _configuration.DurationSeconds = durationSeconds;
            return this;
        }

        /// <summary>
        /// 设置预热时间
        /// </summary>
        /// <param name="warmupSeconds">预热时间（秒）</param>
        /// <returns>构建器</returns>
        public BenchmarkConfigurationBuilder SetWarmup(int warmupSeconds)
        {
            _configuration.WarmupSeconds = warmupSeconds;
            return this;
        }

        /// <summary>
        /// 设置消息大小
        /// </summary>
        /// <param name="sizeBytes">消息大小（字节）</param>
        /// <returns>构建器</returns>
        public BenchmarkConfigurationBuilder SetMessageSize(int sizeBytes)
        {
            _configuration.MessageSizeBytes = sizeBytes;
            return this;
        }

        /// <summary>
        /// 设置请求间隔
        /// </summary>
        /// <param name="intervalMs">请求间隔（毫秒）</param>
        /// <returns>构建器</returns>
        public BenchmarkConfigurationBuilder SetRequestInterval(int intervalMs)
        {
            _configuration.RequestIntervalMs = intervalMs;
            return this;
        }

        /// <summary>
        /// 启用详细日志
        /// </summary>
        /// <param name="enable">是否启用</param>
        /// <returns>构建器</returns>
        public BenchmarkConfigurationBuilder EnableVerboseLogging(bool enable = true)
        {
            _configuration.EnableVerboseLogging = enable;
            return this;
        }

        /// <summary>
        /// 启用资源指标收集
        /// </summary>
        /// <param name="enable">是否启用</param>
        /// <returns>构建器</returns>
        public BenchmarkConfigurationBuilder EnableResourceMetrics(bool enable = true)
        {
            _configuration.CollectResourceMetrics = enable;
            return this;
        }

        /// <summary>
        /// 设置每连接操作数
        /// </summary>
        /// <param name="operations">操作数</param>
        /// <returns>构建器</returns>
        public BenchmarkConfigurationBuilder SetOperationsPerConnection(int operations)
        {
            _configuration.OperationsPerConnection = operations;
            return this;
        }

        /// <summary>
        /// 添加自定义配置
        /// </summary>
        /// <param name="key">配置键</param>
        /// <param name="value">配置值</param>
        /// <returns>构建器</returns>
        public BenchmarkConfigurationBuilder AddCustomConfig(string key, object value)
        {
            _configuration.SetCustomConfig(key, value);
            return this;
        }

        /// <summary>
        /// 构建配置
        /// </summary>
        /// <returns>基准测试配置</returns>
        public BenchmarkConfiguration Build()
        {
            return _configuration;
        }

        /// <summary>
        /// 从现有配置创建构建器
        /// </summary>
        /// <param name="existingConfig">现有配置</param>
        /// <returns>构建器</returns>
        public static BenchmarkConfigurationBuilder FromExisting(BenchmarkConfiguration existingConfig)
        {
            var builder = new BenchmarkConfigurationBuilder();
            builder._configuration = new BenchmarkConfiguration
            {
                Host = existingConfig.Host,
                Port = existingConfig.Port,
                TransportType = existingConfig.TransportType,
                ConcurrentConnections = existingConfig.ConcurrentConnections,
                DurationSeconds = existingConfig.DurationSeconds,
                WarmupSeconds = existingConfig.WarmupSeconds,
                OperationsPerConnection = existingConfig.OperationsPerConnection,
                MessageSizeBytes = existingConfig.MessageSizeBytes,
                RequestIntervalMs = existingConfig.RequestIntervalMs,
                EnableVerboseLogging = existingConfig.EnableVerboseLogging,
                CollectResourceMetrics = existingConfig.CollectResourceMetrics,
                TransportOptions = existingConfig.TransportOptions,
                CustomConfiguration = new Dictionary<string, object>(existingConfig.CustomConfiguration)
            };
            return builder;
        }

        /// <summary>
        /// 创建默认配置构建器
        /// </summary>
        /// <returns>构建器</returns>
        public static BenchmarkConfigurationBuilder CreateDefault()
        {
            return new BenchmarkConfigurationBuilder();
        }

        /// <summary>
        /// 应用IConfiguration到当前配置
        /// </summary>
        /// <param name="config">配置</param>
        private void ApplyConfiguration(IConfiguration config)
        {
            // 基础配置
            if (!string.IsNullOrEmpty(config["Host"]))
                _configuration.Host = config["Host"]!;

            if (int.TryParse(config["Port"], out var port))
                _configuration.Port = port;

            if (!string.IsNullOrEmpty(config["TransportType"]))
                _configuration.TransportType = config["TransportType"]!;

            if (int.TryParse(config["ConcurrentConnections"], out var connections))
                _configuration.ConcurrentConnections = connections;

            if (int.TryParse(config["DurationSeconds"], out var duration))
                _configuration.DurationSeconds = duration;

            if (int.TryParse(config["WarmupSeconds"], out var warmup))
                _configuration.WarmupSeconds = warmup;

            if (int.TryParse(config["MessageSizeBytes"], out var messageSize))
                _configuration.MessageSizeBytes = messageSize;

            if (int.TryParse(config["RequestIntervalMs"], out var interval))
                _configuration.RequestIntervalMs = interval;

            if (bool.TryParse(config["EnableVerboseLogging"], out var verbose))
                _configuration.EnableVerboseLogging = verbose;

            if (bool.TryParse(config["CollectResourceMetrics"], out var metrics))
                _configuration.CollectResourceMetrics = metrics;

            if (int.TryParse(config["OperationsPerConnection"], out var operations))
                _configuration.OperationsPerConnection = operations;

            // 自定义配置
            var customSection = config.GetSection("CustomConfiguration");
            foreach (var item in customSection.GetChildren())
            {
                if (item.Value != null)
                {
                    _configuration.SetCustomConfig(item.Key, item.Value);
                }
            }
        }
    }
}
