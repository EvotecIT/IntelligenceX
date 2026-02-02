using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.AppServer.Models;

namespace IntelligenceX.OpenAI.Fluent;

/// <summary>
/// Fluent wrapper for operations on a specific thread.
/// </summary>
public sealed class FluentThreadSession {
    internal FluentThreadSession(FluentSession session, ThreadInfo thread) {
        Session = session;
        Thread = thread;
    }

    /// <summary>
    /// Gets the parent fluent session.
    /// </summary>
    public FluentSession Session { get; }
    /// <summary>
    /// Gets the thread info.
    /// </summary>
    public ThreadInfo Thread { get; }

    /// <summary>
    /// Sends a text-only turn.
    /// </summary>
    /// <param name="text">Prompt text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<TurnInfo> SendAsync(string text, CancellationToken cancellationToken = default) {
        return Session.Client.StartTurnAsync(Thread.Id, text, cancellationToken);
    }

    /// <summary>
    /// Sends a text-only turn with overrides.
    /// </summary>
    /// <param name="text">Prompt text.</param>
    /// <param name="model">Optional model override.</param>
    /// <param name="currentDirectory">Optional working directory.</param>
    /// <param name="approvalPolicy">Optional approval policy.</param>
    /// <param name="sandboxPolicy">Optional sandbox policy.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<TurnInfo> SendAsync(string text, string? model, string? currentDirectory, string? approvalPolicy,
        SandboxPolicy? sandboxPolicy, CancellationToken cancellationToken = default) {
        return Session.Client.StartTurnAsync(Thread.Id, text, model, currentDirectory, approvalPolicy, sandboxPolicy, cancellationToken);
    }

    /// <summary>
    /// Rolls back the thread by a number of turns.
    /// </summary>
    /// <param name="turns">Number of turns to roll back.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<ThreadInfo> RollbackAsync(int turns, CancellationToken cancellationToken = default) {
        return Session.Client.RollbackThreadAsync(Thread.Id, turns, cancellationToken);
    }

    /// <summary>
    /// Interrupts a running turn.
    /// </summary>
    /// <param name="turnId">Turn id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task InterruptAsync(string turnId, CancellationToken cancellationToken = default) {
        return Session.Client.InterruptTurnAsync(Thread.Id, turnId, cancellationToken);
    }
}
