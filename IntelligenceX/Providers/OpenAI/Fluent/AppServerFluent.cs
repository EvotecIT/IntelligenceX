using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;

namespace IntelligenceX.OpenAI.Fluent;

/// <summary>
/// Fluent helpers for launching the local OpenAI App Server and returning a session wrapper.
/// </summary>
public static class AppServerFluent {
    /// <summary>
    /// Starts the app server with the provided options and returns a fluent session.
    /// </summary>
    /// <param name="options">Optional server options (defaults are used when null).</param>
    /// <param name="cancellationToken">Token to cancel startup.</param>
    /// <returns>A fluent session tied to the running app server.</returns>
    public static async Task<FluentSession> StartAsync(AppServerOptions? options = null, CancellationToken cancellationToken = default) {
        var client = await AppServerClient.StartAsync(options, cancellationToken).ConfigureAwait(false);
        return new FluentSession(client);
    }
}
