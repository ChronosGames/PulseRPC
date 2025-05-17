using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using MemoryPack;
using Microsoft.Extensions.Logging;
using PulseRPC.Protocol.Compression;
using PulseRPC.Protocol.Messages;

namespace PulseRPC.Protocol.Network;

/// <summary>
/// 零复制网络会话 - 高性能版本
/// </summary>
public class NetworkSession
{
    private const int PacketHeaderSize = 7; // 2字节长度 + 1字节标记 + 2字节流水号 + 2字节类型ID

    private readonly Socket _socket;
    private readonly ILogger _logger;

    private readonly NetworkOptions _options;

    // private readonly Pipe _receivePipe;
    // private readonly Pipe _sendPipe;
    // private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
    // private bool _isDisposed;
    // private readonly CancellationTokenSource _cts = new();
    private readonly MetaData<string> _metaData = new();
    private readonly Func<NetworkSession, ushort, IPacket, CancellationToken, Task> _callback;
    private readonly IPulseRPCSerializer _serializer;

    private readonly NetworkStream _stream;
    private readonly PipeReader _reader;
    private readonly PipeWriter _writer;
    private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

    private readonly CancellationTokenSource _cts = new CancellationTokenSource();
    private int _sequenceIdCounter;

    // private readonly IPulseRPCSerializer _messageHandlers;
    private readonly ArrayPool<byte> _arrayPool = ArrayPool<byte>.Shared;
    private bool _isDisposed;

    // 性能计数器
    private long _bytesSent;
    private long _bytesReceived;
    private long _messagesSent;
    private long _messagesReceived;

    /// <summary>
    /// 连接断开事件
    /// </summary>
    public event Action<NetworkSession, Exception>? Disconnected;

    /// <summary>
    /// 构造函数
    /// </summary>
    public NetworkSession(ILogger logger, Socket socket,
        Func<NetworkSession, ushort, IPacket, CancellationToken, Task> callback, IPulseRPCSerializer serializer,
        NetworkOptions? options = null)
    {
        _socket = socket ?? throw new ArgumentNullException(nameof(socket));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serializer = serializer;
        _options = options ?? new NetworkOptions();

        ConfigureSocket(_socket);

        _stream = new NetworkStream(_socket);

        var readerOptions = new StreamPipeReaderOptions(
            bufferSize: 65536, // 64KB
            minimumReadSize: 4096, // 4KB
            pool: MemoryPool<byte>.Shared,
            leaveOpen: false
        );

        var writerOptions = new StreamPipeWriterOptions(
            minimumBufferSize: 65536, // 64KB
            pool: MemoryPool<byte>.Shared,
            leaveOpen: false
        );

        _reader = PipeReader.Create(_stream, readerOptions);
        _writer = PipeWriter.Create(_stream, writerOptions);

        // 使用定制的PipeOptions
        // var pipeOptions = new PipeOptions(
        //     pool: MemoryPool<byte>.Shared,
        //     minimumSegmentSize: 4096,
        //     pauseWriterThreshold: 1024 * 1024, // 1MB
        //     resumeWriterThreshold: 512 * 1024, // 512KB
        //     useSynchronizationContext: false);

        // _receivePipe = new Pipe(pipeOptions);
        // _sendPipe = new Pipe(pipeOptions);
        _callback = callback;
    }

