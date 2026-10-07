using GameServer.Contracts;
using Microsoft.Extensions.Logging;
using PulseRPC;
using PulseRPC.Server.Contexts;
using PulseRPC.Server.Services;
using PulseRPC.Server.Services.Management;

namespace GameServer.Host;

[PulseService(DisplayName = "PlayerHub", InstanceScope = ServiceInstanceScope.MultiInstance)]
[Tick(1)]
public sealed class PlayerService : PulseServiceBase, IPlayerHub
{
    private readonly AssetStore _store;
    private readonly string _node;
    private readonly PulseServiceManager _manager;
    private readonly PlayerSessions _sessions;
    private readonly PlayerSessionMode _sessionMode;
    private AssetStore.Fence? _fence;
    private int _retiring;

    internal PlayerService(string player, AssetStore store, string node, PulseServiceManager manager,
        PlayerSessions sessions, PlayerSessionMode sessionMode, ILogger<PlayerService> logger)
        : base("PlayerHub", player, logger, new ServiceExecutionOptions
        {
            QueueCapacity = 32, BackpressureMode = ServiceBackpressureMode.ThrowException,
            MaxConcurrentReentrantRequests = 8
        })
    {
        _store = store;
        _node = node;
        _manager = manager;
        _sessions = sessions;
        _sessionMode = sessionMode;
    }

    public override async Task OnStartingAsync(CancellationToken cancellationToken = default)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, ActorLeaseCancellationToken);
        lifetime.CancelAfter(TimeSpan.FromSeconds(7));
        var owner = Guid.NewGuid();
        while (true)
        {
            lifetime.Token.ThrowIfCancellationRequested();
            try { _fence = await _store.AcquireAsync(ServiceId, owner, lifetime.Token); break; }
            catch (InvalidOperationException) { await Task.Delay(100, lifetime.Token); }
        }
    }

    protected override async Task OnTickAsync(CancellationToken cancellationToken)
    {
        if (_fence is null || Volatile.Read(ref _retiring) != 0) return;
        try { await _store.RenewAsync(_fence, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Database ownership cannot be renewed; retiring {Player}", ServiceId);
            if (Interlocked.Exchange(ref _retiring, 1) == 0)
                _ = RetireAsync(); // Exactly one cleanup per activation; never await its own Tick from Stop.
        }
    }

    private async Task RetireAsync()
    {
        await Task.Yield();
        try { await _manager.RemoveServiceIfSameAsync(this); }
        catch (Exception ex) { Logger.LogError(ex, "Could not retire database-fenced Actor"); }
    }

    public override async Task OnStoppingAsync(CancellationToken cancellationToken = default)
    {
        if (_fence is not null) await _store.ReleaseAsync(_fence, cancellationToken);
    }

    public async Task<PurchaseReceipt> PurchaseAsync(PurchaseCommand command, CancellationToken cancellationToken = default)
    {
        RequireOwner();
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, ActorLeaseCancellationToken);
        var receipt = await _store.PurchaseAsync(RequireFence(), command, operation.Token, _sessionMode.GetStamp(PulseContext.Current));
        receipt.NodeId = _node;
        receipt.Fence = RequireFence().Generation;
        return receipt;
    }

    public async Task<PlayerSnapshot> GetStateAsync(CancellationToken cancellationToken = default)
    {
        RequireOwner();
        if (_sessionMode.GetStamp(PulseContext.Current) is { } stamp)
            await _sessions.ValidateAsync(stamp, cancellationToken);
        var state = await _store.ReadAsync(RequireFence(), cancellationToken);
        state.NodeId = _node;
        return state;
    }

    public Task<string> EchoAsync(string value, CancellationToken cancellationToken = default)
    {
        RequireOwner();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(value);
    }

    private AssetStore.Fence RequireFence() => _fence ?? throw new InvalidOperationException("Actor has no database ownership.");

    private void RequireOwner()
    {
        var caller = PulseContext.Current;
        if (caller is null || caller.IsExpired || !string.Equals(caller.UserId, ServiceId, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Only the owning player can access this Actor.");
        ActorLeaseCancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _retiring) != 0) throw new InvalidOperationException("Actor is retiring after database lease loss.");
    }
}
