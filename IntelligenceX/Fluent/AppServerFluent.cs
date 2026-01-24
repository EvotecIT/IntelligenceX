using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.AppServer;

namespace IntelligenceX.Fluent;

public static class AppServerFluent {
    public static async Task<FluentSession> StartAsync(AppServerOptions? options = null, CancellationToken cancellationToken = default) {
        var client = await AppServerClient.StartAsync(options, cancellationToken).ConfigureAwait(false);
        return new FluentSession(client);
    }
}
