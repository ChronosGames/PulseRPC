using System;
using UnityEngine;

namespace PulseRPC.Client.Channels
{
    /// <summary>
    /// 传输通道（Unity 简化版本）
    /// </summary>
    public class TransportChannel
    {
        /// <summary>
        /// 通道名称
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// 是否已连接
        /// </summary>
        public bool IsConnected { get; private set; }

        public TransportChannel(string name)
        {
            Name = name;
            Debug.Log($"[PulseRPC] 创建传输通道: {name}");
        }

        /// <summary>
        /// 连接到服务器
        /// </summary>
        public void Connect()
        {
            IsConnected = true;
            Debug.Log($"[PulseRPC] 通道 {Name} 已连接");
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        public void Disconnect()
        {
            IsConnected = false;
            Debug.Log($"[PulseRPC] 通道 {Name} 已断开");
        }
    }
}
