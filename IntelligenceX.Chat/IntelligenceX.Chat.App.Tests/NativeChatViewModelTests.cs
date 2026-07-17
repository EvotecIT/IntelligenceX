using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App.Conversation;
using IntelligenceX.Chat.App.Native;
using IntelligenceX.Chat.Client;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Tests the native chat view model without constructing WinUI controls.
/// </summary>
public sealed class NativeChatViewModelTests {
    /// <summary>
    /// Ensures sending a prompt creates native transcript items and applies streamed/final assistant text.
    /// </summary>
    [Fact]
    public async Task SendDraftAsync_AppendsNativeTranscriptItemsAndSettlesAssistantText() {
        var runtime = new ScriptedRuntime(async updates => {
            await updates.Status("Thinking").ConfigureAwait(false);
            await updates.Delta("Hello").ConfigureAwait(false);
            await updates.Delta(" there").ConfigureAwait(false);
            return CreateTurnResult("Hello there.", "thread-1");
        });
        var model = new NativeChatViewModel(runtime) {
            Draft = "  say hello  "
        };

        var sent = await model.SendDraftAsync();

        Assert.True(sent);
        Assert.False(model.IsSending);
        Assert.Equal("Ready", model.StatusText);
        Assert.Equal("", model.Draft);
        Assert.Equal(2, model.Transcript.Count);
        Assert.Equal("user", model.Transcript[0].Role);
        Assert.Equal("say hello", model.Transcript[0].Text);
        Assert.Equal("assistant", model.Transcript[1].Role);
        Assert.Equal("Hello there.", model.Transcript[1].Text);
        Assert.Equal("Complete", model.Transcript[1].Status);
    }

    /// <summary>
    /// Ensures the native surface forwards the shared profile request contract instead of using service defaults.
    /// </summary>
    [Fact]
    public async Task SendAsync_ForwardsSharedRequestOptions() {
        var runtime = new ScriptedRuntime(_ => Task.FromResult(CreateTurnResult(
            "done",
            "thread-1",
            effectiveModel: "runtime-model")));
        var options = new ChatRequestOptions {
            Model = "conversation-model",
            ReasoningEffort = "high",
            DisabledTools = ["unsafe_tool"]
        };
        var model = new NativeChatViewModel(
            runtime,
            requestOptionsProvider: _ => options);

        var sent = await model.SendAsync("use my profile");

        Assert.True(sent);
        Assert.Same(options, Assert.Single(runtime.Requests).Options);
        Assert.Equal("runtime-model", Assert.Single(model.Transcript, item => item.IsAssistant).Model);
    }

