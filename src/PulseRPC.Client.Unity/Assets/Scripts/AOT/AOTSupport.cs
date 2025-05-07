using System;
using System.Collections.Generic;
using MemoryPack;
using PulseRPC.Protocol.Serialization;
using UnityEngine;

namespace PulseRPC.AOT
{
    /// <summary>
    /// AOT支持类，用于在IL2CPP环境中预先注册所有需要序列化的类型
    /// </summary>
    public static class AOTSupport
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void RegisterTypes()
        {
            Debug.Log("PulseRPC: 注册AOT类型");

            // 注册基本类型的序列化器
            RegisterMemoryPackFormatters();
        }

        /// <summary>
        /// 注册MemoryPack格式化器，确保IL2CPP环境下可以正常工作
        /// </summary>
        private static void RegisterMemoryPackFormatters()
        {
            // 基本类型
            MemoryPackFormatterProvider.Register<int>();
            MemoryPackFormatterProvider.Register<long>();
            MemoryPackFormatterProvider.Register<string>();
            MemoryPackFormatterProvider.Register<bool>();
            MemoryPackFormatterProvider.Register<float>();
            MemoryPackFormatterProvider.Register<double>();
            MemoryPackFormatterProvider.Register<DateTime>();
            MemoryPackFormatterProvider.Register<Guid>();

            // 集合类型
            MemoryPackFormatterProvider.Register<List<int>>();
            MemoryPackFormatterProvider.Register<List<string>>();
            MemoryPackFormatterProvider.Register<Dictionary<string, string>>();
            MemoryPackFormatterProvider.Register<Dictionary<int, string>>();

            // 数组类型
            MemoryPackFormatterProvider.Register<int[]>();
            MemoryPackFormatterProvider.Register<string[]>();
            MemoryPackFormatterProvider.Register<byte[]>();

            // 可空类型
            MemoryPackFormatterProvider.Register<int?>();
            MemoryPackFormatterProvider.Register<long?>();
            MemoryPackFormatterProvider.Register<DateTime?>();

            // 注意：具体的消息类型会由代码生成器自动注册
        }
    }
}
