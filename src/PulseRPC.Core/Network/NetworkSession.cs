using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using MemoryPack;
using Microsoft.Extensions.Logging;

namespace PulseRPC.Network;

/// <summary>
/// 网络会话
/// </summary>
public class NetworkSession : IDisposable
{
    private const int PacketHeaderSize = 7; // 2字节长度 + 1字节标记 + 2字节流水号 + 2字节类型ID

    private readonly Socket _socket;
    private readonly ILogger _logger;
    private readonly NetworkOptions _options;
    private readonly IPulseService _pulseService;

    private readonly NetworkStream _stream;
    private readonly PipeReader _reader;
    private readonly PipeWriter _writer;
    private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
    private readonly MetaData<string> _metaData = new();

    private readonly CancellationTokenSource _cts = new CancellationTokenSource();
    private int _sequenceIdCounter;

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
    public NetworkSession(
        ILogger logger,
        Socket socket,
        IPulseService pulseService,
        NetworkOptions? options = null)
    {
        _logger = logger;
        _socket = socket;
        _pulseService = pulseService;
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
    }

    /// <summary>
    /// 配置Socket选项
    /// </summary>
    private void ConfigureSocket(Socket socket)
    {
        // 禁用Nagle算法以减少延迟
        socket.NoDelay = true;

        // 设置发送和接收缓冲区大小
        socket.SendBufferSize = _options.SendBufferSize;
        socket.ReceiveBufferSize = _options.RecvBufferSize;

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
                    await ProcessMessagesAsync(buffer);
                }
                finally
                {
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
            _logger.LogError(ex, "处理消息时出错");
            Disconnected?.Invoke(this, ex);
        }
        finally
        {
            await _reader.CompleteAsync();
            await _writer.CompleteAsync();
        }
    }

    /// <summary>
    /// 处理所有消息
    /// </summary>
    private async Task ProcessMessagesAsync(ReadOnlySequence<byte> buffer)
    {
        while (TryReadPacketHeader(ref buffer, out var packetLength, out var flags, out var sequenceId, out var remainingBuffer))
        {
            if (remainingBuffer.Length < packetLength)
            {
                // 数据不足
                break;
            }

            // 提取消息内容
            var messageBuffer = remainingBuffer.Slice(0, packetLength);

            // 处理消息
            await _pulseService.ProcessMessageAsync(this, sequenceId, messageBuffer, _cts.Token);

            // 更新计数
            Interlocked.Increment(ref _messagesReceived);
            Interlocked.Add(ref _bytesReceived, packetLength + PacketHeaderSize);

            // 移动缓冲区位置
            buffer = remainingBuffer.Slice(packetLength);
        }
    }

    /// <summary>
    /// 读取包头
    /// </summary>
    private bool TryReadPacketHeader(
        ref ReadOnlySequence<byte> buffer,
        out int packetLength,
        out byte flags,
        out ushort sequenceId,
        out ReadOnlySequence<byte> remainingBuffer)
    {
        packetLength = 0;
        flags = 0;
        sequenceId = 0;
        remainingBuffer = buffer;

        if (buffer.Length < PacketHeaderSize)
        {
            return false;
        }

        // 读取包头
        Span<byte> headerSpan = stackalloc byte[PacketHeaderSize];
        buffer.Slice(0, PacketHeaderSize).CopyTo(headerSpan);

        packetLength = BinaryPrimitives.ReadUInt16LittleEndian(headerSpan);
        flags = headerSpan[2];
        sequenceId = BinaryPrimitives.ReadUInt16LittleEndian(headerSpan.Slice(3, 2));

        // 检查包长度是否合理
        if (packetLength <= 0 || packetLength > _options.MaxPacketSize)
        {
            throw new InvalidOperationException($"无效的包长度: {packetLength}");
        }

        // 返回剩余缓冲区
        remainingBuffer = buffer.Slice(PacketHeaderSize);
        return true;
    }

    /// <summary>
    /// 获取下一个序列号
    /// </summary>
    public ushort GetNextSequenceId()
    {
        ushort id;

        do
        {
            var nextValue = Interlocked.Increment(ref _sequenceIdCounter);
            id = (ushort)(nextValue & 0xFFFF);
        } while (id == 0);

        return id;
    }

    /// <summary>
    /// 发送数据包
    /// </summary>
    public async Task<bool> SendPacketAsync<T>(T message, ushort sequenceId) where T : IMemoryPackable<T>
    {
        if (_isDisposed)
            return false;

        await _sendLock.WaitAsync();
        try
        {
            // 估计序列化后的大小
            var estimatedSize = EstimateSerializedSize(message);

            // 获取输出缓冲区
            var headerMemory = _writer.GetMemory(PacketHeaderSize + estimatedSize);

            // 跳过头部，稍后填充
            _writer.Advance(PacketHeaderSize - 2);

            // 序列化消息体
            _pulseService.Serialize(_writer, message);

            // 计算实际大小
            var actualSize = _writer.UnflushedBytes - (PacketHeaderSize - 2);

            // 回写头部
            BinaryPrimitives.WriteUInt16LittleEndian(headerMemory.Span, (ushort)actualSize);
            headerMemory.Span[2] = 0; // 标志位，默认0
            BinaryPrimitives.WriteUInt16LittleEndian(headerMemory.Span.Slice(3, 2), sequenceId);

            // 刷新数据
            await _writer.FlushAsync();

            // 更新计数
            Interlocked.Increment(ref _messagesSent);
            Interlocked.Add(ref _bytesSent, actualSize + PacketHeaderSize);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送消息时出错");
            return false;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>
    /// 估计序列化后的大小
    /// </summary>
    private static int EstimateSerializedSize<T>(T message)
    {
        if (message == null)
            return 4;

        var type = typeof(T);

        if (type.IsPrimitive)
            return 8;

        if (type == typeof(string))
            return ((string)(object)message).Length * 2 + 8;

        if (type.IsClass)
            return 256;

        if (type.IsValueType)
            return 128;

        return 512;
    }

    /// <summary>
    /// 关闭会话
    /// </summary>
    public void Close()
    {
        _cts.Cancel();
        Dispose();
    }

    /// <summary>
    /// 处理资源
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
            return;

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
