using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Rpc;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class RpcClientTests {
    private static int GetPendingCount(JsonRpcClient client) {
        var field = typeof(JsonRpcClient).GetField("_pending", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        var value = field!.GetValue(client);
        Assert.NotNull(value);
        var prop = value!.GetType().GetProperty("Count", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(prop);
        return (int)(prop!.GetValue(value) ?? 0);
    }

    [Fact]
    public void CallAsync_Cancellation_CleansPending_AndLateResponseIsIgnored() {
        var client = new JsonRpcClient(_ => Task.CompletedTask);
        long id = 0;
        client.CallStarted += (_, args) => id = args.RequestId ?? 0;

        var protocolError = false;
        client.ProtocolError += (_, _) => protocolError = true;

        using var cts = new CancellationTokenSource();
        var task = client.CallAsync("test", (JsonValue?)null, cts.Token);
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => task.GetAwaiter().GetResult());
        Assert.Equal(0, GetPendingCount(client));

        client.HandleLine($"{{\"id\":{id},\"result\":123}}");
        Assert.False(protocolError);
    }
}
