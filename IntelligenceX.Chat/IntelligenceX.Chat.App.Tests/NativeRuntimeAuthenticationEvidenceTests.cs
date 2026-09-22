using System.IO.Pipes;
using System.Text.Json;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Abstractions.Serialization;
using IntelligenceX.Chat.App.Native;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>Checks authentication evidence against a real local protocol connection without provider traffic.</summary>
public sealed class NativeRuntimeAuthenticationEvidenceTests {
    /// <summary>Old and in-flight metadata cannot survive an account recheck, including refresh failure.</summary>
    [Fact]
    public async Task RecheckInvalidatesPriorAndInFlightRuntimeIdentity() {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var token = timeout.Token;
        var pipeName = "ix-auth-evidence-" + Guid.NewGuid().ToString("N");
        await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var blockedHello = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHello = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closeServer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blockNextHello = false;
        var failHello = false;
        var model = "prior-model";
        var server = Task.Run(async () => {
            await pipe.WaitForConnectionAsync(token);
            using var reader = new StreamReader(pipe, leaveOpen: true);
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            try {
                while (await reader.ReadLineAsync(token) is { } line) {
                    var request = JsonSerializer.Deserialize(line, ChatServiceJsonContext.Default.ChatServiceRequest)!;
                    ChatServiceMessage response;
                    if (request is HelloRequest) {
                        if (blockNextHello) {
                            blockNextHello = false;
                            blockedHello.TrySetResult();
                            await releaseHello.Task.WaitAsync(token);
                        }
                        response = failHello
                            ? new ErrorMessage { Kind = ChatServiceMessageKind.Response, RequestId = request.RequestId, Error = "Synthetic metadata failure" }
                            : new HelloMessage { Kind = ChatServiceMessageKind.Response, Name = "fixture", Version = "1", ProcessId = "0", RequestId = request.RequestId,
                                Policy = new SessionPolicyDto { ReadOnly = true, DangerousToolsEnabled = false, MaxToolRounds = 1,
                                    ParallelTools = false, AllowMutatingParallelToolCalls = false,
                                    RuntimeIdentity = new SessionRuntimeIdentityDto { Transport = "fixture", Model = model } } };
                    } else if (request is ListToolsRequest) {
                        response = new ToolListMessage { Kind = ChatServiceMessageKind.Response, RequestId = request.RequestId, Tools = [] };
                    } else if (request is EnsureLoginRequest) {
                        response = new LoginStatusMessage { Kind = ChatServiceMessageKind.Response, RequestId = request.RequestId, IsAuthenticated = true, AccountId = "new-account" };
                    } else {
                        throw new InvalidOperationException("Unexpected fixture request: " + request.GetType().Name);
                    }
                    await writer.WriteLineAsync(JsonSerializer.Serialize(response, ChatServiceJsonContext.Default.ChatServiceMessage).AsMemory(), token);
                    if (request is HelloRequest && model == "current-model") {
                        await closeServer.Task.WaitAsync(token);
                        return;
                    }
                }
            } catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (IOException) when (!pipe.IsConnected) { }
            catch (ObjectDisposedException) { }
        }, token);
        var runtime = new NativeChatServiceRuntime(pipeName);
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.MetadataInvalidated += () => disconnected.TrySetResult();
        try {
            await runtime.RefreshSessionPolicyAsync(token);
            Assert.Equal("prior-model", runtime.SessionPolicy?.RuntimeIdentity?.Model);

            blockNextHello = true;
            var oldRefresh = runtime.RefreshSessionPolicyAsync(token);
            await blockedHello.Task.WaitAsync(token);
            var loginTask = runtime.EnsureLoginAsync(_ => Task.CompletedTask, token);
            releaseHello.TrySetResult();
            await oldRefresh;
            var login = await loginTask;
            Assert.Equal("new-account", login.AccountId);
            Assert.Null(runtime.SessionPolicy);
            Assert.Null(runtime.ToolDefinitions);

            failHello = true;
            await Assert.ThrowsAnyAsync<Exception>(() => runtime.RefreshSessionPolicyAsync(token));
            Assert.Null(runtime.SessionPolicy);
            var text = NativeRuntimeContextFormatter.Format(runtime.SessionPolicy?.RuntimeIdentity, "default", login.AccountId, true);
            Assert.Contains("Service model not confirmed", text);
            Assert.Contains("new-account", text);
            Assert.DoesNotContain("prior-model", text);

            failHello = false;
            model = "current-model";
            await runtime.RefreshSessionPolicyAsync(token);
            Assert.Equal("current-model", runtime.SessionPolicy?.RuntimeIdentity?.Model);
            closeServer.TrySetResult();
            await server.WaitAsync(token);
            pipe.Dispose();
            await disconnected.Task.WaitAsync(token);
            Assert.Null(runtime.SessionPolicy);
            Assert.DoesNotContain("current-model", NativeRuntimeContextFormatter.Format(
                runtime.SessionPolicy?.RuntimeIdentity, "default", login.AccountId, true));
        } finally {
            releaseHello.TrySetResult();
            closeServer.TrySetResult();
            await runtime.DisposeAsync();
            timeout.Cancel();
            await server;
        }
    }
}
