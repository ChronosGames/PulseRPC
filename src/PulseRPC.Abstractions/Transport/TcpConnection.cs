using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using MemoryPack;

namespace PulseRPC.Transport
{
    // 消息头结构
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public readonly struct MessageHeader
    {
        public readonly int Length;
        public readonly ushort MessageId;
        public readonly ushort Flags;  // 扩展为2字节以更好对齐

        public const int Size = 8;

        // 标志位定义
        public const ushort FlagNone = 0x0000;
        public const ushort FlagLargePacket = 0x0001;
        public const ushort FlagCompressed = 0x0002;
        public const ushort FlagChunked = 0x0004;
        public const ushort FlagEndOfChunk = 0x0008;

        public MessageHeader(int length, ushort messageId, ushort flags = FlagNone)
        {
            Length = length;
            MessageId = messageId;
            Flags = flags;
        }

        public bool IsLargePacket => (Flags & FlagLargePacket) != 0;
        public bool IsChunked => (Flags & FlagChunked) != 0;
        public bool IsEndOfChunk => (Flags & FlagEndOfChunk) != 0;
    }

    // 块传输头
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public readonly struct ChunkHeader
    {
        public readonly int ChunkId;        // 块ID
        public readonly int ChunkIndex;     // 块索引
        public readonly int TotalChunks;    // 总块数
        public readonly int ChunkSize;      // 本块大小

        public const int Size = 16;

        public ChunkHeader(int chunkId, int chunkIndex, int totalChunks, int chunkSize)
        {
            ChunkId = chunkId;
            ChunkIndex = chunkIndex;
            TotalChunks = totalChunks;
            ChunkSize = chunkSize;
        }
    }

    // 发送请求
    public sealed class SendRequest
    {
        public ArraySegment<byte> Data { get; set; }
        public bool NeedReturn { get; set; }
        public TaskCompletionSource<bool> Completion { get; set; }

        public SendRequest(ArraySegment<byte> data, bool needReturn, TaskCompletionSource<bool> completion)
        {
            Data = data;
            NeedReturn = needReturn;
            Completion = completion;
        }
    }

    // 大包接收状态（线程安全）
    public sealed class LargePacketState
    {
        private readonly object _lock = new object();
        private readonly Dictionary<int, byte[]> _chunks;
        private int _receivedChunks;

        public int ChunkId { get; }
        public int TotalChunks { get; }
        public ushort MessageId { get; }
        public DateTime StartTime { get; }

        public LargePacketState(int chunkId, int totalChunks, ushort messageId)
        {
            ChunkId = chunkId;
            TotalChunks = totalChunks;
            MessageId = messageId;
            StartTime = DateTime.UtcNow;
            _chunks = new Dictionary<int, byte[]>(totalChunks);
            _receivedChunks = 0;
        }

        public bool AddChunk(int index, byte[] data)
        {
            lock (_lock)
            {
                if (_chunks.ContainsKey(index))
                    return false;

                _chunks[index] = data;
                _receivedChunks++;
                return _receivedChunks == TotalChunks;
            }
        }

        public byte[]? GetCompleteData()
        {
            lock (_lock)
            {
                if (_receivedChunks != TotalChunks)
                    return null;

                var totalSize = 0;
                for (int i = 0; i < TotalChunks; i++)
                {
                    if (_chunks.TryGetValue(i, out var chunk))
                        totalSize += chunk.Length;
                }

                var result = new byte[totalSize];
                var offset = 0;

                for (int i = 0; i < TotalChunks; i++)
                {
                    if (_chunks.TryGetValue(i, out var chunk))
                    {
                        Buffer.BlockCopy(chunk, 0, result, offset, chunk.Length);
                        offset += chunk.Length;
                    }
                }

                return result;
            }
        }

        public float Progress
        {
            get
            {
                lock (_lock)
                {
                    return (float)_receivedChunks / TotalChunks;
                }
            }
        }
    }

    // 线程安全的高性能TCP连接
    public sealed class TcpConnection : IDisposable
    {
        private readonly TransportOptions _options;
        private readonly Socket _socket;
        private readonly BlockingCollection<SendRequest> _sendQueue;
        private readonly CancellationTokenSource _cts;

        // 配置
        private readonly int _smallPacketThreshold;
        private readonly int _chunkSize;

        // 接收缓冲区
        private readonly byte[] _receiveBuffer;
        private int _receivePosition;
        private readonly object _receiveLock = new object();

        // 大包状态管理（线程安全）
        private readonly ConcurrentDictionary<int, LargePacketState> _largePacketStates;
        private int _nextChunkId;

        // 消息处理
        private readonly ConcurrentDictionary<ushort, MessageHandlerInfo> _handlers;

