using PulseRPC.Messaging;
using PulseRPC.Transport;

namespace PulseRPC.Client;


/// <summary>
/// 连接状态机 - 管理连接状态转换
/// </summary>
public sealed class ConnectionStateMachine
{
    private readonly object _lock = new();
    private ExtendedConnectionState _currentState = ExtendedConnectionState.Uninitialized;
    private readonly Dictionary<ExtendedConnectionState, DateTime> _stateHistory = new();
    private readonly List<ConnectionStateChangedEventArgs> _transitions = new();

    /// <summary>
    /// 连接ID
    /// </summary>
    public string ConnectionId { get; }

    /// <summary>
    /// 当前状态
    /// </summary>
    public ExtendedConnectionState CurrentState
    {
        get
        {
            lock (_lock)
            {
                return _currentState;
            }
        }
    }

    /// <summary>
    /// 状态变化事件
    /// </summary>
    public event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// 构造函数
    /// </summary>
    public ConnectionStateMachine(string connectionId)
    {
        ConnectionId = connectionId ?? throw new ArgumentNullException(nameof(connectionId));
        _stateHistory[_currentState] = DateTime.UtcNow;
    }

    /// <summary>
    /// 转换状态
    /// </summary>
    public bool TryTransition(ExtendedConnectionState newState, string? reason = null, Exception? exception = null)
    {
        lock (_lock)
        {
            if (!IsValidTransition(_currentState, newState))
            {
                return false;
            }

            var previousState = _currentState;
            _currentState = newState;
            var timestamp = DateTime.UtcNow;

            _stateHistory[newState] = timestamp;

            var eventArgs = new ConnectionStateChangedEventArgs
            {
                ConnectionId = ConnectionId,
                PreviousState = previousState,
                CurrentState = newState,
                Timestamp = timestamp,
                Reason = reason,
                Exception = exception
            };

            _transitions.Add(eventArgs);

            // 保持状态历史记录在合理大小内
            if (_transitions.Count > 100)
            {
                _transitions.RemoveRange(0, 50);
            }

            StateChanged?.Invoke(this, eventArgs);
            return true;
        }
    }

    /// <summary>
    /// 强制设置状态（跳过验证）
    /// </summary>
    public void ForceSetState(ExtendedConnectionState newState, string? reason = null, Exception? exception = null)
    {
        lock (_lock)
        {
            var previousState = _currentState;
            _currentState = newState;
            var timestamp = DateTime.UtcNow;

            _stateHistory[newState] = timestamp;

            var eventArgs = new ConnectionStateChangedEventArgs
            {
                ConnectionId = ConnectionId,
                PreviousState = previousState,
                CurrentState = newState,
                Timestamp = timestamp,
                Reason = reason ?? "Forced transition",
                Exception = exception
            };

            _transitions.Add(eventArgs);
            StateChanged?.Invoke(this, eventArgs);
        }
    }

    /// <summary>
    /// 检查状态转换是否有效
    /// </summary>
    public static bool IsValidTransition(ExtendedConnectionState from, ExtendedConnectionState to)
    {
        return from switch
        {
            ExtendedConnectionState.Uninitialized => to is ExtendedConnectionState.Initializing or ExtendedConnectionState.Disposed,

            ExtendedConnectionState.Initializing => to is ExtendedConnectionState.Connecting or ExtendedConnectionState.Failed or ExtendedConnectionState.Disposed,

            ExtendedConnectionState.Connecting => to is ExtendedConnectionState.Connected or ExtendedConnectionState.Failed or ExtendedConnectionState.Reconnecting or ExtendedConnectionState.Disposed,

            ExtendedConnectionState.Connected => to is ExtendedConnectionState.Active or ExtendedConnectionState.Idle or ExtendedConnectionState.Disconnecting or ExtendedConnectionState.Failed or ExtendedConnectionState.Disposed,

            ExtendedConnectionState.Idle => to is ExtendedConnectionState.Active or ExtendedConnectionState.Disconnecting or ExtendedConnectionState.Failed or ExtendedConnectionState.Disposed,

            ExtendedConnectionState.Active => to is ExtendedConnectionState.Idle or ExtendedConnectionState.Disconnecting or ExtendedConnectionState.Failed or ExtendedConnectionState.Disposed,

            ExtendedConnectionState.Reconnecting => to is ExtendedConnectionState.Connected or ExtendedConnectionState.Failed or ExtendedConnectionState.Disconnecting or ExtendedConnectionState.Disposed,

            ExtendedConnectionState.Disconnecting => to is ExtendedConnectionState.Disconnected or ExtendedConnectionState.Failed or ExtendedConnectionState.Disposed,

            ExtendedConnectionState.Disconnected => to is ExtendedConnectionState.Connecting or ExtendedConnectionState.Reconnecting or ExtendedConnectionState.Disposed,

            ExtendedConnectionState.Failed => to is ExtendedConnectionState.Reconnecting or ExtendedConnectionState.Connecting or ExtendedConnectionState.Disposed,

            ExtendedConnectionState.Disposed => false, // 一旦释放就不能转换到其他状态

            _ => false
        };
    }

