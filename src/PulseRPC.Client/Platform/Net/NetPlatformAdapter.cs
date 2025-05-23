using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PulseRPC.Client.Platform.Net
{
    /// <summary>
    /// .NET 平台适配器，提供 .NET 特定的功能实现
    /// </summary>
    public class NetPlatformAdapter : IPlatformAdapter
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly int _mainThreadId;

        public NetPlatformAdapter(ILoggerFactory? loggerFactory = null)
        {
            _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        public ILogger<T> CreateLogger<T>() => _loggerFactory.CreateLogger<T>();

        public Task Delay(int millisecondsDelay, CancellationToken cancellationToken = default)
        {
            return Task.Delay(millisecondsDelay, cancellationToken);
        }

        public void ConfigureThreading()
        {
            // .NET 环境下的线程配置
            ThreadPool.SetMinThreads(Environment.ProcessorCount * 2, Environment.ProcessorCount * 2);
        }

        public bool IsMainThread()
        {
            return Thread.CurrentThread.ManagedThreadId == _mainThreadId;
        }

        public void InvokeOnMainThread(Action action)
        {
            if (IsMainThread())
            {
                action();
            }
            else
            {
                // 在 .NET 环境中，可以使用 TaskScheduler 或 SynchronizationContext
                var context = SynchronizationContext.Current;
                if (context != null)
                {
                    context.Post(_ => action(), null);
                }
                else
                {
                    // 如果没有同步上下文，则在 ThreadPool 中执行
                    Task.Run(action);
                }
            }
        }
    }
}
