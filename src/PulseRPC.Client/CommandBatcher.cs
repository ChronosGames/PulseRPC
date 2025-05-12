using PulseRPC.Protocol.Messages;
using PulseRPC.Protocol.Network;

namespace PulseRPC.Client;

/// <summary>
/// 命令批处理器 - 合并多个小型命令减少网络开销
/// </summary>
public class CommandBatcher
{
    private readonly NetworkSession _session;
    private readonly List<Command> _pendingCommands = new List<Command>();
    private readonly SemaphoreSlim _batchLock = new SemaphoreSlim(1, 1);
    private readonly int _maxBatchSize;
    private readonly int _maxBatchDelay;
    private CancellationTokenSource? _batchDelayCts;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="session">网络会话</param>
    /// <param name="maxBatchSize">最大批处理大小</param>
    /// <param name="maxBatchDelay">最大延迟毫秒数</param>
    public CommandBatcher(NetworkSession session, int maxBatchSize = 16, int maxBatchDelay = 33)
    {
        _session = session;
        _maxBatchSize = maxBatchSize;
        _maxBatchDelay = maxBatchDelay;
    }

    /// <summary>
    /// 添加命令到批处理队列
    /// </summary>
    public async Task AddCommandAsync(Command command)
    {
        await _batchLock.WaitAsync();

        try
        {
            // 添加命令到队列
            _pendingCommands.Add(command);

            // 检查是否应该立即发送
            if (_pendingCommands.Count >= _maxBatchSize)
            {
                await FlushAsync();
            }
            else if (_pendingCommands.Count == 1)
            {
                // 这是第一个命令，启动延迟发送计时器
                StartBatchDelayTimer();
            }
        }
        finally
        {
            _batchLock.Release();
        }
    }

    /// <summary>
    /// 启动批处理延迟计时器
    /// </summary>
    private void StartBatchDelayTimer()
    {
        _batchDelayCts?.Cancel();
        _batchDelayCts = new CancellationTokenSource();

        // 异步启动计时器
        Task.Delay(_maxBatchDelay, _batchDelayCts.Token).ContinueWith(async t =>
        {
            if (t.IsCanceled)
                return;

            await _batchLock.WaitAsync();
            try
            {
                if (_pendingCommands.Count > 0)
                {
                    await FlushAsync();
                }
            }
            finally
            {
                _batchLock.Release();
            }
        }, TaskScheduler.Default);
    }

    /// <summary>
    /// 立即发送所有待处理命令
    /// </summary>
    public async Task FlushAsync()
    {
        // 取消任何待处理的计时器
        _batchDelayCts?.Cancel();

        try
        {
            if (Monitor.TryEnter(_pendingCommands, TimeSpan.FromMilliseconds(_maxBatchDelay)))
            {
                if (_pendingCommands.Count == 0)
                {
                    return;
                }

                if (_pendingCommands.Count == 1)
                {
                    // 如果只有一个命令，直接发送
                    await _session.SendPacketAsync(_pendingCommands[0]);
                }
                else
                {
                    // 创建批处理包
                    var batchCommand = new CommandBatch { Commands = _pendingCommands.ToArray() };

                    // 发送批处理包
                    await _session.SendPacketAsync(batchCommand);
                }

                // 清空待处理命令
                _pendingCommands.Clear();
            }
        }
        finally
        {
            Monitor.Exit(_pendingCommands);
        }
    }
}
