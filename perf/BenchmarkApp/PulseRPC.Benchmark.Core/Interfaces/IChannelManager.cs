using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC.Benchmark.Core.Interfaces
{
    /// <summary>
    /// 通道管理器接口，负责管理多个传输通道
    /// </summary>
    public interface IChannelManager : IDisposable
    {
        /// <summary>
        /// 活跃通道数量
        /// </summary>
        int ActiveChannelCount { get; }

        /// <summary>
        /// 所有通道列表
        /// </summary>
        IReadOnlyList<IBenchmarkTransport> Channels { get; }

        /// <summary>
        /// 创建新的传输通道
        /// </summary>
        /// <param name="transportType">传输类型</param>
        /// <param name="channelId">通道ID</param>
        /// <returns>创建的传输通道</returns>
        IBenchmarkTransport CreateChannel(string transportType, string channelId);

        /// <summary>
        /// 连接所有通道到指定主机
        /// </summary>
        /// <param name="host">主机地址</param>
        /// <param name="port">端口号</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>连接任务</returns>
        Task ConnectAllAsync(string host, int port, CancellationToken cancellationToken = default);

        /// <summary>
        /// 断开所有通道连接
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>断开连接任务</returns>
        Task DisconnectAllAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 移除指定通道
        /// </summary>
        /// <param name="channelId">通道ID</param>
        /// <returns>是否成功移除</returns>
        Task<bool> RemoveChannelAsync(string channelId);

        /// <summary>
        /// 获取指定通道
        /// </summary>
        /// <param name="channelId">通道ID</param>
        /// <returns>通道实例，如果不存在则返回null</returns>
        IBenchmarkTransport? GetChannel(string channelId);

        /// <summary>
        /// 获取聚合统计信息
        /// </summary>
        /// <returns>所有通道的聚合统计</returns>
        TransportStatistics GetAggregatedStatistics();

        /// <summary>
        /// 通道状态变化事件
        /// </summary>
        event Action<ChannelStateChangedEventArgs>? ChannelStateChanged;
    }

    /// <summary>
    /// 通道状态变化事件参数
    /// </summary>
    public class ChannelStateChangedEventArgs : EventArgs
    {
        public string ChannelId { get; }
        public bool IsConnected { get; }
        public string? ErrorMessage { get; }

        public ChannelStateChangedEventArgs(string channelId, bool isConnected, string? errorMessage = null)
        {
            ChannelId = channelId;
            IsConnected = isConnected;
            ErrorMessage = errorMessage;
        }
    }
}
