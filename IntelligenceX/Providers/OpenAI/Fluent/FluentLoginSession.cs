using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer.Models;

namespace IntelligenceX.OpenAI.Fluent;

/// <summary>
/// Fluent wrapper for a ChatGPT login session.
/// </summary>
/// <example>
/// <code>
/// var login = await session.LoginChatGptAsync();
/// await login.WaitAsync();
/// </code>
/// </example>
public sealed class FluentLoginSession {
    internal FluentLoginSession(FluentSession session, ChatGptLoginStart login) {
        Session = session;
        Login = login;
    }

    /// <summary>Parent fluent session.</summary>
    public FluentSession Session { get; }
    /// <summary>Login details returned by the server.</summary>
    public ChatGptLoginStart Login { get; }

    /// <summary>Waits for the login flow to complete.</summary>
    public async Task<FluentSession> WaitAsync(CancellationToken cancellationToken = default) {
        await Session.Client.WaitForLoginCompletionAsync(Login.LoginId, cancellationToken).ConfigureAwait(false);
        return Session;
    }
}