    /// <summary>
    /// 获取状态历史
    /// </summary>
    public IReadOnlyList<ConnectionStateChangedEventArgs> GetStateHistory()
    {
        lock (_lock)
        {
            return _transitions.ToList();
        }
    }

    /// <summary>
    /// 获取在指定状态的总时间
    /// </summary>
    public TimeSpan GetTimeInState(ExtendedConnectionState state)
    {
        lock (_lock)
        {
            var stateTransitions = _transitions.Where(t => t.CurrentState == state).ToList();
            if (stateTransitions.Count == 0)
                return TimeSpan.Zero;

            var totalTime = TimeSpan.Zero;
            for (int i = 0; i < stateTransitions.Count; i++)
            {
                var start = stateTransitions[i].Timestamp;
                var end = i + 1 < stateTransitions.Count
                    ? stateTransitions[i + 1].Timestamp
                    : DateTime.UtcNow;

                if (stateTransitions[i].CurrentState == state)
                {
                    totalTime += end - start;
                }
            }

            return totalTime;
        }
    }

    /// <summary>
    /// 检查连接是否健康
    /// </summary>
    public bool IsHealthy()
    {
        return CurrentState is ExtendedConnectionState.Connected
                            or ExtendedConnectionState.Active
                            or ExtendedConnectionState.Idle;
    }

    /// <summary>
    /// 检查连接是否可用
    /// </summary>
    public bool IsAvailable()
    {
        return CurrentState is ExtendedConnectionState.Connected
                            or ExtendedConnectionState.Active
                            or ExtendedConnectionState.Idle;
    }

    /// <summary>
    /// 检查连接是否正在连接
    /// </summary>
    public bool IsConnecting()
    {
        return CurrentState is ExtendedConnectionState.Connecting
                            or ExtendedConnectionState.Reconnecting
                            or ExtendedConnectionState.Initializing;
    }

    /// <summary>
    /// 检查连接是否已结束
    /// </summary>
    public bool IsTerminated()
    {
        return CurrentState is ExtendedConnectionState.Disconnected
                            or ExtendedConnectionState.Failed
                            or ExtendedConnectionState.Disposed;
    }

    /// <summary>
    /// 转换为传输层状态
    /// </summary>
    public ConnectionState ToTransportState()
    {
        return CurrentState switch
        {
            ExtendedConnectionState.Uninitialized => ConnectionState.Disconnected,
            ExtendedConnectionState.Initializing => ConnectionState.Connecting,
            ExtendedConnectionState.Connecting => ConnectionState.Connecting,
            ExtendedConnectionState.Connected => ConnectionState.Connected,
            ExtendedConnectionState.Idle => ConnectionState.Connected,
            ExtendedConnectionState.Active => ConnectionState.Connected,
            ExtendedConnectionState.Reconnecting => ConnectionState.Reconnecting,
            ExtendedConnectionState.Disconnecting => ConnectionState.Disconnecting,
            ExtendedConnectionState.Disconnected => ConnectionState.Disconnected,
            ExtendedConnectionState.Failed => ConnectionState.Failed,
            ExtendedConnectionState.Disposed => ConnectionState.Disconnected,
            _ => ConnectionState.Disconnected
        };
    }

    public static ExtendedConnectionState ToConnectionState(ConnectionState state)
    {
        return state switch
        {
            ConnectionState.Disconnected => ExtendedConnectionState.Disconnected,
            ConnectionState.Connecting => ExtendedConnectionState.Connecting,
            ConnectionState.Connected => ExtendedConnectionState.Connected,
            ConnectionState.Disconnecting => ExtendedConnectionState.Disconnecting,
            ConnectionState.Failed => ExtendedConnectionState.Failed,
            ConnectionState.Reconnecting => ExtendedConnectionState.Reconnecting,
            _ => ExtendedConnectionState.Uninitialized,
        };
    }

    public override string ToString()
    {
        return $"Connection[{ConnectionId}]: {CurrentState}";
    }
}
