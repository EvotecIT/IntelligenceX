using System;
using IntelligenceX.AppServer;

namespace IntelligenceX.PowerShell;

public abstract class IntelligenceXCmdlet : AsyncPSCmdlet {
    protected AppServerClient ResolveClient(AppServerClient? client) {
        var resolved = client ?? ClientContext.DefaultClient;
        if (resolved is null) {
            throw new InvalidOperationException("No active IntelligenceX client. Use Connect-IntelligenceX first.");
        }
        return resolved;
    }

    protected void SetDefaultClient(AppServerClient client) {
        ClientContext.DefaultClient = client;
    }

    protected void ClearDefaultClient(AppServerClient client) {
        if (ReferenceEquals(ClientContext.DefaultClient, client)) {
            ClientContext.DefaultClient = null;
        }
    }
}
