using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.AppServer.Models;

namespace IntelligenceX.OpenAI.Fluent;

/// <summary>
/// Fluent wrapper for a single thread.
/// </summary>
/// <example>
/// <code>
/// var thread = await session.StartThreadAsync("gpt-5.2-codex");
/// var turn = await thread.SendAsync("Draft release notes.");
/// </code>
/// </example>
public sealed class FluentThreadSession {
    internal FluentThreadSession(FluentSession session, ThreadInfo thread) {
        Session = session;
        Thread = thread;
    }

    /// <summary>Parent fluent session.</summary>
    public FluentSession Session { get; }
    /// <summary>Thread metadata.</summary>
    public ThreadInfo Thread { get; }

    /// <summary>Sends a message to the thread.</summary>
    public Task<TurnInfo> SendAsync(string text, CancellationToken cancellationToken = default) {
        return Session.Client.StartTurnAsync(Thread.Id, text, cancellationToken);
    }

    /// <summary>Sends a message with per-request overrides.</summary>
    public Task<TurnInfo> SendAsync(string text, string? model, string? currentDirectory, string? approvalPolicy,
        SandboxPolicy? sandboxPolicy, CancellationToken cancellationToken = default) {
        return Session.Client.StartTurnAsync(Thread.Id, text, model, currentDirectory, approvalPolicy, sandboxPolicy, cancellationToken);
    }

    /// <summary>Rolls back the last N turns.</summary>
    public Task<ThreadInfo> RollbackAsync(int turns, CancellationToken cancellationToken = default) {
        return Session.Client.RollbackThreadAsync(Thread.Id, turns, cancellationToken);
    }

    /// <summary>Interrupts a running turn.</summary>
    public Task InterruptAsync(string turnId, CancellationToken cancellationToken = default) {
        return Session.Client.InterruptTurnAsync(Thread.Id, turnId, cancellationToken);
    }
}