        private ConnectionState _state = ConnectionState.Disconnected;

        // 统计
        private long _totalBytesSent;
        private long _totalBytesReceived;

        public string Name => "TCP";
        public long TotalBytesSent => Interlocked.Read(ref _totalBytesSent);
        public long TotalBytesReceived => Interlocked.Read(ref _totalBytesReceived);

        public TransportType Type => TransportType.Tcp;

        public bool IsConnected => _state == ConnectionState.Connected && _socket?.Connected == true;
        public ConnectionState State => _state;

        public EndPoint LocalEndPoint => _socket.LocalEndPoint!;
        public EndPoint RemoteEndPoint => _socket.RemoteEndPoint!;

        public event System.EventHandler<TransportStateEventArgs>? StateChanged;
        public event System.EventHandler<TransportDataEventArgs>? DataReceived;

        private class MessageHandlerInfo
        {
            public MessageHandlerInfo(Type messageType, Func<byte[], Task> handler, bool isSync = false)
            {
                MessageType = messageType;
                Handler = handler;
                IsSync = isSync;
            }

            public Type MessageType { get; set; }
            public Func<byte[], Task> Handler { get; set; }
            public bool IsSync { get; set; }
        }

        public TcpConnection(
            Socket socket,
            TransportOptions? options = null,
            int receiveBufferSize = 1024 * 1024, // 1MB
            int smallPacketThreshold = 64 * 1024, // 64KB
            int chunkSize = 32 * 1024) // 32KB chunks
        {
            _options = options ?? new TransportOptions();
            _socket = socket ?? throw new ArgumentNullException(nameof(socket));
            _sendQueue = new BlockingCollection<SendRequest>(1000);
            _cts = new CancellationTokenSource();

            _smallPacketThreshold = smallPacketThreshold;
            _chunkSize = chunkSize;

            _receiveBuffer = new byte[receiveBufferSize];
            _receivePosition = 0;

            _largePacketStates = new ConcurrentDictionary<int, LargePacketState>();
            _nextChunkId = 0;

            _handlers = new ConcurrentDictionary<ushort, MessageHandlerInfo>();
        }

        #region 消息处理器注册

        public void RegisterHandler<T>(ushort messageId, Func<T, Task> handler)
        {
            _handlers[messageId] = new MessageHandlerInfo(
                typeof(T),
                handler: async (data) =>
                {
                    var message = MemoryPackSerializer.Deserialize<T>(data);
                    await handler(message!).ConfigureAwait(false);
                },
                isSync: false);
        }

        public void RegisterHandler<T>(ushort messageId, Action<T> handler)
        {
            _handlers[messageId] = new MessageHandlerInfo
            (
                messageType: typeof(T),
                handler: (data) =>
                {
                    var message = MemoryPackSerializer.Deserialize<T>(data);
                    handler(message!);
                    return Task.CompletedTask;
                },
                isSync: true
            );
        }

        #endregion

        #region 发送方法

        public async Task SendAsync<T>(ushort messageId, T message)
        {
            // 序列化消息
            var data = MemoryPackSerializer.Serialize(message);

            if (data.Length <= _smallPacketThreshold)
            {
                // 小包直接发送
                await SendSmallPacketAsync(messageId, data).ConfigureAwait(false);
            }
            else
            {
                // 大包分块发送
                await SendLargePacketAsync(messageId, data).ConfigureAwait(false);
            }
        }

        private async Task SendSmallPacketAsync(ushort messageId, byte[] data)
        {
            var totalSize = MessageHeader.Size + data.Length;
            var buffer = ArrayPool<byte>.Shared.Rent(totalSize);

            try
            {
                // 写入消息头
                var header = new MessageHeader(data.Length, messageId, MessageHeader.FlagNone);
                WriteHeader(buffer, 0, header);

                // 写入数据
                Buffer.BlockCopy(data, 0, buffer, MessageHeader.Size, data.Length);

                // 发送
                var request = new SendRequest
                (
                    data: new ArraySegment<byte>(buffer, 0, totalSize),
                    needReturn: true,
                    completion: new TaskCompletionSource<bool>()
                );

                _sendQueue.Add(request);
                await request.Completion.Task.ConfigureAwait(false);
            }
            catch
            {
                ArrayPool<byte>.Shared.Return(buffer);
                throw;
            }
        }

