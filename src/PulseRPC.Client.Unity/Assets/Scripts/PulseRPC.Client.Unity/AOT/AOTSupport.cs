using System;
using System.Buffers;
using System.Collections.Generic;
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
            RegisterType<object[]>();
            RegisterType<List<int>>();
            RegisterType<List<string>>();
            RegisterType<List<object>>();
            RegisterType<List<byte>>();
            RegisterType<Dictionary<string, string>>();
            RegisterType<Dictionary<string, object>>();
            RegisterType<Dictionary<string, int>>();
            RegisterType<Dictionary<string, bool>>();
            RegisterType<Dictionary<int, object>>();

            // 通道和传输类型
            RegisterType<TransportOptions>();
            RegisterType<TransportType>();

            // 框架内部类型
            RegisterType<TcpTransport>();
            RegisterType<KcpTransport>();
            RegisterType<ISerializer>();

            // Memory 和 Buffer 类型
            RegisterType<ReadOnlyMemory<byte>>();
            RegisterType<Memory<byte>>();
            RegisterType<ArraySegment<byte>>();
            RegisterType<System.Buffers.ArrayBufferWriter<byte>>();

            // Task 类型组合
            RegisterType<System.Threading.Tasks.Task<bool>>();
            RegisterType<System.Threading.Tasks.Task<int>>();
            RegisterType<System.Threading.Tasks.Task<string>>();
            RegisterType<System.Threading.Tasks.Task<object>>();
            RegisterType<System.Threading.Tasks.Task<byte[]>>();
            RegisterType<System.Threading.Tasks.ValueTask<bool>>();
            RegisterType<System.Threading.Tasks.ValueTask<ReadOnlyMemory<byte>>>();

            // TaskCompletionSource 类型
            RegisterType<System.Threading.Tasks.TaskCompletionSource<object>>();
            RegisterType<System.Threading.Tasks.TaskCompletionSource<bool>>();

            // 委托类型
            RegisterType<Action<ReadOnlyMemory<byte>>>();
            RegisterType<Action<string, byte[]>>();
            RegisterType<Action<object>>();
            RegisterType<Func<bool>>();
            RegisterType<Func<object>>();
            RegisterType<Func<System.Threading.CancellationToken, System.Threading.Tasks.Task<bool>>>();
            RegisterType<Func<System.Threading.CancellationToken, System.Threading.Tasks.Task<object>>>();
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
            // 序列化器方法 - Serialize<T>
            PreserveGenericMethod<ISerializer, bool>("Serialize");
            PreserveGenericMethod<ISerializer, int>("Serialize");
            PreserveGenericMethod<ISerializer, long>("Serialize");
            PreserveGenericMethod<ISerializer, string>("Serialize");
            PreserveGenericMethod<ISerializer, byte[]>("Serialize");
            PreserveGenericMethod<ISerializer, object>("Serialize");
            PreserveGenericMethod<ISerializer, Dictionary<string, object>>("Serialize");
            PreserveGenericMethod<ISerializer, Dictionary<string, string>>("Serialize");
            PreserveGenericMethod<ISerializer, List<string>>("Serialize");
            PreserveGenericMethod<ISerializer, List<int>>("Serialize");

            // 序列化器方法 - Deserialize<T>
            PreserveGenericMethod<ISerializer, bool>("Deserialize");
            PreserveGenericMethod<ISerializer, int>("Deserialize");
            PreserveGenericMethod<ISerializer, long>("Deserialize");
            PreserveGenericMethod<ISerializer, string>("Deserialize");
            PreserveGenericMethod<ISerializer, byte[]>("Deserialize");
            PreserveGenericMethod<ISerializer, object>("Deserialize");
            PreserveGenericMethod<ISerializer, Dictionary<string, object>>("Deserialize");
            PreserveGenericMethod<ISerializer, Dictionary<string, string>>("Deserialize");
            PreserveGenericMethod<ISerializer, List<string>>("Deserialize");
            PreserveGenericMethod<ISerializer, List<int>>("Deserialize");

            // 通道方法 - InvokeAsync<TRequest, TResponse>
            PreserveGenericMethod2<ITransport, object, object>("InvokeAsync");

            // 通道方法 - SendEventAsync<T>
            // PreserveGenericMethod<TransportChannel, object>("SendEventAsync");
            // PreserveGenericMethod<TransportChannel, string>("SendEventAsync");
            // PreserveGenericMethod<TransportChannel, int>("SendEventAsync");
            // PreserveGenericMethod<TransportChannel, byte[]>("SendEventAsync");
            // PreserveGenericMethod<TransportChannel, Dictionary<string, object>>("SendEventAsync");

            // 通道方法 - SubscribeToEvent<T>
            // PreserveGenericMethod<TransportChannel, object>("SubscribeToEvent");
            // PreserveGenericMethod<TransportChannel, string>("SubscribeToEvent");
            // PreserveGenericMethod<TransportChannel, int>("SubscribeToEvent");
            // PreserveGenericMethod<TransportChannel, byte[]>("SubscribeToEvent");
            // PreserveGenericMethod<TransportChannel, Dictionary<string, object>>("SubscribeToEvent");

            // Task 返回值类型方法
            PreserveGenericMethod<System.Threading.Tasks.Task<bool>, bool>("Result");
            PreserveGenericMethod<System.Threading.Tasks.Task<int>, int>("Result");
            PreserveGenericMethod<System.Threading.Tasks.Task<object>, object>("Result");
            PreserveGenericMethod<System.Threading.Tasks.Task<string>, string>("Result");

            // ValueTask 返回值类型方法
            PreserveGenericMethod<System.Threading.Tasks.ValueTask<bool>, bool>("Result");
            PreserveGenericMethod<System.Threading.Tasks.ValueTask<ReadOnlyMemory<byte>>, ReadOnlyMemory<byte>>("Result");
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
        /// 预先实例化泛型方法（单个类型参数）
        /// </summary>
        private static void PreserveGenericMethod<TType, TParam>(string methodName)
        {
            // 这个方法实际上并不会调用任何东西，只是为了确保IL2CPP编译器在编译时会保留这些泛型方法
            RuntimeMethodCache<TType, TParam>.PreserveMethod(methodName);
        }

        /// <summary>
        /// 预先实例化泛型方法（两个类型参数）
        /// </summary>
        private static void PreserveGenericMethod2<TType, TParam1, TParam2>(string methodName)
        {
            // 这个方法实际上并不会调用任何东西，只是为了确保IL2CPP编译器在编译时会保留这些泛型方法
            RuntimeMethodCache2<TType, TParam1, TParam2>.PreserveMethod(methodName);
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
        /// 运行时方法缓存（单个类型参数）
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

        /// <summary>
        /// 运行时方法缓存（两个类型参数）
        /// </summary>
        private static class RuntimeMethodCache2<TType, TParam1, TParam2>
        {
            // 此字段仅用于确保方法被保留
            private static readonly Type Type = typeof(TType);
            private static readonly Type ParamType1 = typeof(TParam1);
            private static readonly Type ParamType2 = typeof(TParam2);

            public static void PreserveMethod(string methodName)
            {
                // 仅用于防止被优化掉
                _ = Type;
                _ = ParamType1;
                _ = ParamType2;
                _ = methodName;
            }
        }
    }
}
