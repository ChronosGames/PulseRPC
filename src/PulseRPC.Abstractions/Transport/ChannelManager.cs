using System;
using System.Collections.Generic;
using System.Linq;
using PulseRPC.Messaging;

namespace PulseRPC.Transport
{
    /// <summary>
    /// 通道管理器接口
    /// </summary>
    public interface IChannelManager : IDisposable
    {
        /// <summary>
        /// 获取通道
        /// </summary>
        IClientChannel GetChannel(string channelName);

        /// <summary>
        /// 获取默认通道
        /// </summary>
        IClientChannel GetDefaultChannel();

        /// <summary>
        /// 注册通道
        /// </summary>
        void RegisterChannel(string channelName, IClientChannel channel, bool isDefault = false);

        /// <summary>
        /// 注销通道
        /// </summary>
        void UnregisterChannel(string channelName);

        /// <summary>
        /// 检查通道是否存在
        /// </summary>
        bool HasChannel(string channelName);
    }

    /// <summary>
    /// 通道管理器实现
    /// </summary>
    public class ChannelManager : IChannelManager
    {
        private readonly Dictionary<string, IClientChannel> _channels = new();
        private string? _defaultChannelName;
        private readonly object _syncLock = new object();

        public IClientChannel GetChannel(string channelName)
        {
            lock (_syncLock)
            {
                if (_channels.TryGetValue(channelName, out var channel))
                {
                    return channel;
                }

                throw new ArgumentException($"Channel not found: {channelName}");
            }
        }

        public IClientChannel GetDefaultChannel()
        {
            lock (_syncLock)
            {
                if (string.IsNullOrEmpty(_defaultChannelName))
                {
                    throw new InvalidOperationException("No default channel registered");
                }

                return GetChannel(_defaultChannelName);
            }
        }

        public void RegisterChannel(string channelName, IClientChannel channel, bool isDefault = false)
        {
            lock (_syncLock)
            {
                if (!_channels.TryAdd(channelName, channel))
                {
                    throw new ArgumentException($"Channel already registered: {channelName}");
                }

                if (isDefault || string.IsNullOrEmpty(_defaultChannelName))
                {
                    _defaultChannelName = channelName;
                }
            }
        }

        public void UnregisterChannel(string channelName)
        {
            lock (_syncLock)
            {
                if (_channels.Remove(channelName) && channelName == _defaultChannelName)
                {
                    _defaultChannelName = _channels.Keys.FirstOrDefault();
                }
            }
        }

        public bool HasChannel(string channelName)
        {
            lock (_syncLock)
            {
                return _channels.ContainsKey(channelName);
            }
        }

        public void Dispose()
        {
            lock (_syncLock)
            {
                foreach (var channel in _channels.Values)
                {
                    channel.Dispose();
                }

                _channels.Clear();
                _defaultChannelName = null;
            }
        }
    }
}
