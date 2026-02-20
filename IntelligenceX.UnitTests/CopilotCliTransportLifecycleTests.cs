using System;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
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
        var disposeStarted = await WaitUntilDisposedAsync(transport, TimeSpan.FromSeconds(2));
        Assert.True(disposeStarted, "Dispose did not transition state before timeout.");
        gate.Release();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => initializeTask);
        await disposeTask;
    }

    [Fact]
    public async Task StartTurnAsync_Passes_InfiniteTimeout_ToAvoid_Default_60s_Cap() {
        TimeSpan? observedTimeout = null;
        using var transport = new CopilotCliTransport(new CopilotClientOptions(),
            (session, message, timeout, cancellationToken) => {
                _ = session;
                _ = message;
                _ = cancellationToken;
                observedTimeout = timeout;
                return Task.FromResult<string?>("ok");
            });

        var session = CreateSession("session-1");
        AddThreadState(transport, "thread-1", "gpt-5.3-codex", session);

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var turn = await transport.StartTurnAsync("thread-1", ChatInput.FromText("hello"), null, null, null, null, cts.Token);

        Assert.Equal(Timeout.InfiniteTimeSpan, observedTimeout);
        Assert.Equal("completed", turn.Status);
    }

    private static CopilotCliTransport CreateTransport() {
        return new CopilotCliTransport(new CopilotClientOptions());
    }

    private static CopilotSession CreateSession(string sessionId) {
        var client = (CopilotClient)RuntimeHelpers.GetUninitializedObject(typeof(CopilotClient));
        var ctor = typeof(CopilotSession).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
            new[] { typeof(string), typeof(CopilotClient) }, null);
        Assert.NotNull(ctor);
        return Assert.IsType<CopilotSession>(ctor!.Invoke(new object[] { sessionId, client }));
    }

    private static void AddThreadState(CopilotCliTransport transport, string threadId, string model, CopilotSession session) {
        var stateType = typeof(CopilotCliTransport).GetNestedType("CopilotThreadState", BindingFlags.NonPublic);
        Assert.NotNull(stateType);
        var stateCtor = stateType!.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
            new[] { typeof(string), typeof(CopilotSession) }, null);
        Assert.NotNull(stateCtor);
        var state = stateCtor!.Invoke(new object[] { model, session });

        var threadsField = typeof(CopilotCliTransport).GetField("_threads", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(threadsField);
        var threads = threadsField!.GetValue(transport);
        var dict = Assert.IsAssignableFrom<IDictionary>(threads);
        dict[threadId] = state;
    }

    private static SemaphoreSlim GetClientGate(CopilotCliTransport transport) {
        var field = typeof(CopilotCliTransport).GetField("_clientGate", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var value = field!.GetValue(transport);
        return Assert.IsType<SemaphoreSlim>(value);
    }

    private static async Task<bool> WaitUntilDisposedAsync(CopilotCliTransport transport, TimeSpan timeout) {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout) {
            var ex = await Record.ExceptionAsync(() => transport.LogoutAsync(CancellationToken.None));
            if (ex is ObjectDisposedException) {
                return true;
            }

            await Task.Delay(10);
        }

        return false;
    }
}
