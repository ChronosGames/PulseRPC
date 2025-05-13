using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MemoryPack;
using PulseRPC.Protocol.Compression;
using PulseRPC.Protocol.Messages;

namespace PulseRPC.Protocol.Network;

/// <summary>
/// 零复制网络会话 - 高性能版本
/// </summary>
public class NetworkSession
{
    private readonly Socket _socket;
    private readonly NetworkOptions _options;
    private readonly Pipe _receivePipe;
    private readonly Pipe _sendPipe;
    private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
    private bool _isDisposed;
    private readonly CancellationTokenSource _cts = new();
    private readonly MetaData<string> _metaData = new();
    private readonly Func<NetworkSession, IPacket, CancellationToken, Task> _callback;

    /// <summary>
    /// 连接断开事件
    /// </summary>
    public event Action<NetworkSession, Exception>? Disconnected;

    /// <summary>
    /// 远程终结点
    /// </summary>
    public EndPoint? RemoteEndPoint => _socket.RemoteEndPoint;

    /// <summary>
    /// 构造函数
    /// </summary>
    public NetworkSession(Socket socket, Func<NetworkSession, IPacket, CancellationToken, Task> callback, NetworkOptions? options = null)
    {
        _socket = socket ?? throw new ArgumentNullException(nameof(socket));
        _options = options ?? new NetworkOptions();
        _socket.NoDelay = true;
        _socket.SendBufferSize = _options.SocketBufferSize;
        _socket.ReceiveBufferSize = _options.SocketBufferSize;

        // 使用定制的PipeOptions
        var pipeOptions = new PipeOptions(
            pool: MemoryPool<byte>.Shared,
            minimumSegmentSize: 4096,
            pauseWriterThreshold: 1024 * 1024, // 1MB
            resumeWriterThreshold: 512 * 1024, // 512KB
            useSynchronizationContext: false);

        _receivePipe = new Pipe(pipeOptions);
        _sendPipe = new Pipe(pipeOptions);
        _callback = callback;
    }

    /// <summary>
    /// 开始处理网络消息
    /// </summary>
    public void Start()
    {
        // 启动发送/接收任务
        Task.Run(ReceiveLoopAsync);
        Task.Run(SendLoopAsync);

        // 启动帧处理器
        var frameProcessor = new FrameProcessor(
            _receivePipe.Reader,
            HandleFrameAsync,
            _cts.Token);

        Task.Run(() => frameProcessor.ProcessFramesAsync());
    }

