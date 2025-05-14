using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using PulseRPC.Protocol.Messages;
using PulseRPC.Protocol.Network;

namespace PulseRPC.Server;
public class HandlerThreadPoolManager
{
    // 各种线程池
    private readonly TaskScheduler _workerThreadScheduler;
    private readonly TaskScheduler _highPriorityThreadScheduler;
    private readonly TaskScheduler _lowLatencyThreadScheduler;

    // 单一处理队列
    private readonly BlockingCollection<(Task task, bool isLongRunning)> _taskQueue =
        new BlockingCollection<(Task task, bool isLongRunning)>();

    // 任务处理标志
    private volatile bool _isProcessing;

    public HandlerThreadPoolManager(ThreadPoolConfiguration config)
    {
        // 创建自定义线程池
        _workerThreadScheduler = new LimitedConcurrencyTaskScheduler(
            config.WorkerThreads, "WorkerPool");

        _highPriorityThreadScheduler = new LimitedConcurrencyTaskScheduler(
            config.HighPriorityThreads, "HighPriorityPool");

        _lowLatencyThreadScheduler = new LimitedConcurrencyTaskScheduler(
            config.LowLatencyThreads, "LowLatencyPool");
    }

    // 简化的初始化方法，不再需要主线程ID
    public void Initialize()
    {
        // 无需任何特殊初始化
    }

    // 添加任务到队列
    public void QueueTask(Task task, bool isLongRunning = false)
    {
        _taskQueue.Add((task, isLongRunning));
    }

