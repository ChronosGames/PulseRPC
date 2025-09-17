using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PulseRPC.Authentication;
using PulseRPC.Transport;

namespace PulseRPC.Sessions;

/// <summary>
/// 客户端会话管理器默认实现
/// 符合三层抽象架构，专注于应用层的会话管理
/// </summary>
public class ClientSessionManager : IClientSessionManager, IDisposable
{
    private readonly ConcurrentDictionary<string, IClientSession> _sessions;
    private readonly ILogger<ClientSessionManager>? _logger;
    private volatile bool _disposed;

    public ClientSessionManager(ILogger<ClientSessionManager>? logger = null)
    {
        _sessions = new ConcurrentDictionary<string, IClientSession>();
        _logger = logger;
    }

    public int ActiveSessionCount => _sessions.Count;

    public IReadOnlyCollection<string> SessionIds => _sessions.Keys.ToList();

    public bool AddSession(IClientSession session)
    {
        if (session == null) throw new ArgumentNullException(nameof(session));
        ThrowIfDisposed();

        if (_sessions.TryAdd(session.SessionId, session))
        {
            _logger?.LogDebug("Added session {SessionId}", session.SessionId);

            // 订阅会话事件
            session.AuthenticationChanged += OnSessionAuthenticationChanged;

            SessionConnected?.Invoke(this, new SessionConnectedEventArgs(session));
            return true;
        }

        return false;
    }

    public IClientSession? GetSession(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("Session ID cannot be null or whitespace", nameof(sessionId));

        ThrowIfDisposed();

        return _sessions.TryGetValue(sessionId, out var session) ? session : null;
    }

    public async Task<bool> RemoveSessionAsync(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("Session ID cannot be null or whitespace", nameof(sessionId));

        ThrowIfDisposed();

        if (_sessions.TryRemove(sessionId, out var session))
        {
            // 取消事件订阅
            session.AuthenticationChanged -= OnSessionAuthenticationChanged;

            _logger?.LogDebug("Removed session {SessionId}", sessionId);

            SessionDisconnected?.Invoke(this, new SessionDisconnectedEventArgs(session, "Removed"));

            // 清理会话资源
            try
            {
                await session.CloseAsync();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error closing session {SessionId}", sessionId);
            }

            return true;
        }

        return false;
    }

    public IReadOnlyCollection<IClientSession> GetActiveSessions()
    {
        ThrowIfDisposed();
        return _sessions.Values.ToList();
    }

    public IReadOnlyCollection<IClientSession> GetAuthenticatedSessions()
    {
        ThrowIfDisposed();
        return _sessions.Values.Where(s => s.IsAuthenticated).ToList();
    }

    public IReadOnlyCollection<IClientSession> GetSessionsByUser(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("Username cannot be null or whitespace", nameof(username));

        ThrowIfDisposed();

        return _sessions.Values
            .Where(s => s.AuthenticationContext?.Name == username)
            .ToList();
    }

    public async Task<int> BroadcastAsync(ReadOnlyMemory<byte> data, Func<IClientSession, bool>? filter = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var sessions = filter != null
            ? _sessions.Values.Where(filter).ToList()
            : _sessions.Values.ToList();

        var sendTasks = sessions.Select(async session =>
        {
            try
            {
                return await session.SendAsync(data, cancellationToken) ? 1 : 0;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to send data to session {SessionId}", session.SessionId);
                return 0;
            }
        });

        var results = await Task.WhenAll(sendTasks);
        return results.Sum();
    }

    public event EventHandler<SessionConnectedEventArgs>? SessionConnected;
    public event EventHandler<SessionDisconnectedEventArgs>? SessionDisconnected;
    public event EventHandler<SessionAuthenticatedEventArgs>? SessionAuthenticated;

    private void OnSessionAuthenticationChanged(object? sender, AuthenticationChangedEventArgs e)
    {
        if (sender is IClientSession session && e.CurrentAuthentication != null)
        {
            SessionAuthenticated?.Invoke(this, new SessionAuthenticatedEventArgs(session, e.CurrentAuthentication));
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ClientSessionManager));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;

        // 清理所有会话
        var cleanupTasks = _sessions.Values.Select(async session =>
        {
            try
            {
                await session.CloseAsync();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error disposing session {SessionId}", session.SessionId);
            }
        });

        Task.WaitAll(cleanupTasks.ToArray(), TimeSpan.FromSeconds(5));

        _sessions.Clear();

        _logger?.LogInformation("ClientSessionManager disposed");
    }
}