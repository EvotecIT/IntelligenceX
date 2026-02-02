using System;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer;

namespace IntelligenceX.PowerShell;

/// <summary>
/// Base cmdlet with shared client resolution helpers for IntelligenceX operations.
/// </summary>
public abstract class IntelligenceXCmdlet : AsyncPSCmdlet {
    /// <summary>
    /// Resolves the provided client or falls back to the active default client.
    /// </summary>
    /// <param name="client">Explicit client instance to use, or null to use the default.</param>
    /// <returns>The resolved IntelligenceX client.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no active client is available.</exception>
    protected IntelligenceXClient ResolveClient(IntelligenceXClient? client) {
        var resolved = client ?? ClientContext.DefaultClient;
        if (resolved is null) {
            throw new InvalidOperationException("No active IntelligenceX client. Use Connect-IntelligenceX first.");
        }
        return resolved;
    }

    /// <summary>
    /// Resolves a client and returns its app-server-capable wrapper.
    /// </summary>
    /// <param name="client">Explicit client instance to use, or null to use the default.</param>
    /// <returns>The resolved app server client.</returns>
    protected AppServerClient ResolveAppServerClient(IntelligenceXClient? client) {
        return ResolveClient(client).RequireAppServer();
    }

    /// <summary>
    /// Sets the default client used by cmdlets when no client is supplied.
    /// </summary>
    /// <param name="client">Client to store as the default.</param>
    protected void SetDefaultClient(IntelligenceXClient client) {
        ClientContext.DefaultClient = client;
    }

    /// <summary>
    /// Clears the default client when it matches the provided instance.
    /// </summary>
    /// <param name="client">Client to clear from the default slot.</param>
    protected void ClearDefaultClient(IntelligenceXClient client) {
        if (ReferenceEquals(ClientContext.DefaultClient, client)) {
            ClientContext.DefaultClient = null;
        }
    }
}
