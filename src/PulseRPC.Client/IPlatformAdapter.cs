using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace PulseRPC.Client
{
    /// <summary>
    /// 平台适配器接口，定义跨平台功能的抽象
    /// </summary>
    public interface IPlatformAdapter
    {
        /// <summary>
        /// 创建日志记录器
        /// </summary>
        /// <typeparam name="T">日志记录器类型</typeparam>
        /// <returns>日志记录器实例</returns>
        ILogger<T> CreateLogger<T>();

        /// <summary>
        /// 异步延迟
        /// </summary>
        /// <param name="millisecondsDelay">延迟毫秒数</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>延迟任务</returns>
        Task Delay(int millisecondsDelay, CancellationToken cancellationToken = default);

        /// <summary>
        /// 配置线程池和线程相关设置
        /// </summary>
        void ConfigureThreading();

        /// <summary>
        /// 检查当前是否在主线程
        /// </summary>
        /// <returns>如果在主线程则返回 true</returns>
        bool IsMainThread();

        /// <summary>
        /// 在主线程上执行操作
        /// </summary>
        /// <param name="action">要执行的操作</param>
        void InvokeOnMainThread(Action action);
    }
}
