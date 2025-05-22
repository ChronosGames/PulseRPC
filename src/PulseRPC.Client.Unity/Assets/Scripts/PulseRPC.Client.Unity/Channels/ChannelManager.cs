using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace PulseRPC.Client.Channels
{
    /// <summary>
    /// 通道管理器接口
    /// </summary>
    public interface IChannelManager : IDisposable
    {
        /// <summary>
        /// 注册通道
        /// </summary>
        /// <param name="name">通道名称</param>
        /// <param name="channel">通道实例</param>
        /// <param name="isDefault">是否为默认通道</param>
        void RegisterChannel(string name, IMessageChannel channel, bool isDefault = false);

        /// <summary>
        /// 获取通道
        /// </summary>
        /// <param name="name">通道名称</param>
        /// <returns>通道实例</returns>
        IMessageChannel GetChannel(string name);

        /// <summary>
        /// 获取默认通道
        /// </summary>
        /// <returns>默认通道实例</returns>
        IMessageChannel GetDefaultChannel();

        /// <summary>
        /// 检查通道是否已注册
        /// </summary>
        /// <param name="name">通道名称</param>
        /// <returns>是否已注册</returns>
        bool HasChannel(string name);

        /// <summary>
        /// 移除通道
        /// </summary>
        /// <param name="name">通道名称</param>
        void RemoveChannel(string name);
    }

    /// <summary>
    /// 通道管理器实现
    /// </summary>
    public class ChannelManager : IChannelManager
    {
        private readonly Dictionary<string, IMessageChannel> _channels = new Dictionary<string, IMessageChannel>();
        private string _defaultChannelName;
        private bool _isDisposed;

        /// <summary>
        /// 注册通道
        /// </summary>
        public void RegisterChannel(string name, IMessageChannel channel, bool isDefault = false)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("通道名称不能为空", nameof(name));

            if (channel == null)
                throw new ArgumentNullException(nameof(channel));

            if (_channels.ContainsKey(name))
                throw new InvalidOperationException($"通道 '{name}' 已存在");

            _channels[name] = channel;

            if (isDefault || string.IsNullOrEmpty(_defaultChannelName))
            {
                _defaultChannelName = name;
            }
        }

        /// <summary>
        /// 获取通道
        /// </summary>
        public IMessageChannel GetChannel(string name)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("通道名称不能为空", nameof(name));

            if (!_channels.TryGetValue(name, out var channel))
                throw new KeyNotFoundException($"找不到名为 '{name}' 的通道");

            return channel;
        }

        /// <summary>
        /// 获取默认通道
        /// </summary>
        public IMessageChannel GetDefaultChannel()
        {
            if (string.IsNullOrEmpty(_defaultChannelName) || !_channels.ContainsKey(_defaultChannelName))
                throw new InvalidOperationException("未设置默认通道");

            return _channels[_defaultChannelName];
        }

        /// <summary>
        /// 检查通道是否已注册
        /// </summary>
        public bool HasChannel(string name)
        {
            return !string.IsNullOrEmpty(name) && _channels.ContainsKey(name);
        }

        /// <summary>
        /// 移除通道
        /// </summary>
        public void RemoveChannel(string name)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("通道名称不能为空", nameof(name));

            if (!_channels.ContainsKey(name))
                return;

            var channel = _channels[name];
            _channels.Remove(name);

            if (name == _defaultChannelName)
            {
                _defaultChannelName = _channels.Count > 0 ? _channels.Keys.GetEnumerator().Current : null;
            }

            if (channel is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        /// <summary>
        /// 获取特定服务的代理
        /// </summary>
        public T GetService<T>() where T : class
        {
            // 这里实现服务代理的自动创建
            var channelName = typeof(T).Name;
            var defaultChannel = GetDefaultChannel();

            // 此处根据实际实现来创建服务代理
            // 在实际项目中，这可能涉及到使用Source Generator生成的代码
            return null;
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed)
                return;

            foreach (var channel in _channels.Values)
            {
                if (channel is IDisposable disposable)
                {
                    try
                    {
                        disposable.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"释放通道时出错: {ex.Message}");
                    }
                }
            }

            _channels.Clear();
            _defaultChannelName = null;
            _isDisposed = true;
        }
    }
}
