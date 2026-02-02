using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer.Models;

namespace IntelligenceX.OpenAI.Fluent;

/// <summary>
/// Represents a fluent login session wrapper.
/// </summary>
public sealed class FluentLoginSession {
    internal FluentLoginSession(FluentSession session, ChatGptLoginStart login) {
        Session = session;
        Login = login;
    }

    /// <summary>
    /// Gets the parent fluent session.
    /// </summary>
    public FluentSession Session { get; }
    /// <summary>
    /// Gets the login start response.
    /// </summary>
    public ChatGptLoginStart Login { get; }

    /// <summary>
    /// Waits for login completion and returns the fluent session.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<FluentSession> WaitAsync(CancellationToken cancellationToken = default) {
        await Session.Client.WaitForLoginCompletionAsync(Login.LoginId, cancellationToken).ConfigureAwait(false);
        return Session;
    }
}
