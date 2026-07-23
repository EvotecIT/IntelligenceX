using System;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Client;

namespace IntelligenceX.Chat.App.Native;

internal sealed record NativeLoginPrompt(string LoginId, string PromptId, string PromptText);

internal sealed record NativeLoginResult(
    bool IsAuthenticated,
    string? AccountId,
    string? Error = null,
    bool IsCanceled = false);

internal sealed class NativeLoginCallbacks {
    public Func<string, Task> Status { get; init; } = _ => Task.CompletedTask;

    public Func<Uri, Task> OpenUrl { get; init; } = _ => Task.CompletedTask;

    public Func<NativeLoginPrompt, Task<string?>> PromptForInput { get; init; } = _ => Task.FromResult<string?>(null);
}

/// <summary>
/// Native-only authentication additions over the shared chat runtime contract.
/// </summary>
internal interface INativeChatRuntime {
    Task<ChatTurnRunResult> RunTurnAsync(
        ChatRequest request,
        Func<ChatTurnUpdate, CancellationToken, ValueTask>? onUpdate,
        CancellationToken cancellationToken);

    Task CancelTurnAsync(
        string requestId,
        CancellationToken cancellationToken);

    Task<NativeLoginResult> EnsureLoginAsync(
        Func<string, Task> status,
        CancellationToken cancellationToken);

    Task<NativeLoginResult> StartLoginAsync(
        NativeLoginCallbacks callbacks,
        CancellationToken cancellationToken);
}
