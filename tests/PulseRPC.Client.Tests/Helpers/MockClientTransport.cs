using System;
using System.Collections.Concurrent;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using PulseRPC.Shared;

namespace PulseRPC.Client.Tests.Helpers;

/// <summary>
/// 用于单元测试的 <see cref="IClientTransport"/> 模拟实现：
/// - 捕获所有 <see cref="SendAsync"/> 发送的帧（可按超时取出）；
/// - 提供 <see cref="Receive"/> 主动触发 <see cref="DataReceived"/>（模拟服务端下发帧）。
/// </summary>
internal sealed class MockClientTransport : IClientTransport
{
    private readonly BlockingCollection<byte[]> _sentFrames = new();
    private static readonly EndPoint DummyEndPoint = new IPEndPoint(IPAddress.Loopback, 0);

    public string Id { get; } = "mock-client";
    public TransportType Type => TransportType.TCP;
    public bool IsConnected { get; set; } = true;
    public ConnectionState State { get; set; } = ConnectionState.Connected;
    public EndPoint LocalEndPoint => DummyEndPoint;
    public EndPoint RemoteEndPoint => DummyEndPoint;

    public event EventHandler<TransportStateEventArgs>? StateChanged;
    public event EventHandler<TransportDataEventArgs>? DataReceived;

    /// <summary>下一次 <see cref="SendAsync"/> 的返回值（默认 true）。</summary>
    public bool SendResult { get; set; } = true;

    public Task<bool> SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        _sentFrames.Add(data.ToArray());
        return Task.FromResult(SendResult);
    }

    /// <summary>模拟服务端下发一帧原始数据。</summary>
    public void Receive(byte[] frame)
    {
        DataReceived?.Invoke(this, new TransportDataEventArgs(frame));
    }

    /// <summary>模拟传输层状态变更。</summary>
    public void SimulateStateChanged(ConnectionState previous, ConnectionState current)
    {
        State = current;
        IsConnected = current == ConnectionState.Connected;
        StateChanged?.Invoke(this, new TransportStateEventArgs(Id, previous, current));
    }

    /// <summary>等待并取出下一帧已发送数据；超时返回 null。</summary>
    public byte[]? WaitForSentFrame(int timeoutMs = 2000)
    {
        return _sentFrames.TryTake(out var frame, timeoutMs) ? frame : null;
    }

    public Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public void Dispose()
    {
        _sentFrames.Dispose();
    }
}
