using System;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer;

namespace IntelligenceX.PowerShell;

public abstract class IntelligenceXCmdlet : AsyncPSCmdlet {
    protected IntelligenceXClient ResolveClient(IntelligenceXClient? client) {
        var resolved = client ?? ClientContext.DefaultClient;
        if (resolved is null) {
            throw new InvalidOperationException("No active IntelligenceX client. Use Connect-IntelligenceX first.");
        }
        return resolved;
    }

    protected AppServerClient ResolveAppServerClient(IntelligenceXClient? client) {
        return ResolveClient(client).RequireAppServer();
    }

    protected void SetDefaultClient(IntelligenceXClient client) {
        ClientContext.DefaultClient = client;
    }

    protected void ClearDefaultClient(IntelligenceXClient client) {
        if (ReferenceEquals(ClientContext.DefaultClient, client)) {
            ClientContext.DefaultClient = null;
        }
    }
}
