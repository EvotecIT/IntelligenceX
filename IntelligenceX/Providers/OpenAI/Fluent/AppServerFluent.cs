using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;

namespace IntelligenceX.OpenAI.Fluent;

/// <summary>
/// Fluent helpers for the app-server transport.
/// </summary>
/// <example>
/// <code>
/// await using var session = await AppServerFluent.StartAsync();
/// var login = await session.LoginChatGptAsync();
/// await login.WaitAsync();
/// </code>
/// </example>
public static class AppServerFluent {
    /// <summary>Starts an app-server client and returns a fluent session wrapper.</summary>
    /// <param name="options">Optional app-server options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public static async Task<FluentSession> StartAsync(AppServerOptions? options = null, CancellationToken cancellationToken = default) {
        var client = await AppServerClient.StartAsync(options, cancellationToken).ConfigureAwait(false);
        return new FluentSession(client);
    }
}
