using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.App.Native;
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
        var runner = new ScriptedRunner(async callbacks => {
            await callbacks.Status("Thinking").ConfigureAwait(false);
            await callbacks.Delta("Hello").ConfigureAwait(false);
            await callbacks.Delta(" there").ConfigureAwait(false);
            return new NativeChatTurnResult("Hello there.", "thread-1");
        });
        var model = new NativeChatViewModel(runner) {
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
    /// Ensures blank prompts do not create transcript entries.
    /// </summary>
    [Fact]
    public async Task SendDraftAsync_IgnoresBlankPrompt() {
        var model = new NativeChatViewModel(new ScriptedRunner(_ => Task.FromResult(new NativeChatTurnResult("", null)))) {
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
        var model = new NativeChatViewModel(new ScriptedRunner(_ => throw new InvalidOperationException("pipe unavailable")));

        var sent = await model.SendAsync("hello");

        Assert.False(sent);
        Assert.Equal(2, model.Transcript.Count);
        Assert.Equal("Error", model.StatusText);
        Assert.Equal("Error", model.Transcript[1].Status);
        Assert.Contains("pipe unavailable", model.Transcript[1].Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures provisional assistant text replaces the current draft instead of duplicating streamed fragments.
    /// </summary>
    [Fact]
    public async Task SendAsync_InterimReplacesStreamingDraft() {
        var runner = new ScriptedRunner(async callbacks => {
            await callbacks.Delta("Hello").ConfigureAwait(false);
            await callbacks.Interim("Hello").ConfigureAwait(false);
            return new NativeChatTurnResult("Hello final.", "thread-1");
        });
        var model = new NativeChatViewModel(runner);

        var sent = await model.SendAsync("hello");

        Assert.True(sent);
        Assert.Equal("Hello final.", model.Transcript[1].Text);
        Assert.DoesNotContain("HelloHello", model.Transcript[1].Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the Stop command cancels the local wait and forwards cancellation to the service request id.
    /// </summary>
    [Fact]
    public async Task CancelActiveTurn_ForwardsCancelRequestToRunner() {
        var runner = new CancelableRunner();
        var model = new NativeChatViewModel(runner);

        var sendTask = model.SendAsync("long task");
        await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        model.CancelActiveTurn();

        await sendTask;
        var canceledRequestId = await runner.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(runner.Requests[0].RequestId, canceledRequestId);
        Assert.Equal("Canceled", model.StatusText);
    }

    /// <summary>
    /// Ensures sign-in checks update native sign-in state without transcript HTML.
    /// </summary>
    [Fact]
    public async Task CheckSignInAsync_UpdatesSignInText() {
        var model = new NativeChatViewModel(
            new ScriptedRunner(_ => Task.FromResult(new NativeChatTurnResult("", null))) {
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
            new ScriptedRunner(_ => Task.FromResult(new NativeChatTurnResult("", null))) {
                EnsureLoginHandler = _ => Task.FromResult(new NativeLoginResult(false, null))
            });

        var result = await model.CheckSignInAsync();

        Assert.False(result.IsAuthenticated);
        Assert.Equal("Sign-in required", model.SignInText);
        Assert.Equal(NativeAuthenticationState.Required, model.AuthenticationState);
        Assert.True(model.CanStartSignIn);
    }

    /// <summary>
    /// Ensures sign-in errors expose a failed state without requiring UI string parsing.
    /// </summary>
    [Fact]
    public async Task CheckSignInAsync_ReportsFailedAuthenticationState() {
        var model = new NativeChatViewModel(
            new ScriptedRunner(_ => Task.FromResult(new NativeChatTurnResult("", null))) {
                EnsureLoginHandler = _ => Task.FromResult(new NativeLoginResult(false, null, "device code expired"))
            });

        var result = await model.CheckSignInAsync();

        Assert.False(result.IsAuthenticated);
        Assert.Equal("Sign-in failed", model.SignInText);
        Assert.Equal(NativeAuthenticationState.Failed, model.AuthenticationState);
        Assert.Equal("Sign-in failed: device code expired", model.StatusText);
        Assert.True(model.CanStartSignIn);
    }

    /// <summary>
    /// Ensures sign-in flow delegates URL opening and prompt input through native-host callbacks.
    /// </summary>
    [Fact]
    public async Task StartSignInAsync_UsesLoginCallbacksAndUpdatesState() {
        Uri? opened = null;
        NativeLoginPrompt? prompt = null;
        var runner = new ScriptedRunner(_ => Task.FromResult(new NativeChatTurnResult("", null))) {
            StartLoginHandler = callbacks => {
                _ = callbacks.OpenUrl(new Uri("https://example.test/login"));
                _ = callbacks.PromptForInput(new NativeLoginPrompt("login-1", "prompt-1", "Paste code"));
                return Task.FromResult(new NativeLoginResult(true, null));
            }
        };
        var model = new NativeChatViewModel(runner) {
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

    private sealed class ScriptedRunner : INativeChatTurnRunner {
        private readonly Func<NativeChatTurnCallbacks, Task<NativeChatTurnResult>> _handler;

        public ScriptedRunner(Func<NativeChatTurnCallbacks, Task<NativeChatTurnResult>> handler) {
            _handler = handler;
        }

        public List<NativeChatTurnRequest> Requests { get; } = new();

        public Func<Func<string, Task>, Task<NativeLoginResult>> EnsureLoginHandler { get; init; } =
            _ => Task.FromResult(new NativeLoginResult(false, null));

        public Func<NativeLoginCallbacks, Task<NativeLoginResult>> StartLoginHandler { get; init; } =
            _ => Task.FromResult(new NativeLoginResult(false, null));

        public async Task<NativeChatTurnResult> SendAsync(
            NativeChatTurnRequest request,
            NativeChatTurnCallbacks callbacks,
            CancellationToken cancellationToken) {
            Requests.Add(request);
            cancellationToken.ThrowIfCancellationRequested();
            return await _handler(callbacks).ConfigureAwait(false);
        }

        public Task CancelAsync(string requestId, CancellationToken cancellationToken) {
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

    private sealed class CancelableRunner : INativeChatTurnRunner {
        public List<NativeChatTurnRequest> Requests { get; } = new();

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<string> Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<NativeChatTurnResult> SendAsync(
            NativeChatTurnRequest request,
            NativeChatTurnCallbacks callbacks,
            CancellationToken cancellationToken) {
            Requests.Add(request);
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return new NativeChatTurnResult(string.Empty, null);
        }

        public Task CancelAsync(string requestId, CancellationToken cancellationToken) {
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
}
