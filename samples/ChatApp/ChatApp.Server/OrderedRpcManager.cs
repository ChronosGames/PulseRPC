// using System;
// using System.Collections.Concurrent;
// using System.Collections.Generic;
// using System.Linq;
// using System.Text.Json;
// using System.Threading;
// using System.Threading.Tasks;
// using Microsoft.Extensions.Logging;
//
// namespace ChatApp.Server;
//
// public interface IOrderedRpcManager
// {
//     Task<T> SendOrderedRequest<T>(string targetPlayerId, string endpoint, object request, long sequence = -1);
// }
//
// // 具有顺序保证的RPC调用管理器
// public class OrderedRpcManager : IOrderedRpcManager
// {
//     private readonly IRpcService _rpcService;
//     private readonly IPlayerRoutingService _routingService;
//     private readonly ILogger _logger;
//
//     // 每个玩家的消息序列号
//     private readonly ConcurrentDictionary<string, AtomicLong> _playerSequences =
//         new ConcurrentDictionary<string, AtomicLong>();
//
//     // 记录已处理的消息
//     private readonly ConcurrentDictionary<string, ConcurrentHashSet<long>> _processedMessages =
//         new ConcurrentDictionary<string, ConcurrentHashSet<long>>();
//
//     // 等待处理的消息缓冲区
//     private readonly ConcurrentDictionary<string, SortedDictionary<long, PendingMessage>> _pendingMessages =
//         new ConcurrentDictionary<string, SortedDictionary<long, PendingMessage>>();
//
//     // 当前正在处理的序列号
//     private readonly ConcurrentDictionary<string, AtomicLong> _currentSequences =
//         new ConcurrentDictionary<string, AtomicLong>();
//
//     // 玩家队列锁
//     private readonly ConcurrentDictionary<string, SemaphoreSlim> _playerQueueLocks =
//         new ConcurrentDictionary<string, SemaphoreSlim>();
//
//     public OrderedRpcManager(
//         IRpcService rpcService,
//         IPlayerRoutingService routingService,
//         ILogger logger)
//     {
//         _rpcService = rpcService;
//         _routingService = routingService;
//         _logger = logger;
//
//         RegisterRpcEndpoints();
//     }
//
//     // 注册RPC端点
//     void RegisterRpcEndpoints()
//     {
//         _rpcService.RegisterEndpoint<OrderedRpcRequest, object>("ProcessOrderedRequest", ProcessOrderedRequest);
//     }
//
//     // 获取下一个序列号
//     public long GetNextSequence(string playerId)
//     {
//         var sequence = _playerSequences.GetOrAdd(playerId, _ => new AtomicLong(0));
//         return sequence.Increment();
//     }
//
//     // 发送有序RPC请求
//     public async Task<T> SendOrderedRequest<T>(
//         string targetPlayerId, string endpoint, object request, long sequence = -1)
//     {
//         try
//         {
//             // 定位玩家服务器
//             string serverId = await _routingService.LocatePlayer(targetPlayerId);
//
//             // 自动生成序列号（如果未提供）
//             if (sequence < 0)
//             {
//                 sequence = GetNextSequence(targetPlayerId);
//             }
//
//             // 包装请求
//             var orderedRequest = new OrderedRpcRequest
//             {
//                 PlayerId = targetPlayerId,
//                 Sequence = sequence,
//                 Endpoint = endpoint,
//                 Payload = JsonSerializer.Serialize(request)
//             };
//
//             // 发送RPC请求
//             return await _rpcService.CallRemoteAsync<T>(
//                 serverId, "ProcessOrderedRequest", orderedRequest);
//         }
//         catch (PlayerNotFoundException ex)
//         {
//             _logger.Warning($"Player {targetPlayerId} not found: {ex.Message}");
//             throw;
//         }
//         catch (Exception ex)
//         {
//             _logger.Error($"Error sending ordered request to {targetPlayerId}: {ex.Message}");
//             throw;
//         }
//     }
//
//     // 处理有序RPC请求
//     public async Task<object> ProcessOrderedRequest(OrderedRpcRequest request)
//     {
//         string playerId = request.PlayerId;
//         long sequence = request.Sequence;
//
//         // 获取或创建玩家队列锁
//         var queueLock = _playerQueueLocks.GetOrAdd(playerId, _ => new SemaphoreSlim(1, 1));
//
//         // 获取当前处理序列号
//         var currentSequence = _currentSequences.GetOrAdd(playerId, _ => new AtomicLong(0));
//
//         // 检查消息是否重复
//         var processedSet = _processedMessages.GetOrAdd(playerId, _ => new ConcurrentHashSet<long>());
//         if (processedSet.Contains(sequence))
//         {
//             _logger.Warning($"Duplicate message detected for player {playerId}, sequence {sequence}");
//             return null; // 或者返回幂等操作的结果
//         }
//
//         // 获取或创建待处理消息队列
//         var pendingQueue = _pendingMessages.GetOrAdd(
//             playerId, _ => new SortedDictionary<long, PendingMessage>());
//
//         // 创建待处理消息
//         var pendingMessage = new PendingMessage
//         {
//             Sequence = sequence,
//             Endpoint = request.Endpoint,
//             Payload = request.Payload,
//             CompletionSource = new TaskCompletionSource<object>()
//         };
//
//         await queueLock.WaitAsync();
//         try
//         {
//             // 添加消息到待处理队列
//             lock (pendingQueue)
//             {
//                 pendingQueue[sequence] = pendingMessage;
//             }
//
//             // 处理所有就绪的消息
//             await ProcessReadyMessages(playerId, pendingQueue, currentSequence, processedSet);
//         }
//         finally
//         {
//             queueLock.Release();
//         }
//
//         // 等待当前消息处理完成
//         return await pendingMessage.CompletionSource.Task;
//     }
//
//     // 处理就绪的消息
//     async Task ProcessReadyMessages(
//         string playerId,
//         SortedDictionary<long, PendingMessage> pendingQueue,
//         AtomicLong currentSequence,
//         ConcurrentHashSet<long> processedSet)
//     {
//         bool hasMoreMessages = true;
//
//         while (hasMoreMessages)
//         {
//             PendingMessage nextMessage = null;
//             long expectedSequence = currentSequence.Get() + 1;
//
//             // 查找下一个要处理的消息
//             lock (pendingQueue)
//             {
//                 if (pendingQueue.Count > 0 && pendingQueue.ContainsKey(expectedSequence))
//                 {
//                     nextMessage = pendingQueue[expectedSequence];
//                     pendingQueue.Remove(expectedSequence);
//                 }
//                 else
//                 {
//                     hasMoreMessages = false;
//                 }
//             }
//
//             if (nextMessage != null)
//             {
//                 try
//                 {
//                     // 处理消息
//                     var result = await ProcessMessage(nextMessage);
//
//                     // 更新当前序列号
//                     currentSequence.Set(expectedSequence);
//
//                     // 标记为已处理
//                     processedSet.Add(expectedSequence);
//
//                     // 清理过旧的已处理记录
//                     if (processedSet.Count > 1000)
//                     {
//                         CleanupProcessedSet(processedSet, currentSequence.Get() - 500);
//                     }
//
//                     // 完成任务
//                     nextMessage.CompletionSource.SetResult(result);
//                 }
//                 catch (Exception ex)
//                 {
//                     _logger.Error($"Error processing message {nextMessage.Sequence}: {ex.Message}");
//                     nextMessage.CompletionSource.SetException(ex);
//
//                     // 发生错误时停止处理
//                     hasMoreMessages = false;
//                 }
//             }
//         }
//     }
//
//     // 处理单个消息
//     async Task<object> ProcessMessage(PendingMessage message)
//     {
//         // 反序列化并处理请求
//         // 实际实现应该调用本地注册的处理程序
//         _logger.Info($"Processing message: {message.Endpoint}, sequence: {message.Sequence}");
//
//         // 模拟处理
//         await Task.Delay(10);
//
//         // 返回结果
//         return new { Success = true, Message = "Processed" };
//     }
//
//     // 清理已处理集合
//     static void CleanupProcessedSet(ConcurrentHashSet<long> processedSet, long olderThan)
//     {
//         var oldMessages = processedSet.Items.Where(seq => seq < olderThan).ToList();
//         foreach (var seq in oldMessages)
//         {
//             processedSet.TryRemove(seq);
//         }
//     }
//
//     // 带有原子操作的长整型
//     class AtomicLong(long initialValue)
//     {
//         long _value = initialValue;
//
//         public long Get()
//         {
//             return Interlocked.Read(ref _value);
//         }
//
//         public void Set(long newValue)
//         {
//             Interlocked.Exchange(ref _value, newValue);
//         }
//
//         public long Increment()
//         {
//             return Interlocked.Increment(ref _value);
//         }
//     }
//
//     // 待处理消息
//     class PendingMessage
//     {
//         public long Sequence { get; set; }
//         public string Endpoint { get; set; }
//         public string Payload { get; set; }
//         public TaskCompletionSource<object> CompletionSource { get; set; }
//     }
// }
//
// // 有序RPC请求
// public class OrderedRpcRequest
// {
//     public string PlayerId { get; set; }
//     public long Sequence { get; set; }
//     public string Endpoint { get; set; }
//     public string Payload { get; set; }
// }
//
// // 并发哈希集合
// public class ConcurrentHashSet<T>
// {
//     private readonly ConcurrentDictionary<T, byte> _dictionary = new ConcurrentDictionary<T, byte>();
//
//     public bool Add(T item)
//     {
//         return _dictionary.TryAdd(item, 0);
//     }
//
//     public bool Contains(T item)
//     {
//         return _dictionary.ContainsKey(item);
//     }
//
//     public bool TryRemove(T item)
//     {
//         return _dictionary.TryRemove(item, out _);
//     }
//
//     public int Count => _dictionary.Count;
//
//     public IEnumerable<T> Items => _dictionary.Keys;
// }