    // 提交任务并获取完成Task
    public Task SubmitToTaskProcessorAsync(Action action, bool isLongRunning = false)
    {
        var tcs = new TaskCompletionSource<bool>();

        var task = new Task(() => {
            try
            {
                action();
                tcs.TrySetResult(true);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        QueueTask(task, isLongRunning);

        return tcs.Task;
    }

    // 提交带返回值的任务
    public Task<T> SubmitToTaskProcessorAsync<T>(Func<T> function, bool isLongRunning = false)
    {
        var tcs = new TaskCompletionSource<T>();

        var task = new Task(() => {
            try
            {
                var result = function();
                tcs.TrySetResult(result);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        QueueTask(task, isLongRunning);

        return tcs.Task;
    }

    // 处理任务队列 - 可以从任何线程调用
    public void ProcessTasks(int maxTasksPerFrame)
    {
        // 确保一次只有一个线程处理任务
        if (_isProcessing)
            return;

        _isProcessing = true;

        try
        {
            int processedCount = 0;
            int longRunningCount = 0;

            while (processedCount < maxTasksPerFrame && _taskQueue.TryTake(out var taskItem))
            {
                var (task, isLongRunning) = taskItem;

                // 对于长时间运行的任务，限制每帧处理的数量
                if (isLongRunning)
                {
                    if (longRunningCount >= 1)
                    {
                        // 重新入队，下次处理
                        QueueTask(task, isLongRunning);
                        continue;
                    }
                    longRunningCount++;
                }

                try
                {
                    TryExecuteTask(task);
                }
                catch (Exception ex)
                {
                    // 记录异常
                    Console.WriteLine($"处理任务时出错: {ex}");
                }

                processedCount++;
            }
        }
        finally
        {
            _isProcessing = false;
        }
    }

    // 尝试执行任务的辅助方法
    private bool TryExecuteTask(Task task)
    {
        try
        {
            if (!task.IsCompleted)
            {
                if (task.Status == TaskStatus.Created)
                {
                    task.RunSynchronously();
                }
                else
                {
                    task.Wait();
                }
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    // 修改后的SubmitCommandTaskAsync方法，使用自定义任务提交逻辑
    public async Task SubmitCommandTaskAsync<TCommand>(
        HandlerThreadingPolicy policy,
        int priority,
        object handler,
        TCommand command,
        NetworkSession session,
        CancellationToken cancellationToken) where TCommand : Command
    {
        // 找到正确的Handle或HandleAsync方法
        var commandHandler = handler as ICommandHandler<TCommand>;
        if (commandHandler == null)
            throw new InvalidOperationException("处理器不实现ICommandHandler<TCommand>");

        // 基于策略决定执行位置
        switch (policy)
        {
            case HandlerThreadingPolicy.MainThread:
                await SubmitToTaskProcessorAsync(() =>
                    commandHandler.HandleAsync(session, command, cancellationToken).GetAwaiter().GetResult());
                break;

            case HandlerThreadingPolicy.LowLatencyThread:
                await Task.Factory.StartNew(
                    () => commandHandler.HandleAsync(session, command, cancellationToken),
                    cancellationToken,
                    TaskCreationOptions.None,
                    _lowLatencyThreadScheduler).Unwrap();
                break;

            case HandlerThreadingPolicy.HighPriorityThread:
                await Task.Factory.StartNew(
                    () => commandHandler.HandleAsync(session, command, cancellationToken),
                    cancellationToken,
                    TaskCreationOptions.None,
                    _highPriorityThreadScheduler).Unwrap();
                break;

            default: // WorkerThread
                await Task.Factory.StartNew(
                    () => commandHandler.HandleAsync(session, command, cancellationToken),
                    cancellationToken,
                    TaskCreationOptions.None,
                    _workerThreadScheduler).Unwrap();
                break;
        }
    }

    // 类似地修改SubmitRequestTaskAsync方法
    public async Task<Response> SubmitRequestTaskAsync<TRequest>(
        HandlerThreadingPolicy policy,
        int priority,
        object handler,
        TRequest request,
        Type responseType,
        NetworkSession session,
        CancellationToken cancellationToken) where TRequest : Request
    {
        // 使用反射获取正确的RequestHandler类型和方法
        Type handlerInterfaceType = typeof(IRequestHandler<,>).MakeGenericType(
            request.GetType(), responseType);

        if (!handlerInterfaceType.IsInstanceOfType(handler))
            throw new InvalidOperationException("处理器不实现正确的IRequestHandler接口");

        // 获取HandleAsync方法
        var methodInfo = handlerInterfaceType.GetMethod("HandleAsync");
        if (methodInfo == null)
            throw new InvalidOperationException("找不到HandleAsync方法");

        // 基于策略决定执行位置
        switch (policy)
        {
            case HandlerThreadingPolicy.MainThread:
                return await SubmitToTaskProcessorAsync(() => {
                    var responseTask = (Task)methodInfo.Invoke(
                        handler, [request!, session, cancellationToken])!;

                    responseTask.GetAwaiter().GetResult();

                    // 从Task<TResponse>中提取结果
                    var resultProperty = responseTask.GetType().GetProperty("Result");
                    return (Response)resultProperty!.GetValue(responseTask)!;
                });

            case HandlerThreadingPolicy.LowLatencyThread:
                return await ExecuteRequestOnScheduler(
                    _lowLatencyThreadScheduler, handler, methodInfo, request!, session, cancellationToken);

            case HandlerThreadingPolicy.HighPriorityThread:
                return await ExecuteRequestOnScheduler(
                    _highPriorityThreadScheduler, handler, methodInfo, request!, session, cancellationToken);

            default: // WorkerThread
                return await ExecuteRequestOnScheduler(
                    _workerThreadScheduler, handler, methodInfo, request!, session, cancellationToken);
        }
    }

    // 在指定调度器上执行请求处理的辅助方法
    private async Task<Response> ExecuteRequestOnScheduler(
        TaskScheduler scheduler,
        object handler,
        System.Reflection.MethodInfo methodInfo,
        object request,
        NetworkSession session,
        CancellationToken cancellationToken)
    {
        return await Task.Factory.StartNew<Response>(() => {
            var responseTask = (Task)methodInfo.Invoke(
                handler, [session, request, cancellationToken])!;

            responseTask.GetAwaiter().GetResult();

            // 从Task<TResponse>中提取结果
            var resultProperty = responseTask.GetType().GetProperty("Result");
            return (Response)resultProperty!.GetValue(responseTask)!;
        }, cancellationToken, TaskCreationOptions.None, scheduler);
    }
}

// 线程池配置类
public class ThreadPoolConfiguration
{
    public int WorkerThreads { get; set; } = Environment.ProcessorCount * 2;
    public int HighPriorityThreads { get; set; } = Math.Max(2, Environment.ProcessorCount / 2);
    public int LowLatencyThreads { get; set; } = Math.Max(2, Environment.ProcessorCount / 2);
}

// 有限并发任务调度器
public class LimitedConcurrencyTaskScheduler : TaskScheduler
{
    // 用于同步的对象
    private readonly object _lock = new object();

    // 任务队列
    private readonly LinkedList<Task> _taskQueue = new LinkedList<Task>();

    // 当前正在执行的任务数
    private int _runningTasks;

    // 最大并发任务数
    private readonly int _maxConcurrency;

    // 调度器名称（用于调试）
    private readonly string _name;

    // 构造函数
    public LimitedConcurrencyTaskScheduler(int maxConcurrency, string? name)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrency, 1);

        _maxConcurrency = maxConcurrency;
        _name = name ?? "LimitedConcurrencyTaskScheduler";
    }

    // 队列中待处理的任务数
    public int QueuedTaskCount
    {
        get { lock (_lock) { return _taskQueue.Count; } }
    }

    // 获取当前正在运行的任务数
    public int RunningTaskCount => _runningTasks;

    // 获取调度器名称
    public string Name => _name;

    // 将任务添加到队列
    protected override void QueueTask(Task task)
    {
        lock (_lock)
        {
            _taskQueue.AddLast(task);

            // 如果可以调度更多任务，则执行
            if (_runningTasks < _maxConcurrency)
            {
                _runningTasks++;
                ThreadPool.QueueUserWorkItem(ProcessTasks!, null);
            }
        }
    }

    // 处理任务队列
    private void ProcessTasks(object state)
    {
        while (true)
        {
            Task? task;

            lock (_lock)
            {
                // 如果队列为空，则退出
                if (_taskQueue.Count == 0)
                {
                    _runningTasks--;
                    break;
                }

                // 从队列中获取下一个任务
                task = _taskQueue.First!.Value;
                _taskQueue.RemoveFirst();
            }

            // 执行任务
            try
            {
                TryExecuteTask(task);
            }
            catch (Exception)
            {
                // 记录异常但继续处理
            }
        }
    }

    // 尝试内联执行任务
    protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
    {
        // 如果当前线程不是由此调度器管理的，则拒绝内联
        return !taskWasPreviouslyQueued && TryExecuteTask(task);
    }

    // 获取所有计划运行的任务
    protected override IEnumerable<Task> GetScheduledTasks()
    {
        lock (_lock)
        {
            return _taskQueue.ToArray();
        }
    }
}