    /// <summary>
    /// Ensures the native view model sends the shared envelope and renders only normalized assistant text.
    /// </summary>
    [Fact]
    public async Task SendAsync_UsesSharedTurnProtocolDelegates() {
        const string rawResponse = """
            Visible answer.

            ```ix_memory
            {"upserts":[{"fact":"private protocol","weight":3}]}
            ```
            """;
        var runtime = new ScriptedRuntime(_ => Task.FromResult(CreateTurnResult(rawResponse, "thread-1")));
        var model = new NativeChatViewModel(
            runtime,
            requestTextProvider: (_, text, _) => "shared-envelope\n" + text,
            assistantTurnNormalizer: (_, text, _) => Task.FromResult(
                DesktopChatTurnProtocol.NormalizeAssistantResponse(text)));

        var sent = await model.SendAsync("hello");

        Assert.True(sent);
        Assert.Equal("shared-envelope\nhello", Assert.Single(runtime.Requests).Text);
        var assistant = Assert.Single(model.Transcript, item => item.IsAssistant);
        Assert.Equal("Visible answer.", assistant.Text);
        Assert.DoesNotContain("ix_memory", assistant.Text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures a corrupt history store cannot terminate native window startup.
    /// </summary>
    [Fact]
    public async Task InitializeConversationsAsync_FallsBackToFreshChatWhenLoadFails() {
        var model = new NativeChatViewModel(
            new ScriptedRuntime(_ => Task.FromResult(CreateTurnResult(string.Empty, null))),
            conversationStore: new ThrowingConversationStore());

        await model.InitializeConversationsAsync();

        Assert.Single(model.Conversations);
        Assert.True(model.ActiveConversation.IsEmptyDraft);
        Assert.Contains("History load failed", model.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures slow history persistence cannot admit two turns before IsSending becomes visible.
    /// </summary>
    [Fact]
    public async Task SendAsync_RejectsConcurrentTurnWhileInitialHistorySaveIsPending() {
        var runtime = new ScriptedRuntime(_ => Task.FromResult(CreateTurnResult("done", "thread-1")));
        var store = new BlockingConversationStore();
        var model = new NativeChatViewModel(runtime, conversationStore: store);

        var firstSend = model.SendAsync("first");
        await store.SaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(model.IsSending);
        model.Draft = "keep this draft";
        var other = new NativeConversation("chat-other", "Other");
        model.Conversations.Add(other);

        var secondSent = await model.SendDraftAsync();
        var created = await model.CreateConversationAsync();
        var selected = await model.SelectConversationAsync(other.Id);
        var signInCheck = await model.CheckSignInAsync();
        var signIn = await model.StartSignInAsync();
        store.ReleaseSave.TrySetResult();
        var firstSent = await firstSend;

        Assert.True(firstSent);
        Assert.False(secondSent);
        Assert.False(created);
        Assert.False(selected);
        Assert.False(signInCheck.IsAuthenticated);
        Assert.Contains("running or starting", signInCheck.Error, StringComparison.OrdinalIgnoreCase);
        Assert.False(signIn.IsAuthenticated);
        Assert.Contains("running or starting", signIn.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("keep this draft", model.Draft);
        Assert.NotSame(other, model.ActiveConversation);
        Assert.Single(runtime.Requests);
    }

    /// <summary>
    /// Ensures blank prompts do not create transcript entries.
    /// </summary>
    [Fact]
    public async Task SendDraftAsync_IgnoresBlankPrompt() {
        var model = new NativeChatViewModel(new ScriptedRuntime(_ => Task.FromResult(CreateTurnResult("", null)))) {
            Draft = "  "
        };

        var sent = await model.SendDraftAsync();

        Assert.False(sent);
        Assert.Empty(model.Transcript);
        Assert.Equal("Ready", model.StatusText);
    }

    /// <summary>
    /// Ensures runtime failures become visible native assistant transcript entries.
    /// </summary>
    [Fact]
    public async Task SendAsync_ReportsRunnerFailureInAssistantItem() {
        var model = new NativeChatViewModel(new ScriptedRuntime(_ => throw new InvalidOperationException("pipe unavailable")));

        var sent = await model.SendAsync("hello");

        Assert.False(sent);
        Assert.Equal(2, model.Transcript.Count);
        Assert.Equal("Error", model.StatusText);
        Assert.Equal("Error", model.Transcript[1].Status);
        Assert.Contains("pipe unavailable", model.Transcript[1].Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures provisional snapshot fragments merge with deltas instead of duplicating the current draft.
    /// </summary>
    [Fact]
    public async Task SendAsync_ProvisionalSnapshotsMergeWithStreamingDraft() {
        var runtime = new ScriptedRuntime(async updates => {
            await updates.Delta("Hello").ConfigureAwait(false);
            await updates.Provisional("Hello there").ConfigureAwait(false);
            await updates.Provisional("Hello there.").ConfigureAwait(false);
            return CreateTurnResult(string.Empty, "thread-1");
        });
        var model = new NativeChatViewModel(runtime);

        var sent = await model.SendAsync("hello");

        Assert.True(sent);
        Assert.Equal("Hello there.", model.Transcript[1].Text);
    }

    /// <summary>
    /// Ensures a separate review snapshot does not erase a live streamed answer before the final response.
    /// </summary>
    [Fact]
    public async Task SendAsync_UnrelatedInterimSnapshotDoesNotOverwriteStreamingDraft() {
        var runtime = new ScriptedRuntime(async updates => {
            await updates.Delta("Live streamed answer").ConfigureAwait(false);
            await updates.Interim("Separate review snapshot").ConfigureAwait(false);
            return CreateTurnResult(string.Empty, "thread-1");
        });
        var model = new NativeChatViewModel(runtime);

        var sent = await model.SendAsync("hello");

        Assert.True(sent);
        Assert.Equal("Live streamed answer", model.Transcript[1].Text);
    }

    /// <summary>
    /// Ensures the Stop command cancels the local wait and forwards cancellation to the service request id.
    /// </summary>
    [Fact]
    public async Task CancelActiveTurn_ForwardsCancelRequestToRunner() {
        var runtime = new CancelableRuntime();
        var model = new NativeChatViewModel(runtime);

        var sendTask = model.SendAsync("long task");
        await runtime.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        model.CancelActiveTurn();

        await sendTask;
        var canceledRequestId = await runtime.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(runtime.Requests[0].RequestId, canceledRequestId);
        Assert.Equal("Canceled", model.StatusText);
    }

    /// <summary>
    /// Ensures authentication operations cannot race an active turn on the shared service client.
    /// </summary>
    [Fact]
    public async Task ActiveTurn_DisablesSignInCommands() {
        var runtime = new CancelableRuntime();
        var model = new NativeChatViewModel(runtime);

        var sendTask = model.SendAsync("long task");
        await runtime.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(model.IsSending);
        Assert.False(model.CanCheckSignIn);
        Assert.False(model.CanStartSignIn);
        Assert.Contains("turn is running", (await model.CheckSignInAsync()).Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("turn is running", (await model.StartSignInAsync()).Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        model.CancelActiveTurn();
        await sendTask;
    }

    /// <summary>
    /// Ensures sign-in checks update native sign-in state without transcript HTML.
    /// </summary>
    [Fact]
    public async Task CheckSignInAsync_UpdatesSignInText() {
        var model = new NativeChatViewModel(
            new ScriptedRuntime(_ => Task.FromResult(CreateTurnResult("", null))) {
                EnsureLoginHandler = _ => Task.FromResult(new NativeLoginResult(true, "user@example.com"))
            });

        var result = await model.CheckSignInAsync();

        Assert.True(result.IsAuthenticated);
        Assert.Equal("Signed in: user@example.com", model.SignInText);
        Assert.Equal(NativeAuthenticationState.SignedIn, model.AuthenticationState);
        Assert.False(model.CanStartSignIn);
        Assert.Equal("Ready", model.StatusText);
    }

    /// <summary>
    /// Ensures failed sign-in checks expose typed native authentication state for live empty-state UI.
    /// </summary>
    [Fact]
    public async Task CheckSignInAsync_ReportsRequiredAuthenticationState() {
        var model = new NativeChatViewModel(
            new ScriptedRuntime(_ => Task.FromResult(CreateTurnResult("", null))) {
                EnsureLoginHandler = _ => Task.FromResult(new NativeLoginResult(false, null))
            });

        var result = await model.CheckSignInAsync();

        Assert.False(result.IsAuthenticated);
        Assert.Equal("Sign-in required", model.SignInText);
        Assert.Equal(NativeAuthenticationState.Required, model.AuthenticationState);
        Assert.True(model.CanStartSignIn);
        model.Draft = "must not run";
        Assert.False(model.CanSend);
    }

    /// <summary>
    /// Ensures sign-in errors expose a failed state without requiring UI string parsing.
    /// </summary>
    [Fact]
    public async Task CheckSignInAsync_ReportsFailedAuthenticationState() {
        var model = new NativeChatViewModel(
            new ScriptedRuntime(_ => Task.FromResult(CreateTurnResult("", null))) {
                EnsureLoginHandler = _ => Task.FromResult(new NativeLoginResult(false, null, "device code expired"))
            });

        var result = await model.CheckSignInAsync();

        Assert.False(result.IsAuthenticated);
        Assert.Equal("Sign-in failed", model.SignInText);
        Assert.Equal(NativeAuthenticationState.Failed, model.AuthenticationState);
        Assert.Equal("Sign-in failed. Use Sign in to reconnect.", model.StatusText);
        Assert.True(model.CanStartSignIn);
        model.Draft = "must not run";
        Assert.False(model.CanSend);
    }

    /// <summary>
    /// Ensures sign-in flow delegates URL opening and prompt input through native-host callbacks.
    /// </summary>
    [Fact]
    public async Task StartSignInAsync_UsesLoginCallbacksAndUpdatesState() {
        Uri? opened = null;
        NativeLoginPrompt? prompt = null;
        var runtime = new ScriptedRuntime(_ => Task.FromResult(CreateTurnResult("", null))) {
            StartLoginHandler = callbacks => {
                _ = callbacks.OpenUrl(new Uri("https://example.test/login"));
                _ = callbacks.PromptForInput(new NativeLoginPrompt("login-1", "prompt-1", "Paste code"));
                return Task.FromResult(new NativeLoginResult(true, null));
            }
        };
        var model = new NativeChatViewModel(runtime) {
            OpenLoginUrlAsync = uri => {
                opened = uri;
                return Task.CompletedTask;
            },
            PromptForLoginInputAsync = loginPrompt => {
                prompt = loginPrompt;
                return Task.FromResult<string?>("code");
            }
        };

        var result = await model.StartSignInAsync();

        Assert.True(result.IsAuthenticated);
        Assert.Equal("https://example.test/login", opened?.ToString());
        Assert.Equal("login-1", prompt?.LoginId);
        Assert.Equal("Signed in", model.SignInText);
        Assert.Equal(NativeAuthenticationState.SignedIn, model.AuthenticationState);
        Assert.False(model.CanStartSignIn);
    }

    /// <summary>
    /// Ensures selecting a persisted conversation restores its transcript and service thread context.
    /// </summary>
    [Fact]
    public async Task SelectConversationAsync_RestoresTranscriptAndUsesConversationThread() {
        var first = new NativeConversation(
            "chat-first",
            "First chat",
            "thread-first",
            messages: new[] { new NativeChatTranscriptItem("user", "Earlier question", DateTimeOffset.UtcNow) });
        var second = new NativeConversation("chat-second", "Second chat", "thread-second");
        var store = new FakeConversationStore(new NativeConversationWorkspace(new[] { first, second }, second.Id));
        var runtime = new ScriptedRuntime(_ => Task.FromResult(CreateTurnResult("Answer", "thread-first-next")));
        var model = new NativeChatViewModel(runtime, conversationStore: store);
        await model.InitializeConversationsAsync();

        var selected = await model.SelectConversationAsync(first.Id);
        var sent = await model.SendAsync("Follow up");

        Assert.True(selected);
        Assert.True(sent);
        Assert.Equal("Earlier question", model.Transcript[0].Text);
        Assert.Equal("thread-first", Assert.Single(runtime.Requests).ThreadId);
        Assert.Equal("thread-first-next", first.ThreadId);
        Assert.True(store.SaveCount >= 2);
        Assert.Equal(first.Id, store.LastSaved?.ActiveConversationId);
    }

    /// <summary>
    /// Ensures a new chat is a real empty persisted conversation rather than a canned workspace.
    /// </summary>
    [Fact]
    public async Task CreateConversationAsync_CreatesAndPersistsEmptyActiveConversation() {
        var existing = new NativeConversation("chat-existing", "Existing chat");
        var store = new FakeConversationStore(new NativeConversationWorkspace(new[] { existing }, existing.Id));
        var model = new NativeChatViewModel(
            new ScriptedRuntime(_ => Task.FromResult(CreateTurnResult(string.Empty, null))),
            conversationStore: store);
        await model.InitializeConversationsAsync();

        var created = await model.CreateConversationAsync();

        Assert.True(created);
        Assert.Equal("New Chat", model.ActiveConversation.Title);
        Assert.Empty(model.Transcript);
        Assert.Equal(model.ActiveConversation.Id, store.LastSaved?.ActiveConversationId);
        Assert.Equal(2, store.LastSaved?.Conversations.Count);
    }

    /// <summary>
    /// Ensures repeated New actions reuse the current empty draft instead of accumulating blank chats.
    /// </summary>
    [Fact]
    public async Task CreateConversationAsync_ReusesCurrentEmptyDraft() {
        var model = new NativeChatViewModel(
            new ScriptedRuntime(_ => Task.FromResult(CreateTurnResult(string.Empty, null))));
        var originalId = model.ActiveConversation.Id;

        var created = await model.CreateConversationAsync();

        Assert.True(created);
        Assert.Equal(originalId, model.ActiveConversation.Id);
        Assert.Single(model.Conversations);
    }

    /// <summary>
    /// Ensures New selects an existing background draft instead of creating another blank chat.
    /// </summary>
    [Fact]
    public async Task CreateConversationAsync_ReusesExistingInactiveEmptyDraft() {
        var existing = new NativeConversation("chat-existing", "Existing chat");
        var draft = new NativeConversation("chat-draft", "New Chat");
        var store = new FakeConversationStore(new NativeConversationWorkspace(new[] { existing, draft }, existing.Id));
        var model = new NativeChatViewModel(
            new ScriptedRuntime(_ => Task.FromResult(CreateTurnResult(string.Empty, null))),
            conversationStore: store);
        await model.InitializeConversationsAsync();

        var created = await model.CreateConversationAsync();

        Assert.True(created);
        Assert.Equal(draft.Id, model.ActiveConversation.Id);
        Assert.Equal(2, model.Conversations.Count);
        Assert.Single(model.Conversations, conversation => conversation.IsEmptyDraft);
        Assert.Equal(draft.Id, store.LastSaved?.ActiveConversationId);
    }

    /// <summary>
    /// Ensures user transcript items project content immediately for native rendering.
    /// </summary>
    [Fact]
    public void NativeChatTranscriptItem_ProjectsUserContentImmediately() {
        var item = new NativeChatTranscriptItem("user", "hello", DateTimeOffset.Now);

        var content = Assert.Single(item.Content);
        Assert.Equal("hello", content.Text);
    }

    /// <summary>
    /// Ensures assistant transcript items defer projection while streaming and project when complete.
    /// </summary>
    [Fact]
    public void NativeChatTranscriptItem_DefersAssistantProjectionUntilFinalStatus() {
        var item = new NativeChatTranscriptItem("assistant", string.Empty, DateTimeOffset.Now, "Waiting for runtime...");

        item.AppendText("**hello**");

        Assert.Empty(item.Content);

        item.Status = "Complete";

        var content = Assert.Single(item.Content);
        Assert.Contains("hello", content.Text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures native transcript export preserves the original Markdown body for OfficeIMO export.
    /// </summary>
    [Fact]
    public void NativeTranscriptMarkdownFormatter_FormatsPortableTranscriptMarkdown() {
        var created = new DateTimeOffset(2026, 6, 13, 20, 10, 0, TimeSpan.Zero);
        var items = new[] {
            new NativeChatTranscriptItem("user", "Show risky accounts.", created),
            new NativeChatTranscriptItem(
                "assistant",
                """
                Found one account.

                | Account | Risk |
                | --- | --- |
                | svc-sync | High |
                """,
                created.AddMinutes(1),
                "Complete")
        };

        var markdown = NativeTranscriptMarkdownFormatter.Format(items);

        Assert.Contains("### User - ", markdown, StringComparison.Ordinal);
        Assert.Contains("Show risky accounts.", markdown, StringComparison.Ordinal);
        Assert.Contains("### Assistant - ", markdown, StringComparison.Ordinal);
        Assert.Contains("| Account | Risk |", markdown, StringComparison.Ordinal);
        Assert.Contains("| svc-sync | High |", markdown, StringComparison.Ordinal);
    }

    private static ChatTurnRunResult CreateTurnResult(
        string text,
        string? threadId,
        string? effectiveModel = null) =>
        new(new ChatResultMessage {
            Kind = ChatServiceMessageKind.Response,
            RequestId = "test-result",
            ThreadId = threadId ?? string.Empty,
            Text = text
        }, string.IsNullOrWhiteSpace(effectiveModel)
            ? null
            : new ChatMetricsMessage {
                Kind = ChatServiceMessageKind.Event,
                RequestId = "test-result",
                ThreadId = threadId ?? string.Empty,
                StartedAtUtc = DateTime.UtcNow.AddSeconds(-1),
                CompletedAtUtc = DateTime.UtcNow,
                Outcome = "ok",
                Model = effectiveModel
            });

    private sealed class ScriptedRuntime : INativeChatRuntime {
        private readonly Func<TestTurnUpdates, Task<ChatTurnRunResult>> _handler;

        public ScriptedRuntime(Func<TestTurnUpdates, Task<ChatTurnRunResult>> handler) {
            _handler = handler;
        }

        public List<ChatRequest> Requests { get; } = new();

        public Func<Func<string, Task>, Task<NativeLoginResult>> EnsureLoginHandler { get; init; } =
            _ => Task.FromResult(new NativeLoginResult(false, null));

        public Func<NativeLoginCallbacks, Task<NativeLoginResult>> StartLoginHandler { get; init; } =
            _ => Task.FromResult(new NativeLoginResult(false, null));

        public async Task<ChatTurnRunResult> RunTurnAsync(
            ChatRequest request,
            Func<ChatTurnUpdate, CancellationToken, ValueTask>? onUpdate,
            CancellationToken cancellationToken) {
            Requests.Add(request);
            cancellationToken.ThrowIfCancellationRequested();
            return await _handler(new TestTurnUpdates(onUpdate, cancellationToken)).ConfigureAwait(false);
        }

        public Task CancelTurnAsync(string requestId, CancellationToken cancellationToken) {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task<NativeLoginResult> EnsureLoginAsync(
            Func<string, Task> status,
            CancellationToken cancellationToken) {
            cancellationToken.ThrowIfCancellationRequested();
            return EnsureLoginHandler(status);
        }

        public Task<NativeLoginResult> StartLoginAsync(
            NativeLoginCallbacks callbacks,
            CancellationToken cancellationToken) {
            cancellationToken.ThrowIfCancellationRequested();
            return StartLoginHandler(callbacks);
        }
    }

    private sealed class FakeConversationStore : INativeConversationStore {
        private readonly NativeConversationWorkspace _workspace;

        public FakeConversationStore(NativeConversationWorkspace workspace) {
            _workspace = workspace;
        }

        public int SaveCount { get; private set; }

        public NativeConversationWorkspace? LastSaved { get; private set; }

        public Task<NativeConversationWorkspace> LoadAsync(CancellationToken cancellationToken) {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_workspace);
        }

        public Task SaveAsync(NativeConversationWorkspace workspace, CancellationToken cancellationToken) {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCount++;
            LastSaved = workspace;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ThrowingConversationStore : INativeConversationStore {
        public Task<NativeConversationWorkspace> LoadAsync(CancellationToken cancellationToken) =>
            throw new InvalidDataException("bad persisted JSON");

        public Task SaveAsync(NativeConversationWorkspace workspace, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class BlockingConversationStore : INativeConversationStore {
        private int _saveCount;

        public TaskCompletionSource SaveStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseSave { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<NativeConversationWorkspace> LoadAsync(CancellationToken cancellationToken) {
            cancellationToken.ThrowIfCancellationRequested();
            var conversation = NativeConversation.CreateNew();
            return Task.FromResult(new NativeConversationWorkspace([conversation], conversation.Id));
        }

        public async Task SaveAsync(NativeConversationWorkspace workspace, CancellationToken cancellationToken) {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref _saveCount) != 1) {
                return;
            }

            SaveStarted.TrySetResult();
            await ReleaseSave.Task.WaitAsync(cancellationToken);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CancelableRuntime : INativeChatRuntime {
        public List<ChatRequest> Requests { get; } = new();

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<string> Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ChatTurnRunResult> RunTurnAsync(
            ChatRequest request,
            Func<ChatTurnUpdate, CancellationToken, ValueTask>? onUpdate,
            CancellationToken cancellationToken) {
            Requests.Add(request);
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return CreateTurnResult(string.Empty, null);
        }

        public Task CancelTurnAsync(string requestId, CancellationToken cancellationToken) {
            cancellationToken.ThrowIfCancellationRequested();
            Canceled.TrySetResult(requestId);
            return Task.CompletedTask;
        }

        public Task<NativeLoginResult> EnsureLoginAsync(
            Func<string, Task> status,
            CancellationToken cancellationToken) =>
            Task.FromResult(new NativeLoginResult(false, null));

        public Task<NativeLoginResult> StartLoginAsync(
            NativeLoginCallbacks callbacks,
            CancellationToken cancellationToken) =>
            Task.FromResult(new NativeLoginResult(false, null));
    }

    private sealed class TestTurnUpdates {
        private readonly Func<ChatTurnUpdate, CancellationToken, ValueTask>? _callback;
        private readonly CancellationToken _cancellationToken;

        public TestTurnUpdates(
            Func<ChatTurnUpdate, CancellationToken, ValueTask>? callback,
            CancellationToken cancellationToken) {
            _callback = callback;
            _cancellationToken = cancellationToken;
        }

        public ValueTask Status(string message) => PublishAsync(new ChatTurnStatusUpdate(new ChatStatusMessage {
            Kind = ChatServiceMessageKind.Event,
            RequestId = "test-request",
            ThreadId = "test-thread",
            Status = "thinking",
            Message = message
        }));

        public ValueTask Delta(string text) => PublishAsync(new ChatTurnDeltaUpdate(new ChatDeltaMessage {
            Kind = ChatServiceMessageKind.Event,
            RequestId = "test-request",
            ThreadId = "test-thread",
            Text = text
        }));

        public ValueTask Provisional(string text) => PublishAsync(new ChatTurnProvisionalUpdate(new ChatAssistantProvisionalMessage {
            Kind = ChatServiceMessageKind.Event,
            RequestId = "test-request",
            ThreadId = "test-thread",
            Text = text
        }));

        public ValueTask Interim(string text) => PublishAsync(new ChatTurnInterimUpdate(new ChatInterimResultMessage {
            Kind = ChatServiceMessageKind.Event,
            RequestId = "test-request",
            ThreadId = "test-thread",
            Text = text
        }));

        private ValueTask PublishAsync(ChatTurnUpdate update) =>
            _callback is null ? ValueTask.CompletedTask : _callback(update, _cancellationToken);
    }
}
