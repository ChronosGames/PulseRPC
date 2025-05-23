using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace PulseRPC.Client.Platform.Unity
{
    /// <summary>
    /// Unity 平台适配器，提供 Unity 特定的功能实现
    /// </summary>
    public class UnityPlatformAdapter : IPlatformAdapter
    {
        private readonly ILoggerFactory _loggerFactory;

        public UnityPlatformAdapter(ILoggerFactory? loggerFactory = null)
        {
            _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        }

        public ILogger<T> CreateLogger<T>() => _loggerFactory.CreateLogger<T>();

        public Task Delay(int millisecondsDelay, CancellationToken cancellationToken = default)
        {
            // Unity 环境下使用 Task.Delay
            return Task.Delay(millisecondsDelay, cancellationToken);
        }

        public void ConfigureThreading()
        {
            // Unity 特定的线程配置
            // 在 Unity 中，大部分网络操作应该在后台线程执行
            ThreadPool.SetMinThreads(Environment.ProcessorCount, Environment.ProcessorCount);
        }

        public bool IsMainThread()
        {
#if UNITY_EDITOR || UNITY_STANDALONE || UNITY_ANDROID || UNITY_IOS
            return UnityEngine.Application.isPlaying &&
                   System.Threading.Thread.CurrentThread.ManagedThreadId == 1;
#else
            return false;
#endif
        }

        public void InvokeOnMainThread(Action action)
        {
#if UNITY_EDITOR || UNITY_STANDALONE || UNITY_ANDROID || UNITY_IOS
            if (IsMainThread())
            {
                action();
            }
            else
            {
                // 在 Unity 中，需要使用 Dispatcher 或者其他机制将操作调度到主线程
                // 这里可以使用 Unity 的 SynchronizationContext
                var context = SynchronizationContext.Current;
                if (context != null)
                {
                    context.Post(_ => action(), null);
                }
                else
                {
                    // 如果没有 SynchronizationContext，则直接执行
                    action();
                }
            }
#else
            action();
#endif
        }
    }
}
