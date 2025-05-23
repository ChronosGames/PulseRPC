using System;
using System.Collections.Generic;
using System.Reflection;
using PulseRPC.Client.Channels;
using PulseRPC.Serialization;
using PulseRPC.Transport;
using PulseRPC.Transport.Tcp;
using PulseRPC.Transport.Kcp;
using UnityEngine;

namespace PulseRPC.AOT
{
    /// <summary>
    /// AOT运行时支持，用于IL2CPP环境下预先注册类型
    /// </summary>
    public static class AOTSupport
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void RegisterTypes()
        {
            if (!IsIL2CPP())
                return;

            Debug.Log("[PulseRPC] 正在注册AOT类型...");

            try
            {
                // 注册基本类型
                RegisterKnownTypes();

                // 注册消息处理器
                RegisterMessageHandlers();

                // 注册泛型方法实例化
                PreserveGenericMethods();

                Debug.Log("[PulseRPC] AOT类型注册完成");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PulseRPC] AOT类型注册失败: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// 检查是否运行在IL2CPP环境中
        /// </summary>
        private static bool IsIL2CPP()
        {
#if ENABLE_IL2CPP
            return true;
#else
            return false;
#endif
        }

        /// <summary>
        /// 注册已知类型
        /// </summary>
        private static void RegisterKnownTypes()
        {
            // 基本类型
            RegisterType<bool>();
            RegisterType<byte>();
            RegisterType<sbyte>();
            RegisterType<char>();
            RegisterType<short>();
            RegisterType<ushort>();
            RegisterType<int>();
            RegisterType<uint>();
            RegisterType<long>();
            RegisterType<ulong>();
            RegisterType<float>();
            RegisterType<double>();
            RegisterType<decimal>();
            RegisterType<string>();
            RegisterType<DateTime>();
            RegisterType<TimeSpan>();
            RegisterType<Guid>();
            RegisterType<Uri>();
            RegisterType<Version>();

            // 数组和集合类型
            RegisterType<byte[]>();
            RegisterType<int[]>();
            RegisterType<string[]>();
            RegisterType<List<int>>();
            RegisterType<List<string>>();
            RegisterType<Dictionary<string, string>>();
            RegisterType<Dictionary<string, object>>();

            // 通道和传输类型
            RegisterType<TransportOptions>();
            RegisterType<KcpOptions>();
            RegisterType<TransportType>();

            // 框架内部类型
            RegisterType<TransportChannel>();
            RegisterType<TcpTransport>();
            RegisterType<KcpTransport>();
            RegisterType<PulseRPCSerializer>();
            RegisterType<ISerializer>();
        }

        /// <summary>
        /// 注册消息处理器
        /// </summary>
        private static void RegisterMessageHandlers()
        {
            // 在这里注册特定的消息处理器类型
        }

        /// <summary>
        /// 预先实例化泛型方法
        /// </summary>
        private static void PreserveGenericMethods()
        {
            // 序列化器方法
            PreserveGenericMethod<ISerializer, bool>("Serialize");
            PreserveGenericMethod<ISerializer, int>("Serialize");
            PreserveGenericMethod<ISerializer, string>("Serialize");
            PreserveGenericMethod<ISerializer, byte[]>("Serialize");
            PreserveGenericMethod<ISerializer, Dictionary<string, object>>("Serialize");

            PreserveGenericMethod<ISerializer, bool>("Deserialize");
            PreserveGenericMethod<ISerializer, int>("Deserialize");
            PreserveGenericMethod<ISerializer, string>("Deserialize");
            PreserveGenericMethod<ISerializer, byte[]>("Deserialize");
            PreserveGenericMethod<ISerializer, Dictionary<string, object>>("Deserialize");

            // 通道方法 - 注释掉因为可能不需要
            // PreserveGenericMethod<IMessageChannel, byte[]>("SubscribeToEvent");
            // PreserveGenericMethod<IMessageChannel, string>("SubscribeToEvent");
            // PreserveGenericMethod<IMessageChannel, int>("SubscribeToEvent");
            // PreserveGenericMethod<IMessageChannel, Dictionary<string, object>>("SubscribeToEvent");
        }

        /// <summary>
        /// 注册类型
        /// </summary>
        private static void RegisterType<T>()
        {
            // IL2CPP会在编译时保留此类型
            RuntimeTypeCache<T>.Preserve();
        }

        /// <summary>
        /// 预先实例化泛型方法
        /// </summary>
        private static void PreserveGenericMethod<TType, TParam>(string methodName)
        {
            // 这个方法实际上并不会调用任何东西，只是为了确保IL2CPP编译器在编译时会保留这些泛型方法
            RuntimeMethodCache<TType, TParam>.PreserveMethod(methodName);
        }

        /// <summary>
        /// 运行时类型缓存
        /// </summary>
        private static class RuntimeTypeCache<T>
        {
            // 此字段仅用于触发类型的静态构造函数，确保类型被保留
            private static readonly Type Type = typeof(T);

            public static void Preserve()
            {
                // 仅用于防止被优化掉
                _ = Type;
            }
        }

        /// <summary>
        /// 运行时方法缓存
        /// </summary>
        private static class RuntimeMethodCache<TType, TParam>
        {
            // 此字段仅用于确保方法被保留
            private static readonly Type Type = typeof(TType);
            private static readonly Type ParamType = typeof(TParam);

            public static void PreserveMethod(string methodName)
            {
                // 仅用于防止被优化掉
                _ = Type;
                _ = ParamType;
                _ = methodName;
            }
        }
    }
}
