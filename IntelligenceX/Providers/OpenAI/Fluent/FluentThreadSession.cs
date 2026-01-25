using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.AppServer.Models;

namespace IntelligenceX.OpenAI.Fluent;

public sealed class FluentThreadSession {
    internal FluentThreadSession(FluentSession session, ThreadInfo thread) {
        Session = session;
        Thread = thread;
    }

    public FluentSession Session { get; }
    public ThreadInfo Thread { get; }

    public Task<TurnInfo> SendAsync(string text, CancellationToken cancellationToken = default) {
        return Session.Client.StartTurnAsync(Thread.Id, text, cancellationToken);
    }

    public Task<TurnInfo> SendAsync(string text, string? model, string? currentDirectory, string? approvalPolicy,
        SandboxPolicy? sandboxPolicy, CancellationToken cancellationToken = default) {
        return Session.Client.StartTurnAsync(Thread.Id, text, model, currentDirectory, approvalPolicy, sandboxPolicy, cancellationToken);
    }

    public Task<ThreadInfo> RollbackAsync(int turns, CancellationToken cancellationToken = default) {
        return Session.Client.RollbackThreadAsync(Thread.Id, turns, cancellationToken);
    }

    public Task InterruptAsync(string turnId, CancellationToken cancellationToken = default) {
        return Session.Client.InterruptTurnAsync(Thread.Id, turnId, cancellationToken);
    }
}