    /// <summary>
    /// 接收循环 - 将数据从Socket读取到Pipe
    /// </summary>
    private async Task ReceiveLoopAsync()
    {
        const int minimumBufferSize = 4096;

        try
        {
            while (!_cts.IsCancellationRequested)
            {
                // 获取至少4KB的内存
                var memory = _receivePipe.Writer.GetMemory(minimumBufferSize);

                // 直接接收到Pipe内存中
                var bytesRead = await _socket.ReceiveAsync(memory, SocketFlags.None, _cts.Token);

                if (bytesRead == 0)
                {
                    // 连接已关闭
                    break;
                }

                // 更新写入位置并刷新
                _receivePipe.Writer.Advance(bytesRead);
                var flushResult = await _receivePipe.Writer.FlushAsync(_cts.Token);

                if (flushResult.IsCompleted || flushResult.IsCanceled)
                {
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            OnDisconnected(ex);
        }
        finally
        {
            await _receivePipe.Writer.CompleteAsync();
        }
    }

    /// <summary>
    /// 发送循环 - 将数据从Pipe发送到Socket
    /// </summary>
    private async Task SendLoopAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var readResult = await _sendPipe.Reader.ReadAsync(_cts.Token);

                if (readResult.IsCanceled)
                {
                    break;
                }

                var buffer = readResult.Buffer;

                if (buffer.IsEmpty && readResult.IsCompleted)
                {
                    break;
                }

                // 发送数据
                if (!buffer.IsEmpty)
                {
                    try
                    {
                        // 合并发送多个缓冲区片段
                        if (buffer.IsSingleSegment)
                        {
                            // 单段缓冲区，直接发送
                            await _socket.SendAsync(buffer.First, SocketFlags.None, _cts.Token);
                        }
                        else
                        {
                            // 多段缓冲区，创建BufferList发送
                            var segments = new List<ArraySegment<byte>>();
                            foreach (var segment in buffer)
                            {
                                segments.Add(new ArraySegment<byte>(segment.ToArray()));
                            }

                            await _socket.SendAsync(segments, SocketFlags.None);
                        }
                    }
                    catch (Exception ex)
                    {
                        OnDisconnected(ex);
                        break;
                    }
                }

                _sendPipe.Reader.AdvanceTo(buffer.End);

                if (readResult.IsCompleted)
                {
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            OnDisconnected(ex);
        }
        finally
        {
            await _sendPipe.Reader.CompleteAsync();
        }
    }

    /// <summary>
    /// 处理接收到的帧
    /// </summary>
    private async Task HandleFrameAsync(ReadOnlySequence<byte> frameData, bool isCompressed)
    {
        // 将帧数据反序列化为消息
        IPacket packet;

        if (isCompressed)
        {
            var compressedData = frameData.ToArray(); // 不得不复制一次
            var decompressedData = await MessageCompressor.DecompressAsync(compressedData);
            packet = MemoryPackSerializer.Deserialize<IPacket>(decompressedData)!;
        }
        else
        {
            // 未压缩
            if (frameData.IsSingleSegment)
            {
                // 单段数据，直接反序列化
                packet = MemoryPackSerializer.Deserialize<IPacket>(frameData.First.Span)!;
            }
            else
            {
                // 多段数据，需要复制到连续内存
                var dataArray = frameData.ToArray();
                packet = MemoryPackSerializer.Deserialize<IPacket>(dataArray)!;
            }
        }

        // 分发处理
        await _callback(this, packet, _cts.Token);
    }

    /// <summary>
    /// 发送数据包
    /// </summary>
    public async Task SendPacketAsync(IPacket packet)
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(NetworkSession));
        }

        await _sendLock.WaitAsync();
        try
        {
            // 序列化消息
            var packetData = MemoryPackSerializer.Serialize(packet);

            // 确定是否需要压缩
            var shouldCompress = CompressionStrategy.ShouldCompress(packetData, _options);
            var dataToSend = packetData;

            if (shouldCompress)
            {
                var compressionLevel = CompressionStrategy.SelectCompressionLevel(packetData.Length);
                dataToSend = await MessageCompressor.CompressAsync(packetData, compressionLevel);
            }

            // 使用零复制API将帧写入到发送管道
            await FrameProcessor.WriteMarshaledFrameAsync(
                _sendPipe.Writer,
                dataToSend,
                shouldCompress,
                packet.Type,
                packet.SequenceId,
                packet switch
                {
                    Request req => req.RequestId,
                    Response resp => resp.RequestId,
                    _ => null
                });

            // 如果使用了压缩，但dataToSend不是packetData，需要释放
            if (shouldCompress && dataToSend != packetData)
            {
                ArrayPool<byte>.Shared.Return(dataToSend);
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>
    /// 处理连接断开
    /// </summary>
    private void OnDisconnected(Exception ex)
    {
        if (!_isDisposed)
        {
            Disconnected?.Invoke(this, ex);
        }
    }

    /// <summary>
    /// 关闭连接
    /// </summary>
    public void Close()
    {
        Dispose();
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        _cts.Cancel();
        _cts.Dispose();

        try
        {
            _socket.Shutdown(SocketShutdown.Both);
        }
        catch
        {
            // 忽略关闭时的异常
        }

        _socket.Close();
        _sendLock.Dispose();
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
