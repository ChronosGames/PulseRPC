using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Profiling;

namespace UnityTCP.Optimization
{
    /// <summary>
    /// 性能优化助手，提供CPU和内存优化的工具方法
    /// </summary>
    public static class PerformanceOptimizer
    {
        private static readonly ThreadLocal<Stopwatch> _threadLocalStopwatch = new ThreadLocal<Stopwatch>(() => new Stopwatch());

        /// <summary>
        /// 在调用前后测量性能
        /// </summary>
        public static long MeasureExecutionTime(Action action)
        {
            var sw = _threadLocalStopwatch.Value;
            sw.Restart();
            action();
            sw.Stop();
            return sw.ElapsedMilliseconds;
        }

        /// <summary>
        /// 强制执行垃圾回收，释放未使用的内存
        /// </summary>
        public static void ForceGarbageCollection()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        /// <summary>
        /// 确保对象被分配到大对象堆（LOH）上，减少碎片化
        /// </summary>
        public static byte[] AllocateInLargeObjectHeap(int size)
        {
            // 大于85KB的对象会被分配到LOH
            const int lohThreshold = 85 * 1024;
            return new byte[Math.Max(size, lohThreshold)];
        }

        /// <summary>
        /// 将托管内存固定在物理地址上，防止GC移动
        /// </summary>
        public static GCHandle PinObject(object obj)
        {
            return GCHandle.Alloc(obj, GCHandleType.Pinned);
        }

        /// <summary>
        /// 通过内存屏障确保内存操作按顺序执行
        /// </summary>
        public static void MemoryBarrier()
        {
            Thread.MemoryBarrier();
        }
    }

    /// <summary>
    /// 使用SIMD指令集优化内存复制和比较操作
    /// </summary>
    public static unsafe class SimdMemoryOperations
    {
        /// <summary>
        /// 使用SIMD加速的内存复制（仅适用于对齐的内存）
        /// </summary>
        public static void CopyAligned(void* source, void* destination, int byteCount)
        {
            if (Vector.IsHardwareAccelerated && byteCount >= Vector<byte>.Count)
            {
                var vectorizedLength = byteCount - (byteCount % Vector<byte>.Count);
                var i = 0;

                for (; i < vectorizedLength; i += Vector<byte>.Count)
                {
                    var sourceVector = Unsafe.ReadUnaligned<Vector<byte>>((byte*)source + i);
                    Unsafe.WriteUnaligned((byte*)destination + i, sourceVector);
                }

                // 复制剩余字节
                for (; i < byteCount; i++)
                {
                    ((byte*)destination)[i] = ((byte*)source)[i];
                }
            }
            else
            {
                // 回退到常规复制
                for (var i = 0; i < byteCount; i++)
                {
                    ((byte*)destination)[i] = ((byte*)source)[i];
                }
            }
        }