        private async Task SendLargePacketAsync(ushort messageId, byte[] data)
        {
            var chunkId = Interlocked.Increment(ref _nextChunkId);
            var totalChunks = (data.Length + _chunkSize - 1) / _chunkSize;

            for (int i = 0; i < totalChunks; i++)
            {
                var offset = i * _chunkSize;
                var chunkSize = Math.Min(_chunkSize, data.Length - offset);
                var isLastChunk = i == totalChunks - 1;

                // 准备块数据
                var totalSize = MessageHeader.Size + ChunkHeader.Size + chunkSize;
                var buffer = ArrayPool<byte>.Shared.Rent(totalSize);

                try
                {
                    // 写入消息头
                    ushort flags = MessageHeader.FlagLargePacket | MessageHeader.FlagChunked;
                    if (isLastChunk)
                        flags |= MessageHeader.FlagEndOfChunk;

                    var header = new MessageHeader(
                        ChunkHeader.Size + chunkSize,
                        messageId,
                        flags);
                    WriteHeader(buffer, 0, header);

                    // 写入块头
                    var chunkHeader = new ChunkHeader(chunkId, i, totalChunks, chunkSize);
                    WriteChunkHeader(buffer, MessageHeader.Size, chunkHeader);

                    // 写入块数据
                    Buffer.BlockCopy(data, offset, buffer, MessageHeader.Size + ChunkHeader.Size, chunkSize);

                    // 发送
                    var request = new SendRequest
                    (
                        data: new ArraySegment<byte>(buffer, 0, totalSize),
                        needReturn: true,
                        completion: new TaskCompletionSource<bool>()
                    );

                    _sendQueue.Add(request);
                    await request.Completion.Task.ConfigureAwait(false);
                }
                catch
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                    throw;
                }
            }
        }

        #endregion

        #region 连接管理

        public void Start()
        {
            Task.Run(() => SendLoopAsync(_cts.Token));
            Task.Run(() => ReceiveLoopAsync(_cts.Token));

            // 定期清理过期的大包状态
            Task.Run(() => CleanupLoopAsync(_cts.Token));
        }

        private async Task SendLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (!_sendQueue.TryTake(out var request, 100))
                    {
                        continue;
                    }

