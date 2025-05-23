using System;
using System.Linq;
using UnityEngine;

namespace PulseRPC.Client
{
    /// <summary>
    /// 通道管理器扩展方法
    /// </summary>
    public static class ChannelManagerExtensions
    {
        /// <summary>
        /// 获取 PlayerService 代理实例
        /// </summary>
        public static T GetPlayerService<T>(this IChannelManager channelManager) where T : class
        {
            return GetService<T>(channelManager, "PlayerService");
        }

        /// <summary>
        /// 获取 ChatService 代理实例
        /// </summary>
        public static T GetChatService<T>(this IChannelManager channelManager) where T : class
        {
            return GetService<T>(channelManager, "ChatService");
        }

        /// <summary>
        /// 获取泛型服务代理实例
        /// </summary>
        public static T GetService<T>(this IChannelManager channelManager, string serviceName = null) where T : class
        {
            if (channelManager == null)
                throw new ArgumentNullException(nameof(channelManager));

            // 如果未指定服务名称，则使用接口类型名称
            if (string.IsNullOrEmpty(serviceName))
            {
                serviceName = typeof(T).Name;
                if (serviceName.StartsWith("I"))
                    serviceName = serviceName.Substring(1);
            }

            // 查找代理类型
            var proxyTypeName = $"PulseRPC.Generated.{serviceName}Proxy";
            var proxyType = FindProxyType(proxyTypeName);

            if (proxyType == null)
            {
                Debug.LogError($"找不到服务代理类型: {proxyTypeName}");
                return null;
            }

            try
            {
                // 获取默认通道
                var channel = channelManager.GetDefaultChannel();

                // 创建代理实例
                var proxy = Activator.CreateInstance(proxyType, channel) as T;
                return proxy;
            }
            catch (Exception ex)
            {
                Debug.LogError($"创建服务代理失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 查找代理类型
        /// </summary>
        private static Type FindProxyType(string typeName)
        {
            // 在所有已加载的程序集中查找
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var type = assembly.GetType(typeName);
                    if (type != null)
                        return type;
                }
                catch
                {
                    // 忽略异常，继续查找
                }
            }

            // 在 PulseRPC.Generated 程序集中查找
            var generatedAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "PulseRPC.Generated");

            if (generatedAssembly != null)
            {
                try
                {
                    foreach (var type in generatedAssembly.GetTypes())
                    {
                        if (type.FullName == typeName)
                            return type;
                    }
                }
                catch
                {
                    // 忽略异常
                }
            }

            return null;
        }
    }
}
