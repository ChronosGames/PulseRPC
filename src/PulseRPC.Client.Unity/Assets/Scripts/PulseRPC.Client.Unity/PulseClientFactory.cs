using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace PulseRPC.Client.Unity
{
    /// <summary>
    /// 用于创建客户端代理的工厂类
    /// </summary>
    public static class PulseClientFactory
    {
        /// <summary>
        /// 创建服务接口的客户端代理
        /// </summary>
        /// <typeparam name="TService">服务接口类型</typeparam>
        /// <param name="connection">要使用的连接</param>
        /// <returns>客户端代理实例</returns>
        public static TService Create<TService>(IPulseConnection connection)
            where TService : class, IPulseService<TService>
        {
            // 查找为此服务生成的代理类型
            string proxyTypeName = $"PulseRPC.Client.Unity.Generated.{typeof(TService).Name}ClientImpl";

            Type proxyType = Type.GetType(proxyTypeName);
            if (proxyType == null)
            {
                // 尝试在所有加载的程序集中查找
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    proxyType = assembly.GetType(proxyTypeName);
                    if (proxyType != null)
                        break;
                }
            }

            if (proxyType == null)
            {
                Debug.LogError($"无法找到服务 {typeof(TService).FullName} 的生成代理类型");
                throw new InvalidOperationException($"服务 {typeof(TService).FullName} 的客户端代理尚未生成或不可用");
            }

            try
            {
                // 创建代理实例
                return (TService)Activator.CreateInstance(proxyType, connection);
            }
            catch (Exception ex)
            {
                Debug.LogError($"创建代理实例时出错: {ex.Message}");
                throw new InvalidOperationException($"无法创建服务 {typeof(TService).FullName} 的客户端代理", ex);
            }
        }

        /// <summary>
        /// 连接到Hub并创建其客户端代理
        /// </summary>
        /// <typeparam name="THub">Hub接口类型</typeparam>
        /// <typeparam name="TReceiver">接收器实现类型</typeparam>
        /// <param name="connection">要使用的连接</param>
        /// <param name="receiver">客户端接收器实现</param>
        /// <returns>Hub客户端代理实例</returns>
        public static THub ConnectToHub<THub, TReceiver>(
            IPulseConnection connection,
            TReceiver receiver)
            where THub : class, IPulseHub<THub, TReceiver>
            where TReceiver : class
        {
            // 查找为此Hub生成的代理类型
            string proxyTypeName = $"PulseRPC.Client.Unity.Generated.{typeof(THub).Name}ClientImpl";

            Type proxyType = Type.GetType(proxyTypeName);
            if (proxyType == null)
            {
                // 尝试在所有加载的程序集中查找
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    proxyType = assembly.GetType(proxyTypeName);
                    if (proxyType != null)
                        break;
                }
            }

            if (proxyType == null)
            {
                Debug.LogError($"无法找到Hub {typeof(THub).FullName} 的生成代理类型");
                throw new InvalidOperationException($"Hub {typeof(THub).FullName} 的客户端代理尚未生成或不可用");
            }

            try
            {
                // 创建代理实例
                return (THub)Activator.CreateInstance(proxyType, connection, receiver);
            }
            catch (Exception ex)
            {
                Debug.LogError($"创建Hub代理实例时出错: {ex.Message}");
                throw new InvalidOperationException($"无法创建Hub {typeof(THub).FullName} 的客户端代理", ex);
            }
        }
    }
}
