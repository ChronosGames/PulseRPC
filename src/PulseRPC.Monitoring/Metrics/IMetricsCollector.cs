namespace PulseRPC.Monitoring.Metrics
{
    /// <summary>
    /// 指标收集器接口
    /// </summary>
    public interface IMetricsCollector
    {
        /// <summary>
        /// 获取或创建计数器
        /// </summary>
        /// <param name="name">指标名称</param>
        /// <param name="description">指标描述</param>
        /// <param name="tags">标签</param>
        /// <returns>计数器实例</returns>
        ICounter GetCounter(string name, string? description = null, IDictionary<string, string>? tags = null);

        /// <summary>
        /// 获取或创建仪表
        /// </summary>
        /// <param name="name">指标名称</param>
        /// <param name="description">指标描述</param>
        /// <param name="tags">标签</param>
        /// <returns>仪表实例</returns>
        IGauge GetGauge(string name, string? description = null, IDictionary<string, string>? tags = null);

        /// <summary>
        /// 获取或创建直方图
        /// </summary>
        /// <param name="name">指标名称</param>
        /// <param name="description">指标描述</param>
        /// <param name="buckets">分桶配置</param>
        /// <param name="tags">标签</param>
        /// <returns>直方图实例</returns>
        IHistogram GetHistogram(string name, string? description = null, double[]? buckets = null, IDictionary<string, string>? tags = null);

        /// <summary>
        /// 获取或创建计时器
        /// </summary>
        /// <param name="name">指标名称</param>
        /// <param name="description">指标描述</param>
        /// <param name="tags">标签</param>
        /// <returns>计时器实例</returns>
        ITimer GetTimer(string name, string? description = null, IDictionary<string, string>? tags = null);

        /// <summary>
        /// 记录RPC调用指标
        /// </summary>
        /// <param name="serviceName">服务名称</param>
        /// <param name="methodName">方法名称</param>
        /// <param name="status">调用状态</param>
        /// <param name="duration">调用耗时</param>
        /// <param name="requestSize">请求大小</param>
        /// <param name="responseSize">响应大小</param>
        void RecordRpcCall(string serviceName, string methodName, string status, TimeSpan duration, long requestSize = 0, long responseSize = 0);

        /// <summary>
        /// 记录负载均衡指标
        /// </summary>
        /// <param name="strategy">负载均衡策略</param>
        /// <param name="serviceName">服务名称</param>
        /// <param name="endpointCount">可用端点数</param>
        /// <param name="selectionTime">选择耗时</param>
        void RecordLoadBalancing(string strategy, string serviceName, int endpointCount, TimeSpan selectionTime);

        /// <summary>
        /// 记录服务发现指标
        /// </summary>
        /// <param name="discoveryType">发现类型</param>
        /// <param name="serviceName">服务名称</param>
        /// <param name="endpointCount">发现的端点数</param>
        /// <param name="discoveryTime">发现耗时</param>
        /// <param name="cacheHit">是否命中缓存</param>
        void RecordServiceDiscovery(string discoveryType, string serviceName, int endpointCount, TimeSpan discoveryTime, bool cacheHit = false);

        /// <summary>
        /// 记录健康检查指标
        /// </summary>
        /// <param name="serviceName">服务名称</param>
        /// <param name="endpointId">端点ID</param>
        /// <param name="status">健康状态</param>
        /// <param name="checkTime">检查耗时</param>
        void RecordHealthCheck(string serviceName, string endpointId, string status, TimeSpan checkTime);

        /// <summary>
        /// 记录连接池指标
        /// </summary>
        /// <param name="poolName">连接池名称</param>
        /// <param name="activeConnections">活跃连接数</param>
        /// <param name="idleConnections">空闲连接数</param>
        /// <param name="totalConnections">总连接数</param>
        /// <param name="waitTime">等待连接时间</param>
        void RecordConnectionPool(string poolName, int activeConnections, int idleConnections, int totalConnections, TimeSpan? waitTime = null);

        /// <summary>
        /// 获取所有指标快照
        /// </summary>
        /// <returns>指标快照</returns>
        MetricsSnapshot GetSnapshot();

        /// <summary>
        /// 重置所有指标
        /// </summary>
        void Reset();
    }

    /// <summary>
    /// 计数器接口
    /// </summary>
    public interface ICounter
    {
        /// <summary>
        /// 递增计数
        /// </summary>
        /// <param name="value">递增值</param>
        void Increment(double value = 1.0);

        /// <summary>
        /// 获取当前值
        /// </summary>
        double Value { get; }

        /// <summary>
        /// 获取快照
        /// </summary>
        CounterSnapshot GetSnapshot();

        /// <summary>
        /// 重置计数器
        /// </summary>
        void Reset();
    }

    /// <summary>
    /// 仪表接口
    /// </summary>
    public interface IGauge
    {
        /// <summary>
        /// 设置值
        /// </summary>
        /// <param name="value">值</param>
        void Set(double value);

        /// <summary>
        /// 递增值
        /// </summary>
        /// <param name="value">递增量</param>
        void Increment(double value = 1.0);

        /// <summary>
        /// 递减值
        /// </summary>
        /// <param name="value">递减量</param>
        void Decrement(double value = 1.0);

        /// <summary>
        /// 获取当前值
        /// </summary>
        double Value { get; }

        /// <summary>
        /// 获取快照
        /// </summary>
        GaugeSnapshot GetSnapshot();

        /// <summary>
        /// 重置仪表
        /// </summary>
        void Reset();
    }

    /// <summary>
    /// 直方图接口
    /// </summary>
    public interface IHistogram
    {
        /// <summary>
        /// 观察值
        /// </summary>
        /// <param name="value">观察值</param>
        void Observe(double value);

        /// <summary>
        /// 获取样本数量
        /// </summary>
        long Count { get; }

        /// <summary>
        /// 获取总和
        /// </summary>
        double Sum { get; }

        /// <summary>
        /// 获取快照
        /// </summary>
        HistogramSnapshot GetSnapshot();

        /// <summary>
        /// 重置直方图
        /// </summary>
        void Reset();
    }

    /// <summary>
    /// 计时器接口
    /// </summary>
    public interface ITimer
    {
        /// <summary>
        /// 记录时间
        /// </summary>
        /// <param name="duration">持续时间</param>
        void Record(TimeSpan duration);

        /// <summary>
        /// 开始计时
        /// </summary>
        /// <returns>计时上下文</returns>
        ITimerContext StartTimer();

        /// <summary>
        /// 获取快照
        /// </summary>
        TimerSnapshot GetSnapshot();

        /// <summary>
        /// 重置计时器
        /// </summary>
        void Reset();
    }

    /// <summary>
    /// 计时器上下文接口
    /// </summary>
    public interface ITimerContext : IDisposable
    {
        /// <summary>
        /// 停止计时并记录结果
        /// </summary>
        /// <returns>耗时</returns>
        TimeSpan Stop();
    }
} 