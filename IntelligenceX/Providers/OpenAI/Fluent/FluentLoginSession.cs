using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer.Models;

namespace IntelligenceX.OpenAI.Fluent;

public sealed class FluentLoginSession {
    internal FluentLoginSession(FluentSession session, ChatGptLoginStart login) {
        Session = session;
        Login = login;
    }

    public FluentSession Session { get; }
    public ChatGptLoginStart Login { get; }

    public async Task<FluentSession> WaitAsync(CancellationToken cancellationToken = default) {
        await Session.Client.WaitForLoginCompletionAsync(Login.LoginId, cancellationToken).ConfigureAwait(false);
        return Session;
    }
}
