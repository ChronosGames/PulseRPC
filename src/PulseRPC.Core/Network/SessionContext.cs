using System;
using System.Net.Sockets;
using System.IO.Pipelines;
using System.Buffers;
using System.Threading;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using PulseRPC.Protocol.Messages;
using PulseRPC.Protocol.Serialization;

namespace PulseRPC.Protocol.Network;

/// <summary>
/// 表示客户端会话上下文
/// </summary>
public class SessionContext : IDisposable
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly PipeWriter _writer;
    private readonly Dictionary<string, object> _items = new Dictionary<string, object>();
    private readonly MemoryPool<byte> _memoryPool = MemoryPool<byte>.Shared;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// 会话唯一标识
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// 会话创建时间
    /// </summary>
    public DateTime CreatedTime { get; }

    /// <summary>
    /// 最后活动时间
    /// </summary>
    public DateTime LastActivityTime { get; private set; }

    /// <summary>
    /// 初始化会话上下文
    /// </summary>
    /// <param name="client">TCP客户端</param>
    public SessionContext(TcpClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));

        if (!client.Connected)
        {
            throw new InvalidOperationException("TCP客户端未连接");
        }

        _stream = client.GetStream();

        // 配置写入器选项
        var writerOptions = new StreamPipeWriterOptions(
            pool: _memoryPool,
            minimumBufferSize: 4096,
            leaveOpen: true
        );

        _writer = PipeWriter.Create(_stream, writerOptions);
        Id = Guid.NewGuid();
        CreatedTime = DateTime.UtcNow;
        LastActivityTime = CreatedTime;
    }

    /// <summary>
    /// 发送消息
    /// </summary>
    /// <typeparam name="T">消息类型</typeparam>
    /// <param name="message">消息对象</param>
    /// <param name="flags">消息标志</param>
    /// <param name="sequenceId">消息序列号</param>
    public async Task SendAsync<T>(T message, MessageFlags flags = MessageFlags.None, int sequenceId = 0) where T : class, IMessage
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SessionContext));
        }

        if (message == null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        await _writeLock.WaitAsync();
        try
        {
            // 序列化消息
            var data = MessageSerializer.Serialize(message, flags, sequenceId);

            // 获取足够大的缓冲区
            var buffer = _writer.GetMemory(4 + data.Length);

            try
            {
                // 写入消息长度
                BitConverter.GetBytes(data.Length).CopyTo(buffer.Span[..4]);

                // 写入消息数据
                data.CopyTo(buffer[4..]);

                // 推进写入指针
                _writer.Advance(4 + data.Length);

                // 立即刷新数据
                var result = await _writer.FlushAsync();
                if (result.IsCompleted || result.IsCanceled)
                {
                    throw new IOException("连接已关闭");
                }
            }
            catch (Exception)
            {
                _writer.CancelPendingFlush();
                throw;
            }

            // 更新活动时间
            LastActivityTime = DateTime.UtcNow;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// 设置会话数据项
    /// </summary>
    /// <param name="key">键</param>
    /// <param name="value">值</param>
    public void SetItem(string key, object value)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SessionContext));
        _items[key] = value;
    }

    /// <summary>
    /// 获取会话数据项
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    /// <param name="key">键</param>
    /// <returns>数据值，如果不存在则返回默认值</returns>
    public T? GetItem<T>(string key)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SessionContext));
        if (_items.TryGetValue(key, out var value) && value is T typedValue)
        {
            return typedValue;
        }
        return default;
    }

    /// <summary>
    /// 移除会话数据项
    /// </summary>
    /// <param name="key">键</param>
    /// <returns>是否成功移除</returns>
    public bool RemoveItem(string key)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SessionContext));
        return _items.Remove(key);
    }

    /// <summary>
    /// 关闭会话
    /// </summary>
    public void Close()
    {
        if (_disposed) return;
        Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _writeLock.Dispose();
        _writer.Complete();
        _stream.Dispose();
        _client.Dispose();

        GC.SuppressFinalize(this);
    }
}