                    try
                    {
                        await SendDataAsync(request.Data).ConfigureAwait(false);
                        Interlocked.Add(ref _totalBytesSent, request.Data.Count);

                        request.Completion?.SetResult(true);
                    }
                    catch (Exception ex)
                    {
                        request.Completion?.SetException(ex);
                    }
                    finally
                    {
                        if (request.NeedReturn && request.Data.Array != null)
                        {
                            ArrayPool<byte>.Shared.Return(request.Data.Array);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async Task SendDataAsync(ArraySegment<byte> data)
        {
            var sent = 0;
            while (sent < data.Count)
            {
                var bytesToSend = data.Count - sent;
                var sent1 = sent;
                var bytesSent = await Task.Factory.FromAsync(
                    (callback, state) => _socket.BeginSend(
                        data.Array!,
                        data.Offset + sent1,
                        bytesToSend,
                        SocketFlags.None,
                        callback,
                        state),
                    _socket.EndSend,
                    null).ConfigureAwait(false);

                sent += bytesSent;
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    int bytesReceived;

                    lock (_receiveLock)
                    {
                        var freeSpace = _receiveBuffer.Length - _receivePosition;
                        if (freeSpace < 1024)
                        {
                            // 需要处理缓冲区中的数据
                            ProcessBufferedMessages();
                            freeSpace = _receiveBuffer.Length - _receivePosition;
                        }

                        if (freeSpace == 0)
                        {
                            // 缓冲区满，强制处理
                            _receivePosition = 0;
                            continue;
                        }
                    }

                    // 异步接收
                    bytesReceived = await Task.Factory.FromAsync(
                        (callback, state) => _socket.BeginReceive(
                            _receiveBuffer,
                            _receivePosition,
                            _receiveBuffer.Length - _receivePosition,
                            SocketFlags.None,
                            callback,
                            state),
                        _socket.EndReceive,
                        null).ConfigureAwait(false);

                    if (bytesReceived == 0)
                        break;

                    lock (_receiveLock)
                    {
                        _receivePosition += bytesReceived;
                        Interlocked.Add(ref _totalBytesReceived, bytesReceived);

                        ProcessBufferedMessages();
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (SocketException)
            {
            }
        }

        private void ProcessBufferedMessages()
        {
            var processed = 0;

            while (processed + MessageHeader.Size <= _receivePosition)
            {
                // 读取消息头
                var header = ReadHeader(_receiveBuffer, processed);

                if (header.Length < 0 || header.Length > _receiveBuffer.Length)
                {
                    // 无效消息
                    _cts.Cancel();
                    return;
                }

                var totalMessageSize = MessageHeader.Size + header.Length;
                if (processed + totalMessageSize > _receivePosition)
                    break; // 数据不完整

                if (header.IsChunked)
                {
                    // 处理分块消息
                    ProcessChunkedMessage(header, processed + MessageHeader.Size);
                }
                else
                {
                    // 处理普通消息
                    var data = new byte[header.Length];
                    Buffer.BlockCopy(_receiveBuffer, processed + MessageHeader.Size, data, 0, header.Length);

                    Task.Run(() => ProcessMessageAsync(header.MessageId, data));
                }

                processed += totalMessageSize;
            }

            // 移动未处理的数据
            if (processed > 0 && processed < _receivePosition)
            {
                Buffer.BlockCopy(_receiveBuffer, processed, _receiveBuffer, 0, _receivePosition - processed);
                _receivePosition -= processed;
            }
            else if (processed >= _receivePosition)
            {
                _receivePosition = 0;
            }
        }

        private void ProcessChunkedMessage(MessageHeader header, int dataOffset)
        {
            // 读取块头
            var chunkHeader = ReadChunkHeader(_receiveBuffer, dataOffset);

            // 提取块数据
            var chunkData = new byte[chunkHeader.ChunkSize];
            Buffer.BlockCopy(_receiveBuffer, dataOffset + ChunkHeader.Size, chunkData, 0, chunkHeader.ChunkSize);

            // 获取或创建大包状态
            var state = _largePacketStates.GetOrAdd(chunkHeader.ChunkId,
                _ => new LargePacketState(chunkHeader.ChunkId, chunkHeader.TotalChunks, header.MessageId));

            // 添加块
            if (!state.AddChunk(chunkHeader.ChunkIndex, chunkData))
            {
                return;
            }

            // 所有块接收完成
            _largePacketStates.TryRemove(chunkHeader.ChunkId, out _);

            var completeData = state.GetCompleteData();
            if (completeData != null)
            {
                Task.Run(() => ProcessMessageAsync(header.MessageId, completeData));
            }
        }

        private async Task ProcessMessageAsync(ushort messageId, byte[] data)
        {
            if (_handlers.TryGetValue(messageId, out var handlerInfo))
            {
                try
                {
                    await handlerInfo.Handler(data).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // 记录错误
                    OnMessageHandlerError(messageId, ex);
                }
            }
        }

        private async Task CleanupLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(30000, cancellationToken).ConfigureAwait(false); // 30秒清理一次

                    var now = DateTime.UtcNow;
                    var timeout = TimeSpan.FromMinutes(5);

                    var expiredIds = _largePacketStates
                        .Where(kvp => now - kvp.Value.StartTime > timeout)
                        .Select(kvp => kvp.Key)
                        .ToList();

                    foreach (var id in expiredIds)
                    {
                        _largePacketStates.TryRemove(id, out _);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        #endregion

        #region 辅助方法

        private static void WriteHeader(byte[] buffer, int offset, MessageHeader header)
        {
            BitConverter.GetBytes(header.Length).CopyTo(buffer, offset);
            BitConverter.GetBytes(header.MessageId).CopyTo(buffer, offset + 4);
            BitConverter.GetBytes(header.Flags).CopyTo(buffer, offset + 6);
        }

        private static MessageHeader ReadHeader(byte[] buffer, int offset)
        {
            var length = BitConverter.ToInt32(buffer, offset);
            var messageId = BitConverter.ToUInt16(buffer, offset + 4);
            var flags = BitConverter.ToUInt16(buffer, offset + 6);
            return new MessageHeader(length, messageId, flags);
        }

        private static void WriteChunkHeader(byte[] buffer, int offset, ChunkHeader header)
        {
            BitConverter.GetBytes(header.ChunkId).CopyTo(buffer, offset);
            BitConverter.GetBytes(header.ChunkIndex).CopyTo(buffer, offset + 4);
            BitConverter.GetBytes(header.TotalChunks).CopyTo(buffer, offset + 8);
            BitConverter.GetBytes(header.ChunkSize).CopyTo(buffer, offset + 12);
        }

        private static ChunkHeader ReadChunkHeader(byte[] buffer, int offset)
        {
            var chunkId = BitConverter.ToInt32(buffer, offset);
            var chunkIndex = BitConverter.ToInt32(buffer, offset + 4);
            var totalChunks = BitConverter.ToInt32(buffer, offset + 8);
            var chunkSize = BitConverter.ToInt32(buffer, offset + 12);
            return new ChunkHeader(chunkId, chunkIndex, totalChunks, chunkSize);
        }

        private static void OnMessageHandlerError(ushort messageId, Exception ex)
        {
            // 可重写以添加日志
        }

        #endregion

        public void Dispose()
        {
            _cts?.Cancel();
            _sendQueue?.Dispose();
            _socket?.Close();
            _cts?.Dispose();
        }
    }
}
