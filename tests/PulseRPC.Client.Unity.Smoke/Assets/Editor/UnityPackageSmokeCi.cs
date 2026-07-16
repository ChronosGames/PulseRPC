using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MemoryPack;
using PulseRPC;
using PulseRPC.Client;
using PulseRPC.Client.Configuration;
using UnityEngine;

namespace HelloRPC.Contracts
{
    public interface IHelloHub : IPulseHub
    {
        Task<string> SayHelloAsync(string name);
    }
}

namespace PulseRPC.UnityPackageSmoke
{
    [MemoryPackable]
    public partial class UnitySmokePayload
    {
        public int Sequence { get; set; }
    }

    [Channel("CLIENT")]
    public interface IUnityPackageReceiver : IPulseHub
    {
        Task OnPayloadAsync(UnitySmokePayload payload, CancellationToken cancellationToken = default);
    }

    [PulseClientGeneration(typeof(HelloRPC.Contracts.IHelloHub))]
    [PulseClientGeneration(typeof(IUnityPackageReceiver))]
    public static class UnityPackageGenerationMarker
    {
    }

    internal sealed class UnityPackageReceiver : IUnityPackageReceiver
    {
        public Task OnPayloadAsync(
            UnitySmokePayload payload,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    public static class UnityPackageSmokeCi
    {
        public static void Run()
        {
            _ = typeof(HelloRPC.Contracts.IHelloHubStub);
            _ = typeof(UnityPackageReceiverDispatcher);

            Task.Run(RunAsync).GetAwaiter().GetResult();

            var resultPath = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", "unity-package-smoke.txt"));
            File.WriteAllText(resultPath, "PulseRPC Unity UPM TCP roundtrip passed.");
        }

        private static async Task RunAsync()
        {
            var payload = new UnitySmokePayload { Sequence = 42 };
            var serialized = MemoryPackSerializer.Serialize(payload);
            var deserialized = MemoryPackSerializer.Deserialize<UnitySmokePayload>(serialized);
            if (deserialized == null || deserialized.Sequence != payload.Sequence)
            {
                throw new InvalidOperationException("MemoryPack Unity source generator did not roundtrip the smoke payload.");
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var client = new PulseClientBuilder().Build();

            await client.InitializeAsync(timeout.Token);
            var channel = await client.ConnectToServerAsync(
                "127.0.0.1",
                5055,
                cancellationToken: timeout.Token);
            try
            {
                using var receiver = channel.RegisterReceiver<IUnityPackageReceiver>(new UnityPackageReceiver());
                var hello = channel.GetHub<HelloRPC.Contracts.IHelloHub>();
                var response = await hello.SayHelloAsync("PulseRPC");
                if (!string.Equals(response, "Hello, PulseRPC!", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Unexpected TCP roundtrip response: " + response);
                }
            }
            finally
            {
                await channel.DisconnectAsync();
                await client.StopAsync(cancellationToken: timeout.Token);
            }
        }
    }
}