    /// <summary>
    /// 配置Socket选项以优化性能
    /// </summary>
    private void ConfigureSocket(Socket socket)
    {
        // 禁用Nagle算法以减少延迟
        socket.NoDelay = true;

        // 设置发送和接收缓冲区大小
        socket.SendBufferSize = _options.SendBufferSize; // 256KB
        socket.ReceiveBufferSize = _options.RecvBufferSize; // 256KB

        // 配置TCP保活选项
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

        // 配置TCP快速关闭
        socket.LingerState = new LingerOption(true, 0);

        // 尝试启用零拷贝（仅Linux平台支持）
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            try
            {
                // ReSharper disable once InconsistentNaming
                const int SO_ZEROCOPY = 60;
                socket.SetSocketOption(SocketOptionLevel.Socket, (SocketOptionName)SO_ZEROCOPY, 1);
            }
            catch
            {
                // 忽略不支持的错误
            }
        }
    }

    /// <summary>
    /// 开始处理消息
    /// </summary>
    public async Task ProcessMessagesAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var result = await _reader.ReadAsync(_cts.Token);
                var buffer = result.Buffer;

                try
                {
                    // 处理所有完整的消息
                    ProcessMessages(ref buffer);
                }
                finally
                {
                    // 告诉PipeReader我们处理到哪里了
                    _reader.AdvanceTo(buffer.Start, buffer.End);
                }

                if (result.IsCompleted)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"处理消息时出错");
        }
        finally
        {
            await _reader.CompleteAsync();
            await _writer.CompleteAsync();
        }
    }

    /// <summary>
    /// 处理接收到的所有完整消息
    /// </summary>
    private void ProcessMessages(ref ReadOnlySequence<byte> buffer)
    {
        int length = 0;
        while ((length = TryReadMessage(ref buffer, out var id, out var packet)) > 0)
        {
            try
            {
                // 更新计数器
                Interlocked.Increment(ref _messagesReceived);
                Interlocked.Add(ref _bytesReceived, length + PacketHeaderSize);

                // 将消息分发给处理器
                _callback(this, id, packet!, _cts.Token);
                // _messageHandlers.DispatchMessage(packet.TypeId, packet.Data);
            }
            finally
            {
                // 归还缓冲区
                // if (packet!.Data.Length > 0)
                // {
                //     _arrayPool.Return(packet.Data);
                // }
            }
        }
    }

    /// <summary>
    /// 尝试从缓冲区读取一个完整消息
    /// </summary>
    private int TryReadMessage(ref ReadOnlySequence<byte> buffer, out ushort sequenceId, out IPacket? packet)
    {
        sequenceId = ushort.MinValue;
        packet = null;

        if (buffer.Length < PacketHeaderSize)
        {
            return 0;
        }

        // 读取消息长度和类型ID
        Span<byte> headerSpan = stackalloc byte[PacketHeaderSize];
        buffer.Slice(0, PacketHeaderSize).CopyTo(headerSpan);

        var messageLength = (int)BinaryPrimitives.ReadUInt16LittleEndian(headerSpan);
        var flags = headerSpan[2];
        sequenceId = BinaryPrimitives.ReadUInt16LittleEndian(headerSpan[3..5]);

        // 检查长度合理性（防止恶意数据）
        if (messageLength < 0 || messageLength > _options.MaxPacketSize)
        {
            throw new InvalidOperationException($"消息长度无效: {messageLength}");
        }

        // 确保有完整消息
        if (buffer.Length < messageLength + PacketHeaderSize)
        {
            return 0;
        }

        // 创建消息包
        packet = _serializer.Deserialize(buffer.Slice(PacketHeaderSize - 2, messageLength + 2).FirstSpan);

        // 移动缓冲区位置
        buffer = buffer.Slice(messageLength + PacketHeaderSize);

        return messageLength;
    }

    /// <summary>
    /// 获取下一个序列号
    /// </summary>
    public ushort GetNextSequenceId()
    {
        ushort id;

        do
        {
            // 使用 int 类型进行原子递增
            var nextValue = Interlocked.Increment(ref _sequenceIdCounter);

            // 转换为 ushort (0-65535 范围)
            id = (ushort)(nextValue & 0xFFFF);

            // 如果溢出回到0，则继续递增
        } while (id == 0);

        return id;
    }

    /// <summary>
    /// 使用零分配发送对象（高级优化）
    /// </summary>
    public async Task<bool> SendPacketAsync<T>(T message, ushort sequenceId) where T : IPacket
    {
        if (_isDisposed)
            return false;

        await _sendLock.WaitAsync();
        try
        {
            // 首先估计序列化后的大小
            var estimatedSize = EstimateSerializedSize(message);

            // 获取输出缓冲区
            var headerMemory = _writer.GetMemory(PacketHeaderSize + estimatedSize);

            // 保存当前位置
            _writer.Advance(PacketHeaderSize - 2); // 跳过头部，稍后填充

            // 使用MemoryPack直接序列化到管道
            _serializer.Serialize(_writer, message);

            // 确定实际序列化大小
            var actualSize = _writer.UnflushedBytes - PacketHeaderSize;

            // 回写头部
            BinaryPrimitives.WriteUInt16LittleEndian(headerMemory.Span, (ushort)actualSize);
            headerMemory.Span[2] = (byte)PacketFlags.None;
            BinaryPrimitives.WriteUInt16LittleEndian(headerMemory.Span[3..5], sequenceId);

            // 刷新数据
            await _writer.FlushAsync();

            // 更新计数器
            Interlocked.Increment(ref _messagesSent);
            Interlocked.Add(ref _bytesSent, actualSize + PacketHeaderSize);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"发送消息时出错: {ex.Message}");
            return false;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>
    /// 估计序列化后的对象大小
    /// </summary>
    private static int EstimateSerializedSize<T>(T message)
    {
        // 这是一个估计值，根据对象类型设置合理的初始大小
        // 在真实环境中，可以根据对象字段数量或缓存历史大小做更精确的估计
        if (message == null)
            return 4;

        var type = typeof(T);

        if (type.IsPrimitive)
            return 8;

        if (type == typeof(string))
            return ((string)(object)message).Length * 2 + 8;

        if (type.IsClass)
            return 256; // 类的合理初始大小

        if (type.IsValueType)
            return 128; // 结构体的合理初始大小

        return 512; // 默认大小
    }

    /// <summary>
    /// 获取性能统计信息
    /// </summary>
    public string GetPerformanceStats()
    {
        return $"发送: {_messagesSent} 消息 ({_bytesSent} 字节), " +
               $"接收: {_messagesReceived} 消息 ({_bytesReceived} 字节)";
    }

    /// <summary>
    /// 关闭会话
    /// </summary>
    public void Close()
    {
        _cts.Cancel();
        Dispose();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        _cts.Dispose();
        _sendLock.Dispose();

        try
        {
            _socket.Close();
        }
        catch
        {
            // 忽略错误
        }
    }

    public bool TryGetItem<T>(string key, out T? value)
    {
        return _metaData.TryGet(key, out value);
    }

    public T? GetItem<T>(string key)
    {
        return _metaData.Get<T>(key);
    }

    public void SetItem<T>(string key, T value)
    {
        _metaData.Set(key, value);
    }
}
