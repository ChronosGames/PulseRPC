using System.Diagnostics;
using FluentAssertions;
using PulseRPC.Benchmark.Clustering;
using PulseRPC.Clustering;
using PulseRPC.Server.Clustering;
using Xunit;

namespace PulseRPC.Server.Tests.Clustering;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ThreeNodeTcpEndToEndCollection
{
    public const string Name = "Three-node TCP end-to-end";
}

/// <summary>
/// 外部用户经 A 的 Gateway Front、B 中继到 C 的真实 loopback TCP 三跳回归：
/// 不替换 INodeTransport/INodeLink，并在 C 校验完整 caller claims 与 token 剥离。
/// </summary>
[Collection(ThreeNodeTcpEndToEndCollection.Name)]
public sealed class ThreeNodeTcpEndToEndTests
{
    [Fact]
    public async Task GatewayA_To_B_To_C_MustReturnResponse_ThenFailFastWithoutDuplicate_AndRecoverAfterTtl()
    {
        var options = new ThreeNodeTcpTopologyOptions
        {
            ConnectTimeout = TimeSpan.FromSeconds(1),
            RequestTimeout = TimeSpan.FromSeconds(5),
            RecoveryTtl = TimeSpan.FromSeconds(5),
            LeaseDuration = TimeSpan.FromMilliseconds(500),
        };
        using var testTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var topology = await ThreeNodeTcpTopology.StartAsync(options, testTimeout.Token);

        topology.GetNodeTransport(ThreeNodeTcpTopology.NodeA).Should().BeOfType<TcpNodeTransport>();
        topology.GetNodeTransport(ThreeNodeTcpTopology.NodeB).Should().BeOfType<TcpNodeTransport>();
        topology.GetNodeTransport(ThreeNodeTcpTopology.NodeC).Should().BeOfType<TcpNodeTransport>();
        topology.GetLeaseStore(ThreeNodeTcpTopology.NodeA)
            .Should().BeSameAs(topology.GetLeaseStore(ThreeNodeTcpTopology.NodeB));
        topology.GetLeaseStore(ThreeNodeTcpTopology.NodeB)
            .Should().BeSameAs(topology.GetLeaseStore(ThreeNodeTcpTopology.NodeC));

        var aSession = await ((IVersionedNodeTransport)topology.GetNodeTransport(ThreeNodeTcpTopology.NodeA))
            .GetSessionAsync(ThreeNodeTcpTopology.NodeB, testTimeout.Token);
        var bSession = await ((IVersionedNodeTransport)topology.GetNodeTransport(ThreeNodeTcpTopology.NodeB))
            .GetSessionAsync(ThreeNodeTcpTopology.NodeC, testTimeout.Token);
        aSession.WireVersion.Should().Be(NodeWireProtocol.CurrentWireVersion);
        bSession.WireVersion.Should().Be(NodeWireProtocol.CurrentWireVersion);
        aSession.Capabilities.Should().HaveFlag(NodeWireProtocol.SupportedCapabilities);
        bSession.Capabilities.Should().HaveFlag(NodeWireProtocol.SupportedCapabilities);

        var response = await topology.InvokeAsync("healthy", testTimeout.Token);

        response.Should().Be("healthy|node-a>node-b>node-c");
        topology.EntryExecutionCount.Should().Be(1);
        topology.TerminalExecutionCount.Should().Be(1);

        topology.PauseBeforeTerminalForward();
        var interruptedRequest = topology.InvokeAsync("interrupted", testTimeout.Token).AsTask();
        await topology.WaitUntilTerminalForwardAsync(testTimeout.Token);
        await topology.StopNodeCAsync(testTimeout.Token);

        var failureLatency = Stopwatch.StartNew();
        topology.ResumeTerminalForward();
        Func<Task> observeFailure = async () => await interruptedRequest;

        var injectedFailure = (await observeFailure.Should().ThrowAsync<Exception>()).Which;
        Console.WriteLine($"Injected failure: {injectedFailure.GetType().Name}: {injectedFailure.Message}");
        failureLatency.Stop();
        failureLatency.Elapsed.Should().BeLessThan(
            TimeSpan.FromSeconds(3),
            "节点断开应由真实 TCP 连接状态/请求超时快速显式上报");
        topology.EntryExecutionCount.Should().Be(2);
        topology.TerminalExecutionCount.Should().Be(1, "C 停止后不能静默执行或重放业务处理");

        await WaitUntilAsync(
            () => !topology.GetLiveNodeIds(ThreeNodeTcpTopology.NodeB).Contains(ThreeNodeTcpTopology.NodeC),
            TimeSpan.FromSeconds(1),
            testTimeout.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(150), testTimeout.Token);
        topology.TerminalExecutionCount.Should().Be(1, "失败请求不能在后台迟到执行或重复执行");

        await topology.RestartNodeCAsync(testTimeout.Token);
        topology.GetLiveNodeIds(ThreeNodeTcpTopology.NodeB)
            .Should().NotContain(ThreeNodeTcpTopology.NodeC, "隔离 TTL 未到前不应提前恢复成员资格");
        await WaitUntilAsync(
            () => topology.GetLiveNodeIds(ThreeNodeTcpTopology.NodeB).Contains(ThreeNodeTcpTopology.NodeC),
            TimeSpan.FromSeconds(8),
            testTimeout.Token);

        var recovered = await topology.InvokeAsync("recovered", testTimeout.Token);

        recovered.Should().Be("recovered|node-a>node-b>node-c");
        topology.EntryExecutionCount.Should().Be(3);
        topology.TerminalExecutionCount.Should().Be(2);
    }

    private static async Task WaitUntilAsync(
        Func<bool> predicate,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);
        while (!predicate())
        {
            if (Stopwatch.GetTimestamp() >= deadline)
            {
                throw new TimeoutException($"条件在 {timeout} 内未满足。");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
        }
    }
}
