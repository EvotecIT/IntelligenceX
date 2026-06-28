using System;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Chat.App.Native;

internal sealed record NativeChatTurnRequest(string RequestId, string Text, string? ThreadId);

internal sealed record NativeChatTurnResult(string Text, string? ThreadId);

internal sealed record NativeLoginPrompt(string LoginId, string PromptId, string PromptText);

internal sealed record NativeLoginResult(bool IsAuthenticated, string? AccountId, string? Error = null);

internal sealed class NativeChatTurnCallbacks {
    public Func<string, Task> Status { get; init; } = _ => Task.CompletedTask;

    public Func<string, Task> Delta { get; init; } = _ => Task.CompletedTask;

    public Func<string, Task> Interim { get; init; } = _ => Task.CompletedTask;
}

internal sealed class NativeLoginCallbacks {
    public Func<string, Task> Status { get; init; } = _ => Task.CompletedTask;

    public Func<Uri, Task> OpenUrl { get; init; } = _ => Task.CompletedTask;

    public Func<NativeLoginPrompt, Task<string?>> PromptForInput { get; init; } = _ => Task.FromResult<string?>(null);
}

/// <summary>
/// Boundary between the native WinUI shell and the chat runtime.
/// </summary>
internal interface INativeChatTurnRunner {
    Task<NativeChatTurnResult> SendAsync(
        NativeChatTurnRequest request,
        NativeChatTurnCallbacks callbacks,
        CancellationToken cancellationToken);

    Task CancelAsync(
        string requestId,
        CancellationToken cancellationToken);

    Task<NativeLoginResult> EnsureLoginAsync(
        Func<string, Task> status,
        CancellationToken cancellationToken);

    Task<NativeLoginResult> StartLoginAsync(
        NativeLoginCallbacks callbacks,
        CancellationToken cancellationToken);
}
