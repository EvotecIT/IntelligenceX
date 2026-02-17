using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Copilot;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.Transport;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class CopilotCliTransportLifecycleTests {

    [Fact]
    public async Task AsyncMethods_ThrowObjectDisposed_AfterDispose() {
        using var transport = CreateTransport();
        transport.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => transport.InitializeAsync(
            new ClientInfo("ix", "IntelligenceX", "1.0.0"), CancellationToken.None));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => transport.HealthCheckAsync(null, null, CancellationToken.None));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => transport.GetAccountAsync(CancellationToken.None));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => transport.ListModelsAsync(CancellationToken.None));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => transport.LoginChatGptAsync(
            null, null, false, TimeSpan.FromSeconds(1), CancellationToken.None));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => transport.StartThreadAsync(
            "gpt-5.3-codex", null, null, null, CancellationToken.None));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => transport.StartTurnAsync(
            "thread-1", ChatInput.FromText("hello"), null, null, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task SyncMethods_ThrowObjectDisposed_AfterDispose() {
        using var transport = CreateTransport();
        transport.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => transport.LogoutAsync(CancellationToken.None));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => transport.LoginApiKeyAsync("x", CancellationToken.None));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => transport.ResumeThreadAsync("thread-1", CancellationToken.None));
    }

    [Fact]
    public void Dispose_Is_Idempotent() {
        using var transport = CreateTransport();
        transport.Dispose();
        transport.Dispose();
    }

    [Fact]
    public async Task InitializeAsync_WaitingOnGate_ThrowsObjectDisposed_WhenDisposedConcurrently() {
        using var transport = CreateTransport();
        var gate = GetClientGate(transport);
        gate.Wait();

        var initializeTask = transport.InitializeAsync(new ClientInfo("ix", "IntelligenceX", "1.0.0"), CancellationToken.None);
        var disposeTask = Task.Run(() => transport.Dispose());
        await Task.Delay(50);
        gate.Release();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => initializeTask);
        await disposeTask;
    }

    private static CopilotCliTransport CreateTransport() {
        return new CopilotCliTransport(new CopilotClientOptions());
    }

    private static SemaphoreSlim GetClientGate(CopilotCliTransport transport) {
        var field = typeof(CopilotCliTransport).GetField("_clientGate", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var value = field!.GetValue(transport);
        return Assert.IsType<SemaphoreSlim>(value);
    }
}