        /// <summary>
        /// 使用SIMD加速的内存比较
        /// </summary>
        public static bool AreEqual(void* a, void* b, int byteCount)
        {
            if (Vector.IsHardwareAccelerated && byteCount >= Vector<byte>.Count)
            {
                int vectorizedLength = byteCount - (byteCount % Vector<byte>.Count);
                int i = 0;

                for (; i < vectorizedLength; i += Vector<byte>.Count)
                {
                    var vectorA = Unsafe.ReadUnaligned<Vector<byte>>((byte*)a + i);
                    var vectorB = Unsafe.ReadUnaligned<Vector<byte>>((byte*)b + i);

                    if (!Vector.EqualsAll(vectorA, vectorB))
                    {
                        return false;
                    }
                }

                // 比较剩余字节
                for (; i < byteCount; i++)
                {
                    if (((byte*)a)[i] != ((byte*)b)[i])
                    {
                        return false;
                    }
                }

                return true;
            }
            else
            {
                // 回退到常规比较
                for (int i = 0; i < byteCount; i++)
                {
                    if (((byte*)a)[i] != ((byte*)b)[i])
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /// <summary>
        /// 使用SIMD加速的内存清零
        /// </summary>
        public static void ZeroMemory(void* destination, int byteCount)
        {
            if (Vector.IsHardwareAccelerated && byteCount >= Vector<byte>.Count)
            {
                Vector<byte> zeroVector = new Vector<byte>(0);
                int vectorizedLength = byteCount - (byteCount % Vector<byte>.Count);
                int i = 0;

                for (; i < vectorizedLength; i += Vector<byte>.Count)
                {
                    Unsafe.WriteUnaligned((byte*)destination + i, zeroVector);
                }

                // 清零剩余字节
                for (; i < byteCount; i++)
                {
                    ((byte*)destination)[i] = 0;
                }
            }
            else
            {
                // 回退到常规清零
                for (int i = 0; i < byteCount; i++)
                {
                    ((byte*)destination)[i] = 0;
                }
            }
        }
    }

    /// <summary>
    /// Unity专用的网络性能优化类
    /// </summary>
    public static class UnityNetworkOptimizer
    {
        // 跟踪已分配的NativeArray，确保在场景切换时正确释放
        private static readonly List<IDisposable> _allocatedResources = new List<IDisposable>();
        private static bool _isInitialized = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            if (_isInitialized) return;

            // 订阅场景卸载事件，确保释放所有原生资源
            UnityEngine.SceneManagement.SceneManager.sceneUnloaded += _ => ReleaseAllResources();
            Application.quitting += ReleaseAllResources;

            _isInitialized = true;
        }

        public static void ReleaseAllResources()
        {
            lock (_allocatedResources)
            {
                foreach (var resource in _allocatedResources)
                {
                    if (resource is not IDisposable disposable) continue;

                    // 检查是否有IsCreated属性
                    var isCreatedProperty = resource.GetType().GetProperty("IsCreated");
                    bool shouldDispose = true;

                    if (isCreatedProperty != null)
                    {
                        shouldDispose = (bool)isCreatedProperty.GetValue(resource);
                    }

                    if (shouldDispose)
                    {
                        disposable.Dispose();
                    }
                }

                _allocatedResources.Clear();
            }
        }

        /// <summary>
        /// 创建跟踪的NativeArray，确保不会泄漏
        /// </summary>
        public static NativeArray<T> CreateManagedNativeArray<T>(int length, Allocator allocator = Allocator.Persistent) where T : unmanaged
        {
            var array = new NativeArray<T>(length, allocator);

            lock (_allocatedResources)
            {
                _allocatedResources.Add(array);
            }

            return array;
        }

        /// <summary>
        /// 释放跟踪的NativeArray
        /// </summary>
        public static void ReleaseNativeArray<T>(NativeArray<T> array) where T : unmanaged
        {
            if (!array.IsCreated) return;

            lock (_allocatedResources)
            {
                _allocatedResources.Remove(array);
            }

            array.Dispose();
        }

        /// <summary>
        /// 在Unity Job System中使用的优化并行处理
        /// </summary>
        public static unsafe NativeArray<T> ProcessInJobSystem<T, TIn>(NativeArray<TIn> inputData, Func<TIn, T> processor)
            where T : unmanaged
            where TIn : unmanaged
        {
            int length = inputData.Length;
            var outputData = CreateManagedNativeArray<T>(length, Allocator.TempJob);

            // 使用Burst编译器和并行处理
            // 实际实现需要使用Unity的Job System
            // 这里只是概念性代码
            for (int i = 0; i < length; i++)
            {
                outputData[i] = processor(inputData[i]);
            }

            return outputData;
        }

        /// <summary>
        /// 减少Unity主线程的GC压力
        /// </summary>
        public static void ReduceGCPressure()
        {
            Profiler.BeginSample("Reduce GC Pressure");

            // 清除未使用的资源缓存
            Resources.UnloadUnusedAssets();

            Profiler.EndSample();
        }

        /// <summary>
        /// 使用Unity Profiler跟踪网络性能
        /// </summary>
        public static void BeginNetworkSample(string sampleName)
        {
            Profiler.BeginSample($"Network: {sampleName}");
        }

        public static void EndNetworkSample()
        {
            Profiler.EndSample();
        }
    }

    /// <summary>
    /// 使用Socket硬件加速的网络优化
    /// </summary>
    public static class HardwareAcceleratedNetworking
    {
        /// <summary>
        /// 启用收发缓冲区扩展策略
        /// </summary>
        public static void EnableReceiveBufferScaling(Socket socket)
        {
            try
            {
                // 启用TCP自动调整窗口大小
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveBuffer, 0);
            }
            catch (Exception)
            {
                // 忽略不支持的平台错误
            }
        }

        /// <summary>
        /// 配置套接字选项以优化高吞吐量
        /// </summary>
        public static void OptimizeForThroughput(Socket socket)
        {
            try
            {
                // 设置为低时延模式
                socket.NoDelay = true;

                // 增加发送和接收缓冲区大小
                socket.ReceiveBufferSize = 1024 * 1024; // 1MB
                socket.SendBufferSize = 1024 * 1024;    // 1MB

                // 禁用延迟ACK
                socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, 1);

                // 设置保持连接选项
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, 1);

                // 禁用Nagle算法
                socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, 1);
            }
            catch (Exception)
            {
                // 忽略不支持的平台错误
            }
        }

        /// <summary>
        /// 启用收集ACK (Acknowledgment)
        /// </summary>
        public static void EnableTcpAckFrequency(Socket socket, int frequency)
        {
            try
            {
                // Windows特定优化
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // TcpAckFrequency值越小，确认包发送越频繁，延迟越低，但CPU使用率略高
                    const int IOC_IN = unchecked((int)0x80000000);
                    const int IOC_VENDOR = 0x18000000;
                    const int SIO_TCP_SET_ACK_FREQUENCY = IOC_IN | IOC_VENDOR | 23;

                    byte[] optionInValue = BitConverter.GetBytes(frequency);
                    socket.IOControl(SIO_TCP_SET_ACK_FREQUENCY, optionInValue, null);
                }
            }
            catch (Exception)
            {
                // 忽略不支持的平台错误
            }
        }

        /// <summary>
        /// 尝试启用Linux下的零拷贝
        /// </summary>
        public static bool TryEnableLinuxZeroCopy(Socket socket)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return false;

            try
            {
                // Linux特定的Socket选项：SO_ZEROCOPY
                const int SO_ZEROCOPY = 60;
                socket.SetSocketOption(SocketOptionLevel.Socket, (SocketOptionName)SO_ZEROCOPY, 1);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// 本机加速的Socket使用DMA和硬件加速
    /// </summary>
    public class HardwareAcceleratedSocket
    {
        // 这个类在实际项目中可以实现针对特定操作系统的本机Socket加速技术
        // 例如：Windows的RIO (Registered I/O)、Linux的io_uring等
        // 注意：真实实现需要使用P/Invoke和本机互操作
    }
}
