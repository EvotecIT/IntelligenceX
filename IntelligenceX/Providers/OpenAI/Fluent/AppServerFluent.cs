using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;

namespace IntelligenceX.OpenAI.Fluent;

public static class AppServerFluent {
    public static async Task<FluentSession> StartAsync(AppServerOptions? options = null, CancellationToken cancellationToken = default) {
        var client = await AppServerClient.StartAsync(options, cancellationToken).ConfigureAwait(false);
        return new FluentSession(client);
    }
}
