// using System.Collections.Concurrent;
// using System.Windows.Input;
// using MemoryPack;
// using PulseRPC.Network;
//
// namespace PulseRPC.Server;
//
// public class HandlerThreadPoolManager(ThreadPoolConfiguration config)
// {
//     // 各种线程池
//     private readonly TaskScheduler _workerThreadScheduler = new LimitedConcurrencyTaskScheduler(
//         config.WorkerThreads, "WorkerPool");
//     private readonly TaskScheduler _highPriorityThreadScheduler = new LimitedConcurrencyTaskScheduler(
//         config.HighPriorityThreads, "HighPriorityPool");
//     private readonly TaskScheduler _lowLatencyThreadScheduler = new LimitedConcurrencyTaskScheduler(
//         config.LowLatencyThreads, "LowLatencyPool");
//
//     // 单一处理队列
//     private readonly BlockingCollection<(Task task, bool isLongRunning)> _taskQueue =
//         new BlockingCollection<(Task task, bool isLongRunning)>();
//
//     // 任务处理标志
//     private volatile bool _isProcessing;
//
//     // 创建自定义线程池
//
//     // 简化的初始化方法，不再需要主线程ID
//     public void Initialize()
//     {
//         // 无需任何特殊初始化
//     }
//
//     // 添加任务到队列
//     public void QueueTask(Task task, bool isLongRunning = false)
//     {
//         _taskQueue.Add((task, isLongRunning));
//     }
//
//     // 提交任务并获取完成Task
//     public Task SubmitToTaskProcessorAsync(Action action, bool isLongRunning = false)
//     {
//         var tcs = new TaskCompletionSource<bool>();
//
//         var task = new Task(() => {
//             try
//             {
//                 action();
//                 tcs.TrySetResult(true);
//             }
//             catch (Exception ex)
//             {
//                 tcs.TrySetException(ex);
//             }
//         });
//
//         QueueTask(task, isLongRunning);
//
//         return tcs.Task;
//     }
//
//     // 提交带返回值的任务
//     public Task<T> SubmitToTaskProcessorAsync<T>(Func<T> function, bool isLongRunning = false)
//     {
//         var tcs = new TaskCompletionSource<T>();
//
//         var task = new Task(() => {
//             try
//             {
//                 var result = function();
//                 tcs.TrySetResult(result);
//             }
//             catch (Exception ex)
//             {
//                 tcs.TrySetException(ex);
//             }
//         });
//
//         QueueTask(task, isLongRunning);
//
//         return tcs.Task;
//     }
//
//     // 处理任务队列 - 可以从任何线程调用
//     public void ProcessTasks(int maxTasksPerFrame)
//     {
//         // 确保一次只有一个线程处理任务
//         if (_isProcessing)
//             return;
//
//         _isProcessing = true;
//
//         try
//         {
//             int processedCount = 0;
//             int longRunningCount = 0;
//
//             while (processedCount < maxTasksPerFrame && _taskQueue.TryTake(out var taskItem))
//             {
//                 var (task, isLongRunning) = taskItem;
//
//                 // 对于长时间运行的任务，限制每帧处理的数量
//                 if (isLongRunning)
//                 {
//                     if (longRunningCount >= 1)
//                     {
//                         // 重新入队，下次处理
//                         QueueTask(task, isLongRunning);
//                         continue;
//                     }
//                     longRunningCount++;
//                 }
//
//                 try
//                 {
//                     TryExecuteTask(task);
//                 }
//                 catch (Exception ex)
//                 {
//                     // 记录异常
//                     Console.WriteLine($"处理任务时出错: {ex}");
//                 }
//
//                 processedCount++;
//             }
//         }
//         finally
//         {
//             _isProcessing = false;
//         }
//     }
//
//     // 尝试执行任务的辅助方法
//     private bool TryExecuteTask(Task task)
//     {
//         try
//         {
//             if (!task.IsCompleted)
//             {
//                 if (task.Status == TaskStatus.Created)
//                 {
//                     task.RunSynchronously();
//                 }
//                 else
//                 {
//                     task.Wait();
//                 }
//             }
//             return true;
//         }
//         catch
//         {
//             return false;
//         }
//     }
//
//     // 提交命令任务的泛型方法
//     public async Task SubmitCommandTaskAsync<TCommand>(
//         HandlerThreadingPolicy policy,
//         int priority,
//         object handler,
//         TCommand command,
//         NetworkSession session,
//         CancellationToken cancellationToken) where TCommand : ICommand
//     {
//         await ExecuteOnThreadPool(policy, () => {
//             // 找到正确的Handle或HandleAsync方法
//             var commandHandler = handler as ICommandHandler<TCommand>;
//             if (commandHandler == null)
//                 throw new InvalidOperationException("处理器不实现ICommandHandler<TCommand>");
//
//             return commandHandler.HandleAsync(session, command, cancellationToken);
//         });
//     }
//
//     // 提交带上下文的命令任务
//     public async Task SubmitContextualCommandTaskAsync<TCommand, TContext>(
//         HandlerThreadingPolicy policy,
//         int priority,
//         object handler,
//         TCommand command,
//         TContext context,
//         NetworkSession session,
//         CancellationToken cancellationToken) where TCommand : ICommand
//     {
//         await ExecuteOnThreadPool(policy, () => {
//             // 找到正确的Handle或HandleAsync方法
//             var commandHandler = handler as IContextualCommandHandler<TCommand, TContext>;
//             if (commandHandler == null)
//                 throw new InvalidOperationException("处理器不实现IContextualCommandHandler<TCommand, TContext>");
//
//             return commandHandler.HandleAsync(session, command, context, cancellationToken);
//         });
//     }
//
//     // 提交请求任务的泛型方法
//     public async Task<TResponse> SubmitRequestTaskAsync<TRequest, TResponse>(
//         HandlerThreadingPolicy policy,
//         int priority,
//         object handler,
//         TRequest request,
//         NetworkSession session,
//         CancellationToken cancellationToken) where TRequest : IMemoryPackable<TRequest>
//     {
//         return await ExecuteOnThreadPool(policy, () => {
//             // 找到正确的Handle或HandleAsync方法
//             var requestHandler = handler as IRequestHandler<TRequest, TResponse>;
//             if (requestHandler == null)
//                 throw new InvalidOperationException("处理器不实现IRequestHandler<TRequest, TResponse>");
//
//             return requestHandler.HandleAsync(session, request, cancellationToken);
//         });
//     }
//
//     // 提交带上下文的请求任务
//     public async Task<TResponse> SubmitContextualRequestTaskAsync<TRequest, TResponse, TContext>(
//         HandlerThreadingPolicy policy,
//         int priority,
//         object handler,
//         TRequest request,
//         TContext context,
//         NetworkSession session,
//         CancellationToken cancellationToken) where TRequest : IMemoryPackable<TRequest>
//     {
//         return await ExecuteOnThreadPool(policy, () => {
//             // 找到正确的Handle或HandleAsync方法
//             var requestHandler = handler as IContextualRequestHandler<TRequest, TResponse, TContext>;
//             if (requestHandler == null)
//                 throw new InvalidOperationException("处理器不实现IContextualRequestHandler<TRequest, TResponse, TContext>");
//
//             return requestHandler.HandleAsync(session, request, context, cancellationToken);
//         });
//     }
//
//     // 提交扩展请求任务
//     public async Task<(TResponse Response, TResult Result)> SubmitExtendedRequestTaskAsync<TRequest, TResponse, TOptions, TResult>(
//         HandlerThreadingPolicy policy,
//         int priority,
//         object handler,
//         TRequest request,
//         TOptions options,
//         NetworkSession session,
//         CancellationToken cancellationToken) where TRequest : IMemoryPackable<TRequest>
//     {
//         return await ExecuteOnThreadPool(policy, () => {
//             // 找到正确的Handle或HandleAsync方法
//             var requestHandler = handler as IExtendedRequestHandler<TRequest, TResponse, TOptions, TResult>;
//             if (requestHandler == null)
//                 throw new InvalidOperationException("处理器不实现IExtendedRequestHandler<TRequest, TResponse, TOptions, TResult>");
//
//             return requestHandler.HandleAsync(session, request, options, cancellationToken);
//         });
//     }
//
//     // 使用指定的线程策略执行任务
//     private async Task ExecuteOnThreadPool(HandlerThreadingPolicy policy, Func<Task> action)
//     {
//         switch (policy)
//         {
//             case HandlerThreadingPolicy.MainThread:
//                 await SubmitToTaskProcessorAsync(() => action().GetAwaiter().GetResult());
//                 break;
//             case HandlerThreadingPolicy.LowLatencyThread:
//                 await Task.Factory.StartNew(
//                     action,
//                     CancellationToken.None,
//                     TaskCreationOptions.None,
//                     _lowLatencyThreadScheduler).Unwrap();
//                 break;
//             case HandlerThreadingPolicy.HighPriorityThread:
//                 await Task.Factory.StartNew(
//                     action,
//                     CancellationToken.None,
//                     TaskCreationOptions.None,
//                     _highPriorityThreadScheduler).Unwrap();
//                 break;
//             default: // WorkerThread
//                 await Task.Factory.StartNew(
//                     action,
//                     CancellationToken.None,
//                     TaskCreationOptions.None,
//                     _workerThreadScheduler).Unwrap();
//                 break;
//         }
//     }
//
//     // 使用指定的线程策略执行带返回值的任务
//     private async Task<T> ExecuteOnThreadPool<T>(HandlerThreadingPolicy policy, Func<Task<T>> action)
//     {
//         switch (policy)
//         {
//             case HandlerThreadingPolicy.MainThread:
//                 return await SubmitToTaskProcessorAsync(() => action().GetAwaiter().GetResult());
//             case HandlerThreadingPolicy.LowLatencyThread:
//                 return await Task.Factory.StartNew(
//                     action,
//                     CancellationToken.None,
//                     TaskCreationOptions.None,
//                     _lowLatencyThreadScheduler).Unwrap();
//             case HandlerThreadingPolicy.HighPriorityThread:
//                 return await Task.Factory.StartNew(
//                     action,
//                     CancellationToken.None,
//                     TaskCreationOptions.None,
//                     _highPriorityThreadScheduler).Unwrap();
//             default: // WorkerThread
//                 return await Task.Factory.StartNew(
//                     action,
//                     CancellationToken.None,
//                     TaskCreationOptions.None,
//                     _workerThreadScheduler).Unwrap();
//         }
//     }
//
//     // 旧方法 - 保持兼容性
//     public async Task<object> SubmitRequestTaskAsync<TRequest>(
//         HandlerThreadingPolicy policy,
//         int priority,
//         object handler,
//         TRequest request,
//         Type responseType,
//         NetworkSession session,
//         CancellationToken cancellationToken) where TRequest : notnull
//     {
//         // 使用反射获取正确的RequestHandler类型和方法
//         var handlerInterfaceType = typeof(IRequestHandler<,>).MakeGenericType(request.GetType(), responseType);
//
//         if (!handlerInterfaceType.IsInstanceOfType(handler))
//         {
//             throw new InvalidOperationException("处理器不实现正确的IRequestHandler接口");
//         }
//
//         // 获取HandleAsync方法
//         var methodInfo = handlerInterfaceType.GetMethod("HandleAsync");
//         if (methodInfo == null)
//         {
//             throw new InvalidOperationException("找不到HandleAsync方法");
//         }
//
//         // 基于策略决定执行位置
//         return policy switch
//         {
//             HandlerThreadingPolicy.MainThread => await SubmitToTaskProcessorAsync(() =>
//             {
//                 var responseTask = (Task)methodInfo.Invoke(handler, [request, session, cancellationToken])!;
//
//                 responseTask.GetAwaiter().GetResult();
//
//                 // 从Task<TResponse>中提取结果
//                 var resultProperty = responseTask.GetType().GetProperty("Result");
//                 return resultProperty!.GetValue(responseTask)!;
//             }),
//             HandlerThreadingPolicy.LowLatencyThread => await ExecuteRequestOnScheduler(_lowLatencyThreadScheduler, handler, methodInfo, request, session, cancellationToken),
//             HandlerThreadingPolicy.HighPriorityThread => await ExecuteRequestOnScheduler(_highPriorityThreadScheduler, handler, methodInfo, request, session, cancellationToken),
//             _ => await ExecuteRequestOnScheduler(_workerThreadScheduler, handler, methodInfo, request, session, cancellationToken).ConfigureAwait(false)
//         };
//     }
//
//     // 在指定调度器上执行请求处理的辅助方法
//     private static Task<object> ExecuteRequestOnScheduler(
//         TaskScheduler scheduler,
//         object handler,
//         System.Reflection.MethodInfo methodInfo,
//         object request,
//         NetworkSession session,
//         CancellationToken cancellationToken)
//     {
//         return Task.Factory.StartNew(() =>
//         {
//             var responseTask = (Task)methodInfo.Invoke(handler, [session, request, cancellationToken])!;
//             responseTask.GetAwaiter().GetResult();
//
//             // 从Task<TResponse>中提取结果
//             var resultProperty = responseTask.GetType().GetProperty("Result");
//             return resultProperty!.GetValue(responseTask)!;
//         }, cancellationToken, TaskCreationOptions.None, scheduler);
//     }
// }
//
// public class ThreadPoolConfiguration
// {
//     public int WorkerThreads { get; set; } = Environment.ProcessorCount * 2;
//     public int HighPriorityThreads { get; set; } = Math.Max(2, Environment.ProcessorCount / 2);
//     public int LowLatencyThreads { get; set; } = Math.Max(2, Environment.ProcessorCount / 2);
// }
//
// public class LimitedConcurrencyTaskScheduler : TaskScheduler
// {
//     // 对象锁
//     private readonly object _lock = new();
//
//     // 任务队列
//     private readonly LinkedList<Task> _taskQueue = [];
//
//     // 当前运行任务数
//     private int _runningTasks;
//
//     // 最大并发数
//     private readonly int _maxConcurrency;
//
//     // 调度器名称
//     private readonly string _name;
//
//     // 构造函数
//     public LimitedConcurrencyTaskScheduler(int maxConcurrency, string? name)
//     {
//         _maxConcurrency = maxConcurrency;
//         _name = name ?? $"LimitedConcurrencyPool({maxConcurrency})";
//     }
//
//     // 队列任务数
//     public int QueuedTaskCount
//     {
//         get { lock (_lock) { return _taskQueue.Count; } }
//     }
//
//     // 正在运行的任务数
//     public int RunningTaskCount => _runningTasks;
//
//     // 调度器名称
//     public string Name => _name;
//
//     // 将任务添加到队列
//     protected override void QueueTask(Task task)
//     {
//         lock (_lock)
//         {
//             _taskQueue.AddLast(task);
//
//             // 如果有可用线程，启动一个任务处理过程
//             if (_runningTasks < _maxConcurrency)
//             {
//                 _runningTasks++;
//                 ThreadPool.QueueUserWorkItem(ProcessTasks, null);
//             }
//         }
//     }
//
//     // 任务处理过程
//     private void ProcessTasks(object? state)
//     {
//         while (true)
//         {
//             Task? task = null;
//
//             lock (_lock)
//             {
//                 // 没有任务了，退出循环
//                 if (_taskQueue.Count == 0)
//                 {
//                     _runningTasks--;
//                     break;
//                 }
//
//                 // 获取下一个任务
//                 task = _taskQueue.First!.Value;
//                 _taskQueue.RemoveFirst();
//             }
//
//             // 执行任务
//             TryExecuteTask(task);
//         }
//     }
//
//     // 尝试内联执行任务
//     protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
//     {
//         return false; // 不执行内联任务以确保线程隔离
//     }
//
//     // 获取所有调度任务的枚举
//     protected override IEnumerable<Task> GetScheduledTasks()
//     {
//         lock (_lock)
//         {
//             return new List<Task>(_taskQueue);
//         }
//     }
// }
